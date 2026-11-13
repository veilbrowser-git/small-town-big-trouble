using System;
using System.Collections.Generic;
using UnityEngine;

namespace SmallTown.Interaction
{
    public enum ItemState
    {
        Free,
        Held,
        Stored,
    }

    public enum GrabDecision
    {
        /// <summary>Take it now.</summary>
        Grant,
        Deny,
        /// <summary>The host has been asked; the grab completes (or not) when it answers.</summary>
        Pending,
    }

    /// <summary>
    /// Decides who may hold a shared item. Offline there is none and every grab is
    /// granted. In co-op the networked item implements this and asks the host, so
    /// two players grabbing at once always resolves to exactly one holder.
    /// </summary>
    public interface IGrabAuthority
    {
        bool CanGrab(HandGrabber grabber);
        GrabDecision RequestGrab(HandGrabber grabber);
        void OnGrabbed(HandGrabber grabber);
        void OnReleased();
        void OnStored();
        void OnTakenFromStorage();
    }

    /// <summary>
    /// A physical object that hands can pick up.
    ///
    /// Light items follow one hand. Gripping with the other hand swaps hands.
    ///
    /// Heavy items stay under gravity and each gripping hand pulls with a
    /// limited force. One hand can only drag or tip them; lifting needs two
    /// hands, which may belong to one player or to two friends.
    ///
    /// While held, an item sits on the HeldItem layer, which is not a locomotion
    /// surface: nobody can propel themselves by pushing on a carried object.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class GrabbableItem : MonoBehaviour
    {
        struct Holder
        {
            public HandGrabber grabber;
            public Vector3 itemLocalGrabPoint;
            public Vector3 handLocalPosition;
            public Quaternion handLocalRotation;
        }

        const float LayerRestoreDelay = 0.25f;
        const float LightTeleportDistance = 0.6f;

        [Header("Identity")]
        [Tooltip("Stable ID of the item definition. Used by saves, jobs and shops.")]
        public string itemId = "item";
        public string displayName = "Item";

        [Header("Carrying")]
        [Tooltip("Heavy items need two hands to lift.")]
        public bool heavy;
        [Tooltip("Maximum pull of one hand on a heavy item, in newtons.")]
        [Min(1f)] public float handForce = 170f;
        [Min(0f)] public float handDamping = 30f;
        [Tooltip("A hand that drifts this far from its grip point on a heavy item slips off.")]
        [Min(0.1f)] public float slipDistance = 0.55f;
        [Tooltip("Optional pose the item snaps to in the hand (for tools). Otherwise it keeps the pose it was grabbed at.")]
        public Transform snapPose;

        [Header("Storage")]
        public bool storable = true;
        [Min(1)] public int storageSize = 1;

        readonly List<Holder> holders = new List<Holder>(2);
        Rigidbody body;
        Collider[] colliders;
        int freeLayer;
        float restoreLayerAt = -1f;
        Vector3 storedScale;
        float freeAngularDamping;
        float freeMaxAngularVelocity;
        Renderer[] renderers;
        MaterialPropertyBlock highlightBlock;
        bool highlighted;

        /// <summary>Value of <see cref="HolderId"/> when nobody has the item.</summary>
        public const ulong NoHolder = ulong.MaxValue;

        Transform storageSlot;
        bool hiddenRemotely;

        /// <summary>Network authority for this item, if it is a shared object.</summary>
        public IGrabAuthority Authority { get; set; }
        /// <summary>Client ID of the player holding or storing this item, or <see cref="NoHolder"/>.</summary>
        public ulong HolderId { get; private set; } = NoHolder;
        /// <summary>The last player to have had it. Decides who is paid for a sale and who is blamed for a theft.</summary>
        public ulong LastHolderId { get; private set; } = NoHolder;
        /// <summary>Where the item sits inside its container, relative to the container's slot.</summary>
        public Vector3 StorageOffset { get; set; }

        public event Action<GrabbableItem, HandGrabber> Grabbed;
        public event Action<GrabbableItem, HandGrabber> Released;
        public event Action<GrabbableItem> StateChanged;

        public ItemState State { get; private set; } = ItemState.Free;
        public Rigidbody Body => body;
        public int HolderCount => holders.Count;
        public bool IsHeld => holders.Count > 0;
        public HandGrabber PrimaryHolder => holders.Count > 0 ? holders[0].grabber : null;

        protected virtual void Awake()
        {
            body = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>(true);
            renderers = GetComponentsInChildren<Renderer>(true);
            freeLayer = gameObject.layer;
            freeAngularDamping = body.angularDamping;
            freeMaxAngularVelocity = body.maxAngularVelocity;
        }

        public bool IsHeldBy(HandGrabber grabber)
        {
            for (int i = 0; i < holders.Count; i++)
            {
                if (holders[i].grabber == grabber) return true;
            }
            return false;
        }

        public virtual bool CanGrab(HandGrabber grabber)
        {
            if (!isActiveAndEnabled || State == ItemState.Stored || IsHeldBy(grabber)) return false;
            if (Authority != null && !Authority.CanGrab(grabber)) return false;
            // Held by another player (their hands are not on this machine): hands off.
            if (holders.Count == 0 && State == ItemState.Held) return false;
            if (holders.Count == 0) return true;
            if (heavy) return holders.Count < 2;
            // Light item already in a hand: only that same player's other hand may take it.
            return holders[0].grabber.Rig == grabber.Rig;
        }

        /// <summary>Called by <see cref="HandGrabber"/>. Returns false if the grab was refused.</summary>
        public bool Grab(HandGrabber grabber)
        {
            if (!CanGrab(grabber)) return false;

            if (!heavy && holders.Count == 1)
            {
                // Hand-to-hand swap.
                holders[0].grabber.DropWithoutThrow();
            }

            Transform hand = grabber.transform;
            var holder = new Holder
            {
                grabber = grabber,
                itemLocalGrabPoint = transform.InverseTransformPoint(hand.position),
            };
            if (snapPose != null && !heavy)
            {
                // Place the item so that snapPose lines up with the hand.
                Quaternion itemRotation = Quaternion.Inverse(Quaternion.Inverse(transform.rotation) * snapPose.rotation);
                holder.handLocalRotation = itemRotation;
                holder.handLocalPosition = itemRotation * -(Quaternion.Inverse(transform.rotation) * (snapPose.position - transform.position));
            }
            else
            {
                holder.handLocalPosition = hand.InverseTransformPoint(body.position);
                holder.handLocalRotation = Quaternion.Inverse(hand.rotation) * body.rotation;
            }
            holders.Add(holder);

            if (holders.Count == 1)
            {
                restoreLayerAt = -1f;
                GameLayers.SetLayerRecursively(gameObject, GameLayers.HeldItem);
                body.isKinematic = false;
                body.useGravity = heavy;
                body.maxAngularVelocity = 50f;
                if (heavy) body.angularDamping = Mathf.Max(freeAngularDamping, 4f);
                body.WakeUp();
                HolderId = grabber.OwnerId;
                LastHolderId = HolderId;
                SetState(ItemState.Held);
            }
            Grabbed?.Invoke(this, grabber);
            return true;
        }

        /// <summary>Called by <see cref="HandGrabber"/> when a hand lets go.</summary>
        public void Release(HandGrabber grabber, Vector3 velocity, Vector3 angularVelocity)
        {
            int index = holders.FindIndex(h => h.grabber == grabber);
            if (index < 0) return;
            holders.RemoveAt(index);

            if (holders.Count == 0)
            {
                body.useGravity = true;
                body.angularDamping = freeAngularDamping;
                body.maxAngularVelocity = freeMaxAngularVelocity;
                if (!heavy && State == ItemState.Held)
                {
                    body.linearVelocity = velocity;
                    body.angularVelocity = angularVelocity;
                }
                restoreLayerAt = Time.time + LayerRestoreDelay;
                if (State == ItemState.Held)
                {
                    HolderId = NoHolder;
                    SetState(ItemState.Free);
                    Authority?.OnReleased();
                }
            }
            Released?.Invoke(this, grabber);
        }

        /// <summary>Makes every hand let go, for example when the item is consumed or its owner disconnects.</summary>
        public void ForceReleaseAll()
        {
            for (int i = holders.Count - 1; i >= 0; i--)
            {
                if (i < holders.Count) holders[i].grabber.DropWithoutThrow();
            }
        }

        // ------------------------------------------------------------------ Storage

        /// <summary>Puts the item inside a container: no physics, parented to the slot, shrunk to fit.</summary>
        public void EnterStorage(Transform slot, float fitSize, ulong ownerId = 0)
        {
            ForceReleaseAll();
            HolderId = ownerId;
            LastHolderId = ownerId;
            restoreLayerAt = -1f;
            GameLayers.SetLayerRecursively(gameObject, freeLayer);
            body.isKinematic = true;
            foreach (var c in colliders)
            {
                if (c != null) c.enabled = false;
            }
            storedScale = transform.localScale;

            float largest = 0.01f;
            foreach (var r in renderers)
            {
                if (r != null) largest = Mathf.Max(largest, r.bounds.size.magnitude);
            }
            // Follow the slot rather than parent to it: shared items must stay at the scene
            // root for the network layer, and following works the same offline.
            storageSlot = slot;
            StorageOffset = Vector3.zero;
            transform.SetPositionAndRotation(slot.position, slot.rotation);
            if (largest > fitSize) transform.localScale = storedScale * (fitSize / largest);
            SetState(ItemState.Stored);
            Authority?.OnStored();
        }

        public void ExitStorage(Vector3 position, Quaternion rotation)
        {
            storageSlot = null;
            HolderId = NoHolder;
            transform.localScale = storedScale;
            transform.SetPositionAndRotation(position, rotation);
            foreach (var c in colliders)
            {
                if (c != null) c.enabled = true;
            }
            body.isKinematic = false;
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            SetState(ItemState.Free);
            Authority?.OnTakenFromStorage();
        }

        void LateUpdate()
        {
            if (State == ItemState.Stored && storageSlot != null)
            {
                transform.SetPositionAndRotation(storageSlot.TransformPoint(StorageOffset), storageSlot.rotation);
            }
        }

        /// <summary>
        /// Mirrors what another machine did with this item: who has it and whether
        /// it is free, in a hand or in a bag. Used only for items this machine does
        /// not control. Items in someone else's bag are hidden.
        /// </summary>
        public void SetRemoteState(ItemState state, ulong holderId)
        {
            if (holders.Count > 0 && state != ItemState.Held) ForceReleaseAll();
            HolderId = holderId;
            if (holderId != NoHolder) LastHolderId = holderId;
            storageSlot = null;

            bool hide = state == ItemState.Stored;
            if (hide != hiddenRemotely)
            {
                hiddenRemotely = hide;
                foreach (var r in renderers)
                {
                    if (r != null) r.enabled = !hide;
                }
                foreach (var c in colliders)
                {
                    if (c != null) c.enabled = !hide;
                }
            }
            SetState(state);
        }

        void SetState(ItemState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(this);
        }

        // ------------------------------------------------------------------ Physics

        protected virtual void FixedUpdate()
        {
            if (restoreLayerAt > 0f && Time.time >= restoreLayerAt && holders.Count == 0 && State == ItemState.Free)
            {
                restoreLayerAt = -1f;
                GameLayers.SetLayerRecursively(gameObject, freeLayer);
            }
            if (holders.Count == 0) return;
            StepHeld(Time.fixedDeltaTime);
        }

        /// <summary>Advances the held-item physics drive. Public so tests can step it deterministically.</summary>
        public void StepHeld(float dt)
        {
            if (holders.Count == 0 || dt <= 0f) return;
            if (heavy) DriveHeavy();
            else DriveLight(dt);
        }

        void DriveLight(float dt)
        {
            Holder holder = holders[0];
            Transform hand = holder.grabber.transform;
            Vector3 targetPosition = hand.TransformPoint(holder.handLocalPosition);
            Quaternion targetRotation = hand.rotation * holder.handLocalRotation;

            Vector3 toTarget = targetPosition - body.position;
            if (toTarget.magnitude > LightTeleportDistance)
            {
                // Left behind (teleport, respawn, snagged on a doorway): bring it to the hand.
                body.position = targetPosition;
                body.rotation = targetRotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                return;
            }

            body.linearVelocity = Vector3.ClampMagnitude(toTarget / dt, 40f);

            Quaternion delta = targetRotation * Quaternion.Inverse(body.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Mathf.Abs(angle) < 0.05f || float.IsNaN(axis.x) || float.IsInfinity(axis.x))
            {
                body.angularVelocity = Vector3.zero;
            }
            else
            {
                body.angularVelocity = Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / dt), 50f);
            }
        }

        void DriveHeavy()
        {
            float stiffness = handForce / 0.12f;
            HandGrabber slipped = null;
            for (int i = 0; i < holders.Count; i++)
            {
                Holder holder = holders[i];
                Vector3 grabPoint = transform.TransformPoint(holder.itemLocalGrabPoint);
                Vector3 error = holder.grabber.transform.position - grabPoint;
                if (error.magnitude > slipDistance)
                {
                    slipped = holder.grabber;
                    continue;
                }
                Vector3 force = error * stiffness - body.GetPointVelocity(grabPoint) * handDamping;
                body.AddForceAtPosition(Vector3.ClampMagnitude(force, handForce), grabPoint, ForceMode.Force);
            }
            if (slipped != null) slipped.DropWithoutThrow(true);
        }

        // ------------------------------------------------------------------ Hover highlight

        public void SetHighlighted(bool on)
        {
            if (highlighted == on) return;
            highlighted = on;
            if (highlightBlock == null) highlightBlock = new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (on)
                {
                    Color baseColor = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")
                        ? r.sharedMaterial.GetColor("_BaseColor")
                        : Color.white;
                    r.GetPropertyBlock(highlightBlock);
                    highlightBlock.SetColor("_BaseColor", Color.Lerp(baseColor, Color.white, 0.45f));
                    r.SetPropertyBlock(highlightBlock);
                }
                else
                {
                    r.SetPropertyBlock(null);
                }
            }
        }

        protected virtual void OnDisable()
        {
            if (holders.Count > 0) ForceReleaseAll();
        }
    }
}
