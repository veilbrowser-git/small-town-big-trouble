using System;
using SmallTown.Crime;
using SmallTown.Economy;
using UnityEngine;

namespace SmallTown
{
    /// <summary>
    /// Everything the host tracks about one player in the session: their money,
    /// their own wanted level, and where they are. Each player has their own;
    /// nothing here is shared, so one player's trouble never becomes another's.
    /// </summary>
    public sealed class PlayerState
    {
        public readonly ulong ClientId;
        public readonly bool IsLocal;
        public readonly WantedTracker Wanted;
        /// <summary>Order of joining, 0 for the host. Used for spawn offsets and avatar colour.</summary>
        public int Index;
        public Vector3 Head;
        public Vector3 Feet;
        /// <summary>True while the player is being arrested or otherwise out of play.</summary>
        public bool Busy;
        public float BusyUntil;
        /// <summary>Shows a message to this player, wherever they are.</summary>
        public Action<string> Notify;

        public Wallet Wallet { get; private set; }
        public string ProfileId => Wallet.Profile.profileId;

        /// <summary>Raised when the wallet object is swapped (a remote player's profile arrived).</summary>
        public event Action<PlayerState> WalletReplaced;

        public PlayerState(ulong clientId, bool isLocal, Wallet wallet, WantedTracker wanted)
        {
            ClientId = clientId;
            IsLocal = isLocal;
            Wallet = wallet;
            Wanted = wanted;
        }

        public void ReplaceWallet(Wallet wallet)
        {
            Wallet = wallet;
            WalletReplaced?.Invoke(this);
        }

        public void Tell(string message) => Notify?.Invoke(message);
    }

    /// <summary>What the wrist device shows. Filled from live state on the host and from replicated values on clients.</summary>
    public struct HudView
    {
        public int cash;
        public int wantedLevel;
        public bool seenByPolice;
        public float escapeProgress;
        public string objective;
    }
}
