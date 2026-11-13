using System.Collections.Generic;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using TMPro;
using UnityEngine;

namespace SmallTown.Crime
{
    /// <summary>
    /// Buys loot, no questions asked. Put an item on the green tray and let go.
    /// Each physical item can be sold exactly once: the sale is keyed on the
    /// item's unique ID and the item is destroyed on payment.
    /// </summary>
    public sealed class PawnShop : MonoBehaviour
    {
        [SerializeField] BoxZone tray;
        [SerializeField] TMP_Text payoutLabel;
        readonly List<LootItem> scratch = new List<LootItem>();
        int total;

        public void Configure(BoxZone trayZone, TMP_Text label)
        {
            tray = trayZone;
            payoutLabel = label;
        }

        void Start()
        {
            if (payoutLabel != null) payoutLabel.text = "<b>WE BUY ANYTHING</b>\nDrop it on the green tray.";
        }

        void Update()
        {
            if (GameSession.Instance == null || tray == null) return;
            scratch.Clear();
            scratch.AddRange(LootItem.All);
            foreach (LootItem loot in scratch)
            {
                if (loot == null || loot.isCash || loot.PaidOut) continue;
                if (loot.Item.State != ItemState.Free || !tray.Contains(loot.transform.position)) continue;
                if (loot.Item.Body.linearVelocity.sqrMagnitude > 0.6f * 0.6f) continue;
                Sell(loot);
            }
        }

        /// <summary>Sells one item. Returns the money paid, or 0 if it was refused (cash, or already sold).</summary>
        public int Sell(LootItem loot)
        {
            GameSession session = GameSession.Instance;
            if (loot == null || loot.isCash || session == null) return 0;
            int price = session.Balance.PawnPrice(loot.value);
            if (!loot.TryMarkPaidOut()) return 0;
            if (session.Wallet.Apply("sale:" + loot.InstanceId, price) != TransactionResult.Applied) return 0;

            total += price;
            GameFeedback.Play(FeedbackSound.Cash, tray.transform.position);
            HudToast.Show("Sold " + loot.displayName + "  +$" + price);
            if (payoutLabel != null) payoutLabel.text = "<b>SOLD</b>\n" + loot.displayName + "  $" + price + "\nToday: $" + total;

            loot.Item.ForceReleaseAll();
            if (session.Bag != null) session.Bag.Remove(loot.Item);
            Destroy(loot.gameObject);
            return price;
        }
    }
}
