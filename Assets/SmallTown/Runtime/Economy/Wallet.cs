using System;
using System.Collections.Generic;

namespace SmallTown.Economy
{
    public enum TransactionResult
    {
        Applied,
        /// <summary>This exact transaction was already applied. Nothing changed.</summary>
        Duplicate,
        InsufficientFunds,
        Invalid,
    }

    /// <summary>
    /// All money movement for one profile. Every change carries a transaction ID
    /// and an ID is only ever applied once, so a job, a sale or a purchase can
    /// never pay out twice however many times it is reported. In co-op this
    /// runs on the host, which is what makes rewards host-validated.
    /// </summary>
    public sealed class Wallet
    {
        readonly HashSet<string> processed = new HashSet<string>();

        public PlayerProfile Profile { get; }

        /// <summary>Raised after any change: transaction ID, wallet delta.</summary>
        public event Action<string, int> Changed;

        public int Cash => Profile.wallet;
        public int Bank => Profile.bank;

        public Wallet(PlayerProfile profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Profile.Sanitize();
            foreach (string id in Profile.processedTransactions) processed.Add(id);
        }

        public bool WasProcessed(string transactionId) => processed.Contains(transactionId);

        /// <summary>Adds (or, if negative, removes) wallet cash exactly once per transaction ID.</summary>
        public TransactionResult Apply(string transactionId, int walletDelta)
        {
            if (string.IsNullOrEmpty(transactionId)) return TransactionResult.Invalid;
            if (processed.Contains(transactionId)) return TransactionResult.Duplicate;
            if (walletDelta < 0 && Profile.wallet + walletDelta < 0) return TransactionResult.InsufficientFunds;

            Profile.wallet += walletDelta;
            if (walletDelta > 0) Profile.lifetimeEarnings += walletDelta;
            Remember(transactionId);
            Changed?.Invoke(transactionId, walletDelta);
            return TransactionResult.Applied;
        }

        /// <summary>Buys an unlock. Owning it already counts as a duplicate, so nobody pays twice.</summary>
        public TransactionResult Purchase(string unlockId, int price)
        {
            if (string.IsNullOrEmpty(unlockId) || price < 0) return TransactionResult.Invalid;
            if (Owns(unlockId)) return TransactionResult.Duplicate;
            if (Profile.wallet < price) return TransactionResult.InsufficientFunds;

            Profile.wallet -= price;
            Profile.unlocks.Add(unlockId);
            Remember("buy:" + unlockId);
            Changed?.Invoke("buy:" + unlockId, -price);
            return TransactionResult.Applied;
        }

        public bool Owns(string unlockId) => Profile.unlocks.Contains(unlockId);

        public TransactionResult Deposit(int amount)
        {
            if (amount <= 0) return TransactionResult.Invalid;
            if (Profile.wallet < amount) return TransactionResult.InsufficientFunds;
            Profile.wallet -= amount;
            Profile.bank += amount;
            Changed?.Invoke("deposit", -amount);
            return TransactionResult.Applied;
        }

        public TransactionResult Withdraw(int amount)
        {
            if (amount <= 0) return TransactionResult.Invalid;
            if (Profile.bank < amount) return TransactionResult.InsufficientFunds;
            Profile.bank -= amount;
            Profile.wallet += amount;
            Changed?.Invoke("withdraw", amount);
            return TransactionResult.Applied;
        }

        /// <summary>
        /// Takes up to <paramref name="amount"/> from the wallet (never the bank:
        /// banked savings are safe from fines). Returns what was actually taken.
        /// </summary>
        public int Fine(string transactionId, int amount)
        {
            if (string.IsNullOrEmpty(transactionId) || amount <= 0 || processed.Contains(transactionId)) return 0;
            int taken = Math.Min(amount, Profile.wallet);
            Profile.wallet -= taken;
            Remember(transactionId);
            Changed?.Invoke(transactionId, -taken);
            return taken;
        }

        void Remember(string transactionId)
        {
            processed.Add(transactionId);
            Profile.processedTransactions.Add(transactionId);
            while (Profile.processedTransactions.Count > PlayerProfile.MaxRememberedTransactions)
            {
                processed.Remove(Profile.processedTransactions[0]);
                Profile.processedTransactions.RemoveAt(0);
            }
        }
    }
}
