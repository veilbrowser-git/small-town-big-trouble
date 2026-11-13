using UnityEngine;

namespace SmallTown.Networking
{
    /// <summary>
    /// The host's sanity check on where a remote player says they are. Players
    /// move themselves (that is what keeps hand locomotion responsive), so the
    /// host cannot replay their motion; it can only refuse the impossible.
    /// Movement is measured over short windows and anything far beyond the
    /// game's top speed is rejected, unless the player announced a teleport
    /// (respawn, arrest, unstuck) by bumping their teleport counter.
    /// </summary>
    public sealed class MotionValidator
    {
        readonly float maxSpeed;
        readonly float window;
        Vector3 windowStart;
        Vector3 lastValid;
        float windowTime;
        byte lastTeleport;
        bool hasBaseline;

        public int Violations { get; private set; }
        public Vector3 LastValid => lastValid;

        /// <param name="maxSpeed">The fastest a player can legitimately travel, in m/s.</param>
        /// <param name="window">Seconds over which movement is judged.</param>
        public MotionValidator(float maxSpeed, float window = 0.5f)
        {
            this.maxSpeed = maxSpeed;
            this.window = window;
        }

        /// <summary>
        /// Feeds one reported position. Returns false when the report is not
        /// believable; the caller should send the player back to <see cref="LastValid"/>.
        /// </summary>
        public bool Check(Vector3 position, float deltaTime, byte teleportCount)
        {
            if (!hasBaseline || teleportCount != lastTeleport)
            {
                hasBaseline = true;
                lastTeleport = teleportCount;
                windowStart = lastValid = position;
                windowTime = 0f;
                return true;
            }

            windowTime += deltaTime;
            if (windowTime < window) return true;

            // Generous margin: network jitter bunches updates, and a launch can briefly exceed the cap.
            float allowed = maxSpeed * 1.6f * windowTime + 1.5f;
            bool ok = (position - windowStart).magnitude <= allowed;
            if (ok) lastValid = position;
            else Violations++;
            windowStart = ok ? position : lastValid;
            windowTime = 0f;
            return ok;
        }

        /// <summary>The host moved the player itself; accept wherever they turn up next.</summary>
        public void Reset() => hasBaseline = false;
    }
}
