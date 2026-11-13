using System;
using System.Collections.Generic;

namespace SmallTown.Economy
{
    /// <summary>
    /// Everything about a player that survives between sessions. Session state
    /// such as wanted level, police searches and carried loot is deliberately
    /// not here.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public const int CurrentVersion = 2;
        public const int MaxRememberedTransactions = 512;

        public int version = CurrentVersion;
        /// <summary>Stable ID for this profile. In co-op the host keys each player's data by this.</summary>
        public string profileId = "";
        public int wallet;
        public int bank;
        /// <summary>Stable IDs of purchased equipment, vehicles, homes and cosmetics.</summary>
        public List<string> unlocks = new List<string>();
        /// <summary>Recently applied transaction IDs, oldest first. Makes payouts idempotent across saves.</summary>
        public List<string> processedTransactions = new List<string>();
        public int jobsCompleted;
        public int robberiesCompleted;
        public int timesArrested;
        public int lifetimeEarnings;

        public static PlayerProfile CreateNew(int startingMoney)
        {
            return new PlayerProfile
            {
                profileId = Guid.NewGuid().ToString("N"),
                wallet = Math.Max(0, startingMoney),
            };
        }

        /// <summary>Repairs anything a hand-edited or partly written file could leave invalid.</summary>
        public void Sanitize()
        {
            if (string.IsNullOrEmpty(profileId)) profileId = Guid.NewGuid().ToString("N");
            if (unlocks == null) unlocks = new List<string>();
            if (processedTransactions == null) processedTransactions = new List<string>();
            if (wallet < 0) wallet = 0;
            if (bank < 0) bank = 0;
            while (processedTransactions.Count > MaxRememberedTransactions) processedTransactions.RemoveAt(0);
        }
    }
}
