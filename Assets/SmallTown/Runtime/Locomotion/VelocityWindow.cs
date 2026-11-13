using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>
    /// Time-weighted average of recent displacement. Sized in seconds rather
    /// than frames so the result does not depend on refresh rate.
    /// </summary>
    public sealed class VelocityWindow
    {
        readonly Vector3[] displacements;
        readonly float[] durations;
        int head;
        int count;

        public float WindowSeconds { get; set; }

        public VelocityWindow(float windowSeconds, int capacity = 64)
        {
            WindowSeconds = windowSeconds;
            displacements = new Vector3[capacity];
            durations = new float[capacity];
        }

        public void Clear()
        {
            head = 0;
            count = 0;
        }

        public void Add(Vector3 displacement, float dt)
        {
            if (dt <= 0f) return;
            displacements[head] = displacement;
            durations[head] = dt;
            head = (head + 1) % displacements.Length;
            if (count < displacements.Length) count++;
        }

        /// <summary>Average velocity over the most recent WindowSeconds.</summary>
        public Vector3 Average()
        {
            Vector3 total = Vector3.zero;
            float time = 0f;
            for (int i = 0; i < count; i++)
            {
                int index = (head - 1 - i + displacements.Length) % displacements.Length;
                float dt = durations[index];
                float remaining = WindowSeconds - time;
                if (remaining <= 0f) break;
                if (dt > remaining)
                {
                    // Take only the part of the oldest sample that fits the window.
                    total += displacements[index] * (remaining / dt);
                    time += remaining;
                    break;
                }
                total += displacements[index];
                time += dt;
            }
            return time > 1e-5f ? total / time : Vector3.zero;
        }
    }
}
