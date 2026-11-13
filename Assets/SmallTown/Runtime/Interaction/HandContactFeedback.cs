using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown.Interaction
{
    /// <summary>Tap sound and haptic pulse whenever a hand lands on a surface.</summary>
    public sealed class HandContactFeedback : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [Min(0f)] public float minSpeed = 0.25f;
        [Min(0.1f)] public float fullSpeed = 3f;

        public void Configure(PlayerRig owner) => rig = owner;

        void OnEnable()
        {
            if (rig != null) rig.HandTouched += OnHandTouched;
        }

        void OnDisable()
        {
            if (rig != null) rig.HandTouched -= OnHandTouched;
        }

        void OnHandTouched(Hand hand, Vector3 position, float speed)
        {
            if (speed < minSpeed) return;
            float strength = Mathf.Clamp01(speed / fullSpeed);
            rig.Input?.Haptic(hand, 0.2f + strength * 0.6f, 0.03f);
            GameFeedback.Play(FeedbackSound.HandTap, position, 0.25f + strength * 0.6f, 0.9f + strength * 0.3f);
        }
    }
}
