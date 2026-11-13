using SmallTown.Locomotion;
using UnityEngine;
using UnityEngine.Events;

namespace SmallTown.Interaction
{
    /// <summary>
    /// A big physical button. Slap it with either hand to press it. Used for
    /// world-space choices so menus need no laser pointer.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PunchButton : MonoBehaviour
    {
        public UnityEvent onPressed = new UnityEvent();
        /// <summary>
        /// Set by the network layer. On a client it sends the press to the host and
        /// returns true, so the action runs once, on the host, on that player's behalf.
        /// </summary>
        public System.Func<PunchButton, bool> Relay;
        [Min(0f)] public float cooldown = 0.6f;
        [SerializeField] Transform cap;
        public float pressDepth = 0.03f;

        float readyAt;
        float pressAnimation;
        Vector3 capRest;

        public void SetCap(Transform capTransform) => cap = capTransform;

        void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (cap != null) capRest = cap.localPosition;
        }

        void OnTriggerEnter(Collider other)
        {
            var marker = other.GetComponent<PlayerBodyMarker>();
            if (marker == null || !marker.isHand) return;
            if (Press() && marker.rig != null) marker.rig.Input?.Haptic(marker.hand, 0.7f, 0.06f);
        }

        /// <summary>Presses the button if it is off cooldown. Returns true if it fired.</summary>
        public bool Press()
        {
            if (Time.time < readyAt) return false;
            readyAt = Time.time + cooldown;
            pressAnimation = 1f;
            GameFeedback.Play(FeedbackSound.Click, transform.position, 0.9f);
            if (Relay == null || !Relay(this)) onPressed.Invoke();
            return true;
        }

        void Update()
        {
            if (cap == null || pressAnimation <= 0f) return;
            pressAnimation = Mathf.MoveTowards(pressAnimation, 0f, Time.deltaTime * 5f);
            cap.localPosition = capRest - Vector3.forward * (pressDepth * pressAnimation);
        }
    }
}
