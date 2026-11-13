using System;
using UnityEngine;

namespace SmallTown.Jobs
{
    /// <summary>Tracks the one job the player is doing. The host can reset a stuck job with <see cref="CancelActive"/>.</summary>
    public sealed class JobManager : MonoBehaviour
    {
        public static JobManager Instance { get; private set; }

        public Job Active { get; private set; }
        public event Action<Job> ActiveChanged;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetActive(Job job)
        {
            Active = job;
            ActiveChanged?.Invoke(job);
        }

        public void ClearActive(Job job)
        {
            if (Active != job) return;
            Active = null;
            ActiveChanged?.Invoke(null);
        }

        public void CancelActive()
        {
            if (Active != null) Active.Cancel();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;
    }
}
