using SmallTown.Jobs;
using TMPro;
using UnityEngine;

namespace SmallTown.UI
{
    /// <summary>
    /// The little device on the player's left wrist: money, wanted level, the
    /// current objective, bag space and save feedback. Read-only for now.
    /// </summary>
    public sealed class WristDevice : MonoBehaviour
    {
        [SerializeField] TMP_Text moneyLabel;
        [SerializeField] TMP_Text objectiveLabel;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Renderer[] wantedPips = new Renderer[0];
        [SerializeField] Material pipOn;
        [SerializeField] Material pipOff;

        float statusUntil;
        GameSession hooked;

        public void Configure(TMP_Text money, TMP_Text objective, TMP_Text status, Renderer[] pips, Material on, Material off)
        {
            moneyLabel = money;
            objectiveLabel = objective;
            statusLabel = status;
            wantedPips = pips;
            pipOn = on;
            pipOff = off;
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            if (session != hooked)
            {
                if (hooked != null) hooked.SaveStatusChanged -= OnSaveStatus;
                hooked = session;
                if (hooked != null) hooked.SaveStatusChanged += OnSaveStatus;
            }

            if (session == null || session.Wallet == null)
            {
                if (moneyLabel != null) moneyLabel.text = "";
                if (objectiveLabel != null) objectiveLabel.text = "Movement gym";
                SetPips(0, 0f);
                return;
            }

            if (moneyLabel != null) moneyLabel.text = "$" + session.Wallet.Cash;
            if (objectiveLabel != null)
            {
                Job job = JobManager.Instance != null ? JobManager.Instance.Active : null;
                if (session.Wanted.IsWanted)
                {
                    objectiveLabel.text = session.Wanted.SeenByPolice
                        ? "WANTED: break line of sight"
                        : "Hiding... " + Mathf.CeilToInt((1f - session.Wanted.EscapeProgress) * session.Balance.escapeSeconds) + "s";
                }
                else
                {
                    objectiveLabel.text = job != null ? job.Objective : "No job. Try the pizza place.";
                }
            }
            SetPips(session.Wanted.Level, session.Wanted.EscapeProgress);

            if (statusLabel != null)
            {
                string bag = session.Bag != null ? "Bag " + session.Bag.UsedSpace + "/" + session.Bag.capacity : "";
                if (Time.unscaledTime > statusUntil) statusLabel.text = bag;
            }
        }

        void OnSaveStatus(string status)
        {
            if (statusLabel == null) return;
            statusLabel.text = status;
            statusUntil = Time.unscaledTime + 2f;
        }

        void SetPips(int level, float escapeProgress)
        {
            // While escaping, the lit pips blink faster as the timer runs out.
            bool blinkOff = level > 0 && escapeProgress > 0f && Mathf.Repeat(Time.unscaledTime * (1f + escapeProgress * 4f), 1f) < 0.35f;
            for (int i = 0; i < wantedPips.Length; i++)
            {
                if (wantedPips[i] == null) continue;
                bool on = i < level && !blinkOff;
                wantedPips[i].sharedMaterial = on ? pipOn : pipOff;
            }
        }

        void OnDestroy()
        {
            if (hooked != null) hooked.SaveStatusChanged -= OnSaveStatus;
        }
    }
}
