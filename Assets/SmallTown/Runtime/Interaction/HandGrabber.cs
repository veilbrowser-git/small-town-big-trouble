using System;
using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown.Interaction
{
    /// <summary>
    /// Grip-to-grab for one virtual hand. Lives on the hand transform that the
    /// locomotion solver positions, so held items respect walls just like the
    /// hand does. Throws use the real controller's motion.
    /// </summary>
    public sealed class HandGrabber : MonoBehaviour
    {
        const int HistorySize = 12;

        [SerializeField] PlayerRig rig;
        [SerializeField] Hand hand;
        [SerializeField] StorageBag bag;
        [Min(0.02f)] public float grabRadius = 0.13f;
        [Range(0.05f, 0.95f)] public float gripPress = 0.6f;
        [Range(0.05f, 0.95f)] public float gripRelease = 0.35f;
        [Min(0f)] public float throwMultiplier = 1.15f;
        [Min(0.02f)] public float throwWindow = 0.09f;

        readonly Collider[] overlap = new Collider[24];
        readonly Vector3[] historyPosition = new Vector3[HistorySize];
        readonly Quaternion[] historyRotation = new Quaternion[HistorySize];
        readonly float[] historyTime = new float[HistorySize];
        int historyHead;
        int historyCount;
        float clock;
        bool gripDown;
        GrabbableItem hovered;

        public event Action<HandGrabber, GrabbableItem> Grabbed;
        public event Action<HandGrabber, GrabbableItem> Released;

        public PlayerRig Rig => rig;
        public Hand Hand => hand;
        public GrabbableItem Held { get; private set; }
        public bool IsHolding => Held != null;
        public GrabbableItem Hovered => hovered;

        public void Configure(PlayerRig owner, Hand side, StorageBag storage)
        {
            rig = owner;
            hand = side;
            bag = storage;
        }

        void OnEnable()
        {
            if (rig != null) rig.Simulated += Tick;
        }

        void OnDisable()
        {
            if (rig != null) rig.Simulated -= Tick;
            if (Held != null) DropWithoutThrow();
            SetHover(null);
        }

        void Tick(float deltaTime)
        {
            clock += deltaTime;
            RecordHistory();

            var input = rig.Input;
            float grip = input != null && input.IsTracked(hand) ? input.Grip(hand) : 0f;
            bool wasDown = gripDown;
            if (!gripDown && grip >= gripPress) gripDown = true;
            else if (gripDown && grip <= gripRelease) gripDown = false;

            if (Held != null && Held.State != ItemState.Held)
            {
                // The item was taken away from us (stored, consumed, despawned).
                ClearHeld();
            }

            if (gripDown && !wasDown && Held == null) TryGrab();
            else if (!gripDown && wasDown && Held != null) ReleaseHeld();

            // Grip on a yellow climbable surface latches the hand to it. Holding the
            // grip while reaching also works, so climbing is "squeeze, slap, pull".
            var motor = rig.Motor;
            if (gripDown && Held == null && rig.Mode == MovementMode.HandLocomotion)
            {
                if (!motor.IsLatched(hand) && motor.TryLatch(hand))
                {
                    input?.Haptic(hand, 0.6f, 0.05f);
                    GameFeedback.Play(FeedbackSound.Grab, transform.position, 0.5f, 0.8f);
                }
            }
            else if (motor.IsLatched(hand))
            {
                motor.Unlatch(hand);
            }

            SetHover(Held == null ? FindCandidate() : null);
        }

        void RecordHistory()
        {
            Transform source = rig.Controller(hand);
            if (source == null) source = transform;
            historyPosition[historyHead] = source.position;
            historyRotation[historyHead] = source.rotation;
            historyTime[historyHead] = clock;
            historyHead = (historyHead + 1) % HistorySize;
            if (historyCount < HistorySize) historyCount++;
        }

        GrabbableItem FindCandidate()
        {
            int hits = Physics.OverlapSphereNonAlloc(transform.position, grabRadius, overlap,
                GameLayers.Grabbable, QueryTriggerInteraction.Collide);
            GrabbableItem best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hits; i++)
            {
                var item = overlap[i].GetComponentInParent<GrabbableItem>();
                if (item == null || !item.CanGrab(this)) continue;
                float distance = (overlap[i].ClosestPoint(transform.position) - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = item;
                }
            }
            return best;
        }

        void SetHover(GrabbableItem item)
        {
            if (hovered == item) return;
            if (hovered != null) hovered.SetHighlighted(false);
            hovered = item;
            if (hovered != null)
            {
                hovered.SetHighlighted(true);
                rig.Input?.Haptic(hand, 0.15f, 0.02f);
            }
        }

        void TryGrab()
        {
            GrabbableItem candidate = FindCandidate();
            if (candidate == null && bag != null && bag.Contains(transform.position))
            {
                candidate = bag.TakeLast(transform.position, transform.rotation);
            }
            if (candidate == null) return;
            TryGrabItem(candidate);
        }

        /// <summary>Grabs a specific item if allowed. Returns true on success.</summary>
        public bool TryGrabItem(GrabbableItem item)
        {
            if (item == null || Held != null || !item.Grab(this)) return false;
            Held = item;
            // Design rule: a hand that is carrying something does not propel the player
            // (unless the settings asset opts in). The other hand still does.
            rig.Motor.SetHandCanPush(hand, rig.Motor.Settings.occupiedHandsCanPush);
            rig.Input?.Haptic(hand, 0.5f, 0.05f);
            GameFeedback.Play(FeedbackSound.Grab, transform.position, 0.6f);
            Grabbed?.Invoke(this, item);
            return true;
        }

        void ReleaseHeld()
        {
            GrabbableItem item = Held;
            if (bag != null && bag.Contains(transform.position) && bag.TryStore(item, this))
            {
                // TryStore released the item from this hand.
                return;
            }

            EstimateVelocity(out Vector3 velocity, out Vector3 angular);
            ClearHeld();
            item.Release(this, velocity * throwMultiplier, angular);
            GameFeedback.Play(velocity.magnitude > 2f ? FeedbackSound.Whoosh : FeedbackSound.Release, transform.position, 0.5f);
            Released?.Invoke(this, item);
        }

        /// <summary>Lets go without imparting a throw. Used for hand swaps, storage and slips.</summary>
        public void DropWithoutThrow(bool slipped = false)
        {
            GrabbableItem item = Held;
            if (item == null) return;
            ClearHeld();
            item.Release(this, item.Body != null ? item.Body.linearVelocity : Vector3.zero, Vector3.zero);
            if (slipped)
            {
                rig.Input?.Haptic(hand, 0.8f, 0.12f);
                GameFeedback.Play(FeedbackSound.Reject, transform.position, 0.4f);
            }
            Released?.Invoke(this, item);
        }

        void ClearHeld()
        {
            Held = null;
            if (rig != null && rig.Motor != null) rig.Motor.SetHandCanPush(hand, true);
        }

        void EstimateVelocity(out Vector3 velocity, out Vector3 angular)
        {
            velocity = Vector3.zero;
            angular = Vector3.zero;
            if (historyCount < 2) return;

            int newest = (historyHead - 1 + HistorySize) % HistorySize;
            int oldest = newest;
            for (int i = 1; i < historyCount; i++)
            {
                int index = (newest - i + HistorySize) % HistorySize;
                if (historyTime[newest] - historyTime[index] > throwWindow) break;
                oldest = index;
            }
            if (oldest == newest) oldest = (newest - 1 + HistorySize) % HistorySize;

            float span = historyTime[newest] - historyTime[oldest];
            if (span <= 1e-5f) return;
            velocity = (historyPosition[newest] - historyPosition[oldest]) / span;

            Quaternion delta = historyRotation[newest] * Quaternion.Inverse(historyRotation[oldest]);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (!float.IsNaN(axis.x) && !float.IsInfinity(axis.x)) angular = axis * (angle * Mathf.Deg2Rad / span);
        }
    }
}
