using System;
using SmallTown.Economy;
using UnityEngine;

namespace SmallTown.Jobs
{
    public enum JobState
    {
        Available,
        Active,
    }

    /// <summary>
    /// Base for every legitimate job: accept, track an objective, pay out
    /// exactly once, cancel cleanly. A job run has its own ID, so its payout
    /// transaction can never be applied twice.
    /// </summary>
    public abstract class Job : MonoBehaviour
    {
        [Tooltip("Stable ID of this job type.")]
        public string jobId = "job";
        public string displayName = "Job";

        public JobState State { get; private set; } = JobState.Available;
        public string Objective { get; private set; } = "";
        public string RunId { get; private set; } = "";
        public int CompletedRuns { get; private set; }

        public event Action<Job> Changed;

        /// <summary>Starts a run. Refused if this or any other job is already active.</summary>
        public bool Accept()
        {
            JobManager manager = JobManager.Instance;
            if (!Net.IsHost) return false;
            if (State != JobState.Available || manager == null || manager.Active != null) return false;
            RunId = Guid.NewGuid().ToString("N");
            State = JobState.Active;
            manager.SetActive(this);
            OnAccepted();
            Changed?.Invoke(this);
            return true;
        }

        /// <summary>Abandons the run. Job items are cleaned up and nothing is paid.</summary>
        public void Cancel()
        {
            if (State != JobState.Active) return;
            OnCleanup();
            Finish();
            Announce(displayName + " cancelled.");
        }

        /// <summary>Pays the reward and ends the run. Safe to call more than once: only the first pays.</summary>
        protected bool Complete(int payout, string message)
        {
            if (State != JobState.Active) return false;
            GameSession session = GameSession.Instance;
            bool paid = false;
            if (session != null)
            {
                // Co-op: a job is a team effort, so everyone in the session is paid in full.
                // The run ID keys the transaction, so each wallet can only be paid once.
                foreach (PlayerState player in session.Players)
                {
                    if (player.Wallet.Apply("job:" + jobId + ":" + RunId, payout) != TransactionResult.Applied) continue;
                    player.Wallet.Profile.jobsCompleted++;
                    player.Tell(message + "  +$" + payout);
                    paid = true;
                }
            }
            if (paid)
            {
                CompletedRuns++;
                GameFeedback.PlayShared(FeedbackSound.Success, transform.position);
            }
            OnCleanup();
            Finish();
            return paid;
        }

        /// <summary>Shows a message to every player in the session.</summary>
        protected static void Announce(string message)
        {
            if (GameSession.Instance != null) GameSession.Instance.TellAll(message);
            else HudToast.Show(message);
        }

        protected void SetObjective(string objective)
        {
            if (Objective == objective) return;
            Objective = objective;
            Changed?.Invoke(this);
        }

        void Finish()
        {
            State = JobState.Available;
            Objective = "";
            if (JobManager.Instance != null) JobManager.Instance.ClearActive(this);
            Changed?.Invoke(this);
        }

        protected abstract void OnAccepted();
        /// <summary>Remove anything the run spawned. Called on completion and on cancel.</summary>
        protected abstract void OnCleanup();

        protected virtual void OnDisable()
        {
            if (State == JobState.Active)
            {
                OnCleanup();
                Finish();
            }
        }
    }
}
