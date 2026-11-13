using System.Collections.Generic;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using UnityEngine;

namespace SmallTown.Crime
{
    /// <summary>
    /// Turns stolen cash into wallet money, but only once the player has got
    /// away with it: cash in the satchel pays out when the wanted level clears,
    /// or straight away if nobody is looking for you. Until then it is a
    /// physical thing that can be confiscated.
    /// </summary>
    public sealed class LootBanker : MonoBehaviour
    {
        readonly List<GrabbableItem> scratch = new List<GrabbableItem>();
        StorageBag bag;

        void Start()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;
            session.Wanted.Escaped += OnEscaped;
            bag = session.Bag;
            if (bag != null) bag.Stored += OnStored;
        }

        void OnDestroy()
        {
            if (bag != null) bag.Stored -= OnStored;
        }

        void OnEscaped(int fromLevel)
        {
            int banked = BankCarriedCash();
            HudToast.Show(banked > 0 ? "You got away!  +$" + banked + " cash" : "You got away.");
            GameFeedback.Play(FeedbackSound.Success, GameSession.Instance.Rig.Head.position);
        }

        void OnStored(GrabbableItem item)
        {
            GameSession session = GameSession.Instance;
            if (session == null || session.Wanted.IsWanted) return;
            var loot = item.GetComponent<LootItem>();
            if (loot == null || !loot.isCash) return;
            int value = Bank(loot);
            if (value > 0) HudToast.Show("Cash pocketed  +$" + value);
        }

        /// <summary>Pays out every cash item in the satchel. Returns the total.</summary>
        public int BankCarriedCash()
        {
            if (bag == null) return 0;
            scratch.Clear();
            scratch.AddRange(bag.Items);
            int total = 0;
            foreach (GrabbableItem item in scratch)
            {
                var loot = item.GetComponent<LootItem>();
                if (loot != null && loot.isCash) total += Bank(loot);
            }
            return total;
        }

        int Bank(LootItem loot)
        {
            GameSession session = GameSession.Instance;
            if (!loot.TryMarkPaidOut()) return 0;
            if (session.Wallet.Apply("cash:" + loot.InstanceId, loot.value) != TransactionResult.Applied) return 0;
            GameFeedback.Play(FeedbackSound.Cash, loot.transform.position);
            if (bag != null) bag.Remove(loot.Item);
            Destroy(loot.gameObject);
            return loot.value;
        }
    }
}
