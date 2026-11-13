using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>
    /// Kinematic platform that glides between two points. The locomotion solver
    /// carries anyone standing or holding on, without treating the platform's
    /// motion as a push.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MovingPlatform : MonoBehaviour
    {
        public Vector3 travel = new Vector3(4f, 0f, 0f);
        [Min(0.1f)] public float secondsPerLeg = 4f;
        [Min(0f)] public float pauseSeconds = 1f;

        Rigidbody body;
        Vector3 origin;
        float time;

        public Vector3 Velocity { get; private set; }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            origin = transform.position;
        }

        void FixedUpdate() => Advance(Time.fixedDeltaTime, false);

        /// <summary>
        /// Moves the platform along its path. Tests pass <paramref name="immediate"/>
        /// to move the transform at once instead of waiting for the physics step.
        /// </summary>
        public void Advance(float dt, bool immediate = true)
        {
            time += dt;
            float cycle = (secondsPerLeg + pauseSeconds) * 2f;
            float t = Mathf.Repeat(time, cycle);
            float leg = secondsPerLeg + pauseSeconds;
            float along = t < leg
                ? Mathf.Clamp01(t / secondsPerLeg)
                : 1f - Mathf.Clamp01((t - leg) / secondsPerLeg);
            along = Mathf.SmoothStep(0f, 1f, along);

            Vector3 next = origin + travel * along;
            Velocity = dt > 0f ? (next - transform.position) / dt : Vector3.zero;
            if (immediate) transform.position = next;
            else body.MovePosition(next);
        }
    }
}
