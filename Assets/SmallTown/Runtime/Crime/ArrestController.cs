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
    /// What happens when the police catch a player: a fine, carried stolen goods
    /// confiscated, and release outside the station after a short fade.
    /// Purchases, banked savings and everything else long-term are untouched.
    /// The penalty is decided on the host; the fade and the move happen on the
    /// arrested player's own machine. Other players are not affected.
    /// </summary>
    public sealed class ArrestController : MonoBehaviour
    {
        [SerializeField] Transform releasePoint;
        readonly List<LootItem> scratch = new List<LootItem>();

        /// <summary>
        /// Set by the network layer: plays the arrest on a remote player's machine.
        /// Arguments: the player, where they are released, facing, and the result.
        /// </summary>
        public System.Action<PlayerState, Vector3, float, ArrestResult> RemotePresentation;

        /// <summary>True while the local player is going through the arrest transition.</summary>
        public bool InProgress { get; private set; }
        public ArrestResult LastResult { get; private set; }
        public Transform ReleasePoint => releasePoint;

        public void Configure(Transform release) => releasePoint = release;

        /// <summary>Arrests a player (the local one if none is given).</summary>
        public void Arrest(PlayerState player = null)
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;
            if (player == null) player = session.Local;
            if (player.Busy) return;

            player.Busy = true;
            player.BusyUntil = Time.time + 3f;
            ArrestResult result = ApplyPenalty(player);
            Vector3 position = releasePoint != null ? releasePoint.position + Vector3.right * (player.Index * 0.7f) : Vector3.zero;
            float yaw = releasePoint != null ? releasePoint.eulerAngles.y : 0f;

            if (player.IsLocal) Present(position, yaw, result);
            else RemotePresentation?.Invoke(player, position, yaw, result);
        }

        /// <summary>Plays the arrest for the player at this machine: freeze, fade out, move, fade in.</summary>
        public void Present(Vector3 releasePosition, float yaw, ArrestResult result)
        {
            if (InProgress || GameSession.Instance == null) return;
            StartCoroutine(Run(releasePosition, yaw, result));
        }

        IEnumerator Run(Vector3 releasePosition, float yaw, ArrestResult result)
        {
            InProgress = true;
            PlayerRig rig = GameSession.Instance.Rig;
            rig.SetMode(MovementMode.Frozen);
            HudToast.Show("BUSTED!");
            GameFeedback.Play(FeedbackSound.Reject, rig.Head.position);
            if (ScreenFader.Instance != null) yield return ScreenFader.Instance.Fade(1f, 0.5f);

            rig.TeleportFeet(releasePosition, yaw);
            yield return new WaitForSeconds(0.8f);

            rig.SetMode(MovementMode.HandLocomotion);
            string taken = result.confiscatedItems > 0 ? "  " + result.confiscatedItems + " stolen item(s) confiscated." : "";
            HudToast.Show("Released. Fine: $" + result.fine + "." + taken);
            if (ScreenFader.Instance != null) yield return ScreenFader.Instance.Fade(0f, 0.6f);
            InProgress = false;
        }

        /// <summary>
        /// The whole penalty for the local player with no presentation: fine,
        /// confiscation, wanted level cleared, and moved to the station.
        /// </summary>
        public ArrestResult ApplyConsequences()
        {
            GameSession session = GameSession.Instance;
            ArrestResult result = ApplyPenalty(session.Local);
            if (releasePoint != null) session.Rig.TeleportFeet(releasePoint.position, releasePoint.eulerAngles.y);
            return result;
        }

        /// <summary>Host side: fine from the wallet (never the bank), carried stolen goods removed, wanted level cleared.</summary>
        public ArrestResult ApplyPenalty(PlayerState player)
        {
            GameSession session = GameSession.Instance;
            var result = new ArrestResult { wantedLevel = player.Wanted.Level };
            result.fine = player.Wallet.Fine("fine:" + System.Guid.NewGuid().ToString("N"), session.Balance.FineFor(result.wantedLevel));
            result.confiscatedItems = ConfiscateCarriedStolenGoods(session, player);
            player.Wallet.Profile.timesArrested++;
            player.Wanted.Clear();
            session.MarkDirty();
            LastResult = result;
            return result;
        }

        int ConfiscateCarriedStolenGoods(GameSession session, PlayerState player)
        {
            // "Carried" means in a hand or in the satchel of this player, on any machine.
            scratch.Clear();
            foreach (LootItem loot in LootItem.All)
            {
                if (loot == null || !loot.stolen) continue;
                GrabbableItem item = loot.Item;
                if (item.State == ItemState.Free || item.HolderId != player.ClientId) continue;
                scratch.Add(loot);
            }
            foreach (LootItem loot in scratch)
            {
                loot.Item.ForceReleaseAll();
                if (player.IsLocal && session.Bag != null) session.Bag.Remove(loot.Item);
                Net.Despawn(loot.gameObject);
            }
            return scratch.Count;
        }
    }
}
