using UnityEngine;

namespace SmallTown
{
    /// <summary>Process-wide runtime settings applied once at startup.</summary>
    public static class GameBootstrap
    {
        /// <summary>Quest 3 / 3S default refresh rate, and the project's performance target.</summary>
        public const int TargetRefreshRate = 72;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Configure()
        {
            // Step physics once per rendered frame at the target rate so held items and
            // shoved props do not visibly lag behind the hands.
            Time.fixedDeltaTime = 1f / TargetRefreshRate;
            Time.maximumDeltaTime = 1f / 15f;
        }
    }
}
