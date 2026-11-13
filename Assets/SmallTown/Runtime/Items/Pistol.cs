using System;
using System.Collections.Generic;
using SmallTown.Crime;
using SmallTown.Interaction;
using TMPro;
using UnityEngine;

namespace SmallTown.Items
{
    /// <summary>
    /// A chunky toy-like pistol. Grip to hold, trigger to fire, it reloads
    /// itself when empty. Shots scare people, knock props about and daze
    /// police; nobody is hurt, and other players cannot be hit at all.
    /// </summary>
    [RequireComponent(typeof(GrabbableItem))]
    public sealed class Pistol : MonoBehaviour
    {
        const int HitMask = (1 << GameLayers.World) | (1 << GameLayers.Item) | (1 << GameLayers.Npc) | (1 << GameLayers.Vehicle);

        static readonly List<Pistol> Registry = new List<Pistol>();

        [SerializeField] Transform muzzle;
        [SerializeField] LineRenderer tracer;
        [SerializeField] Transform flash;
        [SerializeField] TMP_Text ammoLabel;
        [Min(1)] public int magazineSize = 6;
        [Min(0)] public int reserveAmmo = 36;
        [Min(0.1f)] public float reloadSeconds = 1.3f;
        [Min(1f)] public float range = 40f;
        [Min(0f)] public float impactForce = 4f;

        GrabbableItem item;
        bool triggerDown;
        float reloadAt = -1f;
        float effectUntil;

        public static IReadOnlyList<Pistol> All => Registry;
        public int Ammo { get; private set; }
        public bool IsHeld => item != null && item.State == ItemState.Held;
        public HandGrabber Holder => item != null ? item.PrimaryHolder : null;
        public Vector3 AimOrigin => muzzle != null ? muzzle.position : transform.position;
        public Vector3 AimDirection => muzzle != null ? muzzle.forward : transform.forward;

        /// <summary>Raised for every shot actually fired.</summary>
        public static event Action<Pistol> Fired;

        public void Configure(Transform muzzlePoint, LineRenderer tracerLine, Transform muzzleFlash, TMP_Text label)
        {
            muzzle = muzzlePoint;
            tracer = tracerLine;
            flash = muzzleFlash;
            ammoLabel = label;
        }

        void Awake()
        {
            item = GetComponent<GrabbableItem>();
            Ammo = magazineSize;
            HideEffects();
            RefreshLabel();
        }

        void OnEnable() => Registry.Add(this);
        void OnDisable() => Registry.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Registry.Clear();
            Fired = null;
        }

        void Update()
        {
            if (Time.time >= effectUntil) HideEffects();

            if (reloadAt >= 0f && Time.time >= reloadAt) FinishReload();

            HandGrabber holder = Holder;
            float pull = holder != null && holder.Rig.Input != null ? holder.Rig.Input.Trigger(holder.Hand) : 0f;
            bool wasDown = triggerDown;
            if (!triggerDown && pull > 0.7f) triggerDown = true;
            else if (triggerDown && pull < 0.4f) triggerDown = false;
            if (triggerDown && !wasDown) Fire();
        }

        /// <summary>Fires one shot if there is ammunition. Returns true if a shot went off.</summary>
        public bool Fire()
        {
            HandGrabber holder = Holder;
            if (Ammo <= 0)
            {
                GameFeedback.Play(FeedbackSound.Click, AimOrigin, 0.8f, 0.7f);
                if (reloadAt < 0f) StartReload();
                return false;
            }

            Ammo--;
            Vector3 origin = AimOrigin;
            Vector3 direction = AimDirection;
            Vector3 end = origin + direction * range;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, range, HitMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                {
                    hit.rigidbody.AddForceAtPosition(direction * impactForce, hit.point, ForceMode.Impulse);
                }
                var shootable = hit.collider.GetComponentInParent<IShootable>();
                shootable?.OnShot(hit.point, direction);
            }

            ShowEffects(origin, end);
            GameFeedback.Play(FeedbackSound.Gunshot, origin);
            if (holder != null) holder.Rig.Input?.Haptic(holder.Hand, 1f, 0.09f);
            CrimeEvents.Raise(CrimeKind.Gunfire, origin, 2);
            Fired?.Invoke(this);

            if (Ammo == 0) StartReload();
            RefreshLabel();
            return true;
        }

        void StartReload()
        {
            if (reserveAmmo <= 0) return;
            reloadAt = Time.time + reloadSeconds;
            if (ammoLabel != null) ammoLabel.text = "...";
        }

        void FinishReload()
        {
            reloadAt = -1f;
            int loaded = Mathf.Min(magazineSize, reserveAmmo);
            reserveAmmo -= loaded;
            Ammo = loaded;
            GameFeedback.Play(FeedbackSound.Click, AimOrigin, 0.9f, 1.3f);
            RefreshLabel();
        }

        /// <summary>Refills everything, for example when the pistol is returned after being lost.</summary>
        public void Refill(int reserve)
        {
            reloadAt = -1f;
            Ammo = magazineSize;
            reserveAmmo = reserve;
            RefreshLabel();
        }

        void ShowEffects(Vector3 from, Vector3 to)
        {
            effectUntil = Time.time + 0.06f;
            if (tracer != null)
            {
                tracer.enabled = true;
                tracer.SetPosition(0, from);
                tracer.SetPosition(1, to);
            }
            if (flash != null) flash.gameObject.SetActive(true);
        }

        void HideEffects()
        {
            if (tracer != null) tracer.enabled = false;
            if (flash != null) flash.gameObject.SetActive(false);
        }

        void RefreshLabel()
        {
            if (ammoLabel != null) ammoLabel.text = Ammo.ToString();
        }
    }
}
