using System.Collections;
using System.Collections.Generic;
using SmallTown.Interaction;
using SmallTown.Items;
using SmallTown.Locomotion;
using SmallTown.UI;
using UnityEngine;

namespace SmallTown.Crime
{
    public struct ArrestResult
    {
        public int wantedLevel;
        public int fine;
        public int confiscatedItems;
    }

    /// <summary>
    /// What happens when the police catch you: a short fade, a fine, carried
    /// stolen goods confiscated, and release outside the station. Purchases,
    /// banked savings and everything else long-term are untouched.
    /// </summary>
    public sealed class ArrestController : MonoBehaviour
    {
        [SerializeField] Transform releasePoint;
        readonly List<GrabbableItem> scratch = new List<GrabbableItem>();

        public bool InProgress { get; private set; }
        public ArrestResult LastResult { get; private set; }

        public void Configure(Transform release) => releasePoint = release;

        public void Arrest()
        {
            if (InProgress || GameSession.Instance == null) return;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            InProgress = true;
            PlayerRig rig = GameSession.Instance.Rig;
            rig.SetMode(MovementMode.Frozen);
            HudToast.Show("BUSTED!");
            GameFeedback.Play(FeedbackSound.Reject, rig.Head.position);
            if (ScreenFader.Instance != null) yield return ScreenFader.Instance.Fade(1f, 0.5f);

            ArrestResult result = ApplyConsequences();
            yield return new WaitForSeconds(0.8f);

            rig.SetMode(MovementMode.HandLocomotion);
            string taken = result.confiscatedItems > 0 ? "  " + result.confiscatedItems + " stolen item(s) confiscated." : "";
            HudToast.Show("Released. Fine: $" + result.fine + "." + taken);
            if (ScreenFader.Instance != null) yield return ScreenFader.Instance.Fade(0f, 0.6f);
            InProgress = false;
        }

        /// <summary>
        /// The actual penalty, with no presentation. Fine from the wallet (never
        /// the bank), stolen goods the player is carrying are removed, wanted
        /// level cleared, player moved to the station.
        /// </summary>
        public ArrestResult ApplyConsequences()
        {
            GameSession session = GameSession.Instance;
            var result = new ArrestResult { wantedLevel = session.Wanted.Level };

            result.fine = session.Wallet.Fine("fine:" + System.Guid.NewGuid().ToString("N"), session.Balance.FineFor(result.wantedLevel));
            result.confiscatedItems = ConfiscateCarriedStolenGoods(session);
            session.Wallet.Profile.timesArrested++;
            session.Wanted.Clear();
            session.MarkDirty();

            if (releasePoint != null) session.Rig.TeleportFeet(releasePoint.position, releasePoint.eulerAngles.y);
            LastResult = result;
            return result;
        }

        int ConfiscateCarriedStolenGoods(GameSession session)
        {
            int count = 0;
            scratch.Clear();
            if (session.Bag != null) scratch.AddRange(session.Bag.Items);
            foreach (HandGrabber grabber in session.Rig.GetComponentsInChildren<HandGrabber>())
            {
                if (grabber.Held != null) scratch.Add(grabber.Held);
            }
            foreach (GrabbableItem item in scratch)
            {
                var loot = item.GetComponent<LootItem>();
                if (loot == null || !loot.stolen) continue;
                item.ForceReleaseAll();
                if (session.Bag != null) session.Bag.Remove(item);
                Destroy(item.gameObject);
                count++;
            }
            return count;
        }
    }
}
