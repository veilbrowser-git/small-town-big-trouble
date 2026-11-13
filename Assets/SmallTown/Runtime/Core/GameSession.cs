using System;
using System.IO;
using SmallTown.Crime;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown
{
    /// <summary>
    /// Owns the services a play session needs: the player's profile and wallet,
    /// saving, and wanted status. One per gameplay scene. Scenes without one
    /// (the movement gym) simply have no economy.
    ///
    /// Trust model for the co-op prototype: the host owns one GameSession and is
    /// the only machine that changes wallets, wanted levels or loot state.
    /// Clients ask; the host validates and replies. Profiles are identified by
    /// PlayerProfile.profileId, a random ID created with the profile and stored
    /// in that player's own save file. A cheating host can cheat; that is
    /// accepted for friends-only co-op.
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

        float saveAt = -1f;

        public static GameSession Instance { get; private set; }

        public GameBalance Balance => balance;
        public PlayerRig Rig => rig;
        public StorageBag Bag => bag;
        public Wallet Wallet { get; private set; }
        public WantedTracker Wanted { get; private set; }
        public SaveSystem Saves { get; private set; }
        public LoadStatus LoadStatus { get; private set; }
        public string LocalPlayerId => Wallet.Profile.profileId;

        /// <summary>"Saved", "Save failed" and similar, for the wrist device.</summary>
        public event Action<string> SaveStatusChanged;
        public event Action Loaded;

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
            Wallet = new Wallet(profile);
            Wallet.Changed += (_, __) => MarkDirty();
            Wanted = new WantedTracker(balance.escapeSeconds, balance.escalateSeconds);
        }

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

        /// <summary>Applies owned upgrades to the live player (bag size and so on).</summary>
        public void ApplyUnlocks()
        {
            if (bag != null)
            {
                bag.capacity = Wallet.Owns(GameBalance.SatchelUnlock) ? balance.bigSatchelCapacity : balance.baseSatchelCapacity;
                bag.Refresh();
            }
        }

        public void MarkDirty() => saveAt = Time.unscaledTime + autosaveDelay;

        void Update()
        {
            if (saveAt >= 0f && Time.unscaledTime >= saveAt) SaveNow();
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
