using System;
using UnityEngine;

namespace SmallTown.Crime
{
    /// <summary>
    /// One player's wanted status. Each player has their own tracker; standing
    /// next to a wanted friend never makes you wanted. Escaping means staying
    /// unseen by police for the full escape time.
    /// </summary>
    public sealed class WantedTracker
    {
        public const int MaxLevel = 3;

        readonly float escapeSeconds;
        readonly float escalateSeconds;
        float unseenTime;
        float chasedTime;

        public int Level { get; private set; }
        public bool IsWanted => Level > 0;
        public bool SeenByPolice { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        /// <summary>0 when just seen, 1 when about to get away.</summary>
        public float EscapeProgress => Level == 0 ? 0f : Mathf.Clamp01(unseenTime / escapeSeconds);

        public event Action<int> LevelChanged;
        /// <summary>The player got away. The argument is the level they escaped from.</summary>
        public event Action<int> Escaped;
        /// <summary>A crime was reported: level asked for, where.</summary>
        public event Action<int, Vector3> CrimeReported;

        public WantedTracker(float escapeSeconds, float escalateSeconds)
        {
            this.escapeSeconds = Mathf.Max(0.1f, escapeSeconds);
            this.escalateSeconds = Mathf.Max(0.1f, escalateSeconds);
        }

        /// <summary>A crime was reported to the police. The level only ever goes up from reports.</summary>
        public void ReportCrime(int level, Vector3 position)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            LastKnownPosition = position;
            unseenTime = 0f;
            CrimeReported?.Invoke(level, position);
            if (level > Level) SetLevel(level);
        }

        public void Tick(float deltaTime, bool seenByPolice, Vector3 playerPosition)
        {
            SeenByPolice = Level > 0 && seenByPolice;
            if (Level == 0) return;

            if (seenByPolice)
            {
                unseenTime = 0f;
                LastKnownPosition = playerPosition;
                chasedTime += deltaTime;
                if (Level == 2 && chasedTime >= escalateSeconds) SetLevel(3);
            }
            else
            {
                unseenTime += deltaTime;
                if (unseenTime >= escapeSeconds)
                {
                    int from = Level;
                    Clear();
                    Escaped?.Invoke(from);
                }
            }
        }

        /// <summary>Resets to clean without counting as an escape (arrest, host reset).</summary>
        public void Clear()
        {
            unseenTime = 0f;
            chasedTime = 0f;
            SeenByPolice = false;
            if (Level != 0) SetLevel(0);
        }

        void SetLevel(int level)
        {
            Level = level;
            if (level < 2) chasedTime = 0f;
            LevelChanged?.Invoke(level);
        }
    }
}
