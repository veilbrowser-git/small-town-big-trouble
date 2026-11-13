using System.Collections.Generic;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using UnityEngine;

namespace SmallTown.Crime
{
    /// <summary>
    /// Turns stolen cash into wallet money, but only once the player carrying it
    /// has got away: cash in a satchel pays out when that player's wanted level
    /// clears, or straight away if nobody is looking for them. Until then it is
    /// a physical thing that can be confiscated. Runs on the host.
    /// </summary>
    public sealed class LootBanker : MonoBehaviour
    {
        readonly List<LootItem> scratch = new List<LootItem>();
        readonly HashSet<PlayerState> hooked = new HashSet<PlayerState>();
        StorageBag bag;
        float scanAt;

        void Start()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;
            if (!Net.IsHost)
            {
                enabled = false;
                return;
            }
            bag = session.Bag;
            if (bag != null) bag.Stored += OnStoredLocally;
        }

        void OnDestroy()
        {
            if (bag != null) bag.Stored -= OnStoredLocally;
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;
            foreach (PlayerState player in session.Players)
            {
                if (!hooked.Add(player)) continue;
                PlayerState captured = player;
                player.Wanted.Escaped += level => OnEscaped(captured);
            }

            // Other players' satchels: the host only learns an item was bagged from its
            // replicated state, so sweep for bagged cash a few times a second.
            if (Time.time < scanAt) return;
            scanAt = Time.time + 0.25f;
            // This also catches cash that was too hot to bank when it was first bagged.
            foreach (PlayerState player in session.Players)
            {
                if (!player.Wanted.IsWanted) BankCarriedCash(player);
            }
        }

        void OnEscaped(PlayerState player)
        {
            int banked = BankCarriedCash(player);
            player.Tell(banked > 0 ? "You got away!  +$" + banked + " cash" : "You got away.");
            if (player.IsLocal) GameFeedback.Play(FeedbackSound.Success, player.Head);
        }

        void OnStoredLocally(GrabbableItem item)
        {
            GameSession session = GameSession.Instance;
            if (session == null || !Net.IsHost || session.Local.Wanted.IsWanted) return;
            var loot = item.GetComponent<LootItem>();
            if (loot == null || !loot.isCash) return;
            int value = Bank(loot, session.Local);
            if (value > 0) session.Local.Tell("Cash pocketed  +$" + value);
        }

        /// <summary>Pays out every cash item in a player's satchel (the local player's if none is given). Returns the total.</summary>
        public int BankCarriedCash(PlayerState player = null)
        {
            GameSession session = GameSession.Instance;
            if (session == null) return 0;
            if (player == null) player = session.Local;
            scratch.Clear();
            foreach (LootItem loot in LootItem.All)
            {
                if (loot == null || !loot.isCash || loot.PaidOut) continue;
                if (loot.Item.State == ItemState.Stored && loot.Item.HolderId == player.ClientId) scratch.Add(loot);
            }
            int total = 0;
            foreach (LootItem loot in scratch) total += Bank(loot, player);
            return total;
        }

        int Bank(LootItem loot, PlayerState player)
        {
            if (Time.time < loot.hotUntil) return 0;
            if (!loot.TryMarkPaidOut()) return 0;
            if (player.Wallet.Apply("cash:" + loot.InstanceId, loot.value) != TransactionResult.Applied) return 0;
            if (player.IsLocal)
            {
                GameFeedback.Play(FeedbackSound.Cash, loot.transform.position);
                if (bag != null) bag.Remove(loot.Item);
            }
            int value = loot.value;
            Net.Despawn(loot.gameObject);
            return value;
        }
    }
}
