using SmallTown.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace SmallTown.Crime
{
    /// <summary>Something for sale that unlocks permanently. Wire a PunchButton to Buy.</summary>
    public sealed class ShopOffer : MonoBehaviour
    {
        [Tooltip("Stable unlock ID written to the save file.")]
        public string unlockId = GameBalance.PistolUnlock;
        public string displayName = "Pistol";
        [SerializeField] TMP_Text label;
        public UnityEvent purchased = new UnityEvent();

        public void Configure(TMP_Text priceLabel) => label = priceLabel;

        public int Price
        {
            get
            {
                GameBalance balance = GameSession.Instance != null ? GameSession.Instance.Balance : null;
                if (balance == null) return 0;
                return unlockId == GameBalance.SatchelUnlock ? balance.satchelPrice : balance.pistolPrice;
            }
        }

        void Start() => Refresh();

        void OnEnable()
        {
            if (GameSession.Instance != null) GameSession.Instance.Loaded += Refresh;
        }

        void OnDisable()
        {
            if (GameSession.Instance != null) GameSession.Instance.Loaded -= Refresh;
        }

        /// <summary>Attempts the purchase and tells the player what happened.</summary>
        public TransactionResult Buy()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return TransactionResult.Invalid;
            int price = Price;
            TransactionResult result = session.Wallet.Purchase(unlockId, price);
            switch (result)
            {
                case TransactionResult.Applied:
                    GameFeedback.Play(FeedbackSound.Cash, transform.position);
                    HudToast.Show("Bought " + displayName + "  -$" + price);
                    session.ApplyUnlocks();
                    purchased.Invoke();
                    break;
                case TransactionResult.Duplicate:
                    HudToast.Show("You already own the " + displayName + ".");
                    break;
                case TransactionResult.InsufficientFunds:
                    GameFeedback.Play(FeedbackSound.Reject, transform.position);
                    HudToast.Show("The " + displayName + " costs $" + price + ". You need $" + (price - session.Wallet.Cash) + " more.");
                    break;
            }
            Refresh();
            return result;
        }

        /// <summary>Button-friendly wrapper (UnityEvents need a void method).</summary>
        public void BuyPressed() => Buy();

        public void Refresh()
        {
            if (label == null) return;
            GameSession session = GameSession.Instance;
            bool owned = session != null && session.Wallet != null && session.Wallet.Owns(unlockId);
            label.text = "<b>" + displayName.ToUpperInvariant() + "</b>\n" + (owned ? "OWNED" : "$" + Price);
        }
    }
}
