using System;
using System.Collections.Generic;
using System.IO;
using SmallTown.Crime;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Jobs;
using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown
{
    /// <summary>
    /// Owns the services a play session needs: every player's wallet and wanted
    /// status, and saving. One per gameplay scene. Scenes without one (the
    /// movement gym) simply have no economy.
    ///
    /// Trust model for the co-op prototype: the host's GameSession is the only
    /// place wallets, wanted levels and loot state change. Clients ask; the host
    /// validates and replies, and each client mirrors its own numbers for
    /// display and for its own save file. A joining player sends the host their
    /// saved profile, and the host believes it: a player who edits their save
    /// can lie about their money, and a host can cheat. Both are accepted for
    /// friends-only co-op. Profiles are identified by PlayerProfile.profileId, a
    /// random ID created with the profile and stored in that player's own save.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class GameSession : MonoBehaviour
    {
        /// <summary>Tests point this at a temporary folder.</summary>
        public static string SaveDirectoryOverride;
        /// <summary>Tests can switch file saving off entirely.</summary>
        public static bool PersistenceEnabled = true;

        [SerializeField] GameBalance balance;
        [SerializeField] PlayerRig rig;
        [SerializeField] StorageBag bag;
        [Min(0.1f)] public float autosaveDelay = 1.5f;

        readonly List<PlayerState> players = new List<PlayerState>();
        float saveAt = -1f;
        PlayerState actor;
        HudView? remoteView;
        Wallet hookedWallet;

        public static GameSession Instance { get; private set; }

        public GameBalance Balance => balance;
        public PlayerRig Rig => rig;
        public StorageBag Bag => bag;
        public SaveSystem Saves { get; private set; }
        public LoadStatus LoadStatus { get; private set; }

        /// <summary>The player at this machine.</summary>
        public PlayerState Local { get; private set; }
        /// <summary>On the host: everyone in the session. On a client: only the local player.</summary>
        public IReadOnlyList<PlayerState> Players => players;
        /// <summary>
        /// Whoever caused the thing being processed right now, such as the player
        /// who slapped a button. Falls back to the local player.
        /// </summary>
        public PlayerState Actor => actor ?? Local;

        public Wallet Wallet => Local.Wallet;
        public WantedTracker Wanted => Local.Wanted;
        public string LocalPlayerId => Local.ProfileId;

        /// <summary>"Saved", "Save failed" and similar, for the wrist device.</summary>
        public event Action<string> SaveStatusChanged;
        public event Action Loaded;
        public event Action<PlayerState> PlayerAdded;
        public event Action<PlayerState> PlayerRemoved;

        public void Configure(GameBalance gameBalance, PlayerRig playerRig, StorageBag storage)
        {
            balance = gameBalance;
            rig = playerRig;
            bag = storage;
        }

        void Awake()
        {
            Instance = this;
            string directory = SaveDirectoryOverride ?? Path.Combine(Application.persistentDataPath, "Saves");
            Saves = new SaveSystem(directory);
            PlayerProfile profile;
            if (PersistenceEnabled)
            {
                profile = Saves.Load(balance.startingMoney, out LoadStatus status);
                LoadStatus = status;
            }
            else
            {
                profile = PlayerProfile.CreateNew(balance.startingMoney);
                LoadStatus = LoadStatus.NewProfile;
            }

            Local = new PlayerState(Net.LocalClientId, true, new Wallet(profile), NewTracker()) { Notify = HudToast.Show };
            HookLocalWallet();
            players.Add(Local);
            UpdateLocalPosition();
        }

        public WantedTracker NewTracker() => new WantedTracker(balance.escapeSeconds, balance.escalateSeconds);

        void HookLocalWallet()
        {
            if (hookedWallet != null) hookedWallet.Changed -= OnLocalWalletChanged;
            hookedWallet = Local.Wallet;
            hookedWallet.Changed += OnLocalWalletChanged;
        }

        void OnLocalWalletChanged(string id, int delta) => MarkDirty();

        void Start()
        {
            ApplyUnlocks();
            switch (LoadStatus)
            {
                case LoadStatus.RecoveredFromBackup:
                    HudToast.Show("Your save was damaged. Restored the backup.");
                    break;
                case LoadStatus.ResetAfterCorruption:
                    HudToast.Show("Your save could not be read. Starting fresh; the old file was kept.");
                    break;
                case LoadStatus.ResetNewerVersion:
                    HudToast.Show("That save is from a newer version. Starting fresh; the old file was kept.");
                    break;
            }
            Loaded?.Invoke();
        }

        // ------------------------------------------------------------------ Players

        /// <summary>Host only: a remote player joined.</summary>
        public PlayerState AddPlayer(ulong clientId, Action<string> notify)
        {
            PlayerState existing = FindPlayer(clientId);
            if (existing != null) return existing;
            // Until their saved profile arrives they get a fresh one.
            var state = new PlayerState(clientId, false, new Wallet(PlayerProfile.CreateNew(balance.startingMoney)), NewTracker())
            {
                Notify = notify,
                Index = players.Count,
            };
            players.Add(state);
            PlayerAdded?.Invoke(state);
            return state;
        }

        /// <summary>Host only: a player left.</summary>
        public void RemovePlayer(ulong clientId)
        {
            PlayerState state = FindPlayer(clientId);
            if (state == null || state.IsLocal) return;
            players.Remove(state);
            state.Wanted.Clear();
            PlayerRemoved?.Invoke(state);
        }

        /// <summary>The local player's network identity is only known once the session starts.</summary>
        public void RebindLocal(ulong clientId)
        {
            if (Local.ClientId == clientId) return;
            var rebound = new PlayerState(clientId, true, Local.Wallet, Local.Wanted) { Notify = HudToast.Show, Index = Local.Index };
            int index = players.IndexOf(Local);
            Local = rebound;
            if (index >= 0) players[index] = rebound;
            else players.Add(rebound);
        }

        public PlayerState FindPlayer(ulong clientId)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].ClientId == clientId) return players[i];
            }
            return null;
        }

        /// <summary>The player who has, or last had, an item. Falls back to the local player offline.</summary>
        public PlayerState OwnerOf(GrabbableItem item)
        {
            if (item == null) return null;
            ulong id = item.HolderId != GrabbableItem.NoHolder ? item.HolderId : item.LastHolderId;
            return id == GrabbableItem.NoHolder ? null : FindPlayer(id);
        }

        /// <summary>Runs something on behalf of a particular player (see <see cref="Actor"/>).</summary>
        public void RunAs(PlayerState player, Action action)
        {
            PlayerState previous = actor;
            actor = player;
            try
            {
                action();
            }
            finally
            {
                actor = previous;
            }
        }

        public void TellAll(string message)
        {
            for (int i = 0; i < players.Count; i++) players[i].Tell(message);
        }

        // ------------------------------------------------------------------ HUD

        /// <summary>What the local player's wrist device should show.</summary>
        public HudView View
        {
            get
            {
                if (remoteView.HasValue) return remoteView.Value;
                Job job = JobManager.Instance != null ? JobManager.Instance.Active : null;
                return new HudView
                {
                    cash = Local.Wallet.Cash,
                    wantedLevel = Local.Wanted.Level,
                    seenByPolice = Local.Wanted.SeenByPolice,
                    escapeProgress = Local.Wanted.EscapeProgress,
                    objective = job != null ? job.Objective : "",
                };
            }
        }

        /// <summary>Clients: the host's numbers for this player replace the local ones.</summary>
        public void SetRemoteView(HudView view) => remoteView = view;

        /// <summary>
        /// Clients: copy the host's authoritative money and purchases into the local
        /// profile so the wrist device, shop labels and this player's own save file
        /// all agree with the host.
        /// </summary>
        public void MirrorWallet(int cash, int bank, bool ownsPistol, bool ownsSatchel, int jobs, int robberies, int arrests)
        {
            PlayerProfile profile = Local.Wallet.Profile;
            profile.wallet = cash;
            profile.bank = bank;
            SetUnlock(profile, GameBalance.PistolUnlock, ownsPistol);
            SetUnlock(profile, GameBalance.SatchelUnlock, ownsSatchel);
            profile.jobsCompleted = jobs;
            profile.robberiesCompleted = robberies;
            profile.timesArrested = arrests;
            ApplyUnlocks();
            MarkDirty();
            Loaded?.Invoke();
        }

        static void SetUnlock(PlayerProfile profile, string id, bool owned)
        {
            bool has = profile.unlocks.Contains(id);
            if (owned && !has) profile.unlocks.Add(id);
        }

        /// <summary>Applies owned upgrades to the live player (bag size and so on).</summary>
        public void ApplyUnlocks()
        {
            if (bag != null)
            {
                bag.capacity = Wallet.Owns(GameBalance.SatchelUnlock) ? balance.bigSatchelCapacity : balance.baseSatchelCapacity;
                bag.Refresh();
            }
        }

        // ------------------------------------------------------------------ Saving and upkeep

        public void MarkDirty() => saveAt = Time.unscaledTime + autosaveDelay;

        void Update()
        {
            UpdateLocalPosition();
            if (saveAt >= 0f && Time.unscaledTime >= saveAt) SaveNow();
        }

        void UpdateLocalPosition()
        {
            if (rig == null || rig.Head == null) return;
            Local.Head = rig.Head.position;
            Local.Feet = rig.Motor != null && rig.Mode == MovementMode.HandLocomotion
                ? rig.Motor.BodyBottom
                : Local.Head + Vector3.down * 0.8f;
        }

        public bool SaveNow()
        {
            saveAt = -1f;
            if (!PersistenceEnabled) return true;
            bool ok = Saves.Save(Wallet.Profile);
            SaveStatusChanged?.Invoke(ok ? "Saved" : "Save failed");
            return ok;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && saveAt >= 0f) SaveNow();
        }

        void OnApplicationQuit()
        {
            if (saveAt >= 0f) SaveNow();
        }

        void OnDestroy()
        {
            if (saveAt >= 0f) SaveNow();
            if (Instance == this) Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;
    }
}
