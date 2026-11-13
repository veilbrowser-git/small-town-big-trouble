using System.IO;
using NUnit.Framework;
using SmallTown.Crime;
using SmallTown.Economy;
using UnityEngine;

namespace SmallTown.Tests
{
    public class WalletTests
    {
        static Wallet NewWallet(int money = 100) => new Wallet(PlayerProfile.CreateNew(money));

        [Test]
        public void Payout_AppliesOnce_HoweverManyTimesItIsReported()
        {
            Wallet wallet = NewWallet(0);
            Assert.That(wallet.Apply("job:pizza:run1", 60), Is.EqualTo(TransactionResult.Applied));
            Assert.That(wallet.Apply("job:pizza:run1", 60), Is.EqualTo(TransactionResult.Duplicate));
            Assert.That(wallet.Apply("job:pizza:run1", 9999), Is.EqualTo(TransactionResult.Duplicate));
            Assert.That(wallet.Cash, Is.EqualTo(60));
            Assert.That(wallet.Apply("job:pizza:run2", 60), Is.EqualTo(TransactionResult.Applied), "A new run pays again.");
            Assert.That(wallet.Cash, Is.EqualTo(120));
        }

        [Test]
        public void DuplicateProtection_SurvivesSaveAndReload()
        {
            Wallet wallet = NewWallet(0);
            wallet.Apply("sale:abc", 20);
            string json = JsonUtility.ToJson(wallet.Profile);
            var reloaded = new Wallet(JsonUtility.FromJson<PlayerProfile>(json));
            Assert.That(reloaded.Apply("sale:abc", 20), Is.EqualTo(TransactionResult.Duplicate));
            Assert.That(reloaded.Cash, Is.EqualTo(20));
        }

        [Test]
        public void Spending_CannotGoBelowZero()
        {
            Wallet wallet = NewWallet(30);
            Assert.That(wallet.Apply("spend:1", -50), Is.EqualTo(TransactionResult.InsufficientFunds));
            Assert.That(wallet.Cash, Is.EqualTo(30));
            Assert.That(wallet.Apply("spend:1", -30), Is.EqualTo(TransactionResult.Applied), "A refused transaction is not remembered as done.");
            Assert.That(wallet.Cash, Is.EqualTo(0));
            Assert.That(wallet.Apply("", 10), Is.EqualTo(TransactionResult.Invalid));
        }

        [Test]
        public void Purchase_ChargesOnce_AndUnlocks()
        {
            Wallet wallet = NewWallet(200);
            Assert.That(wallet.Purchase(GameBalance.PistolUnlock, 250), Is.EqualTo(TransactionResult.InsufficientFunds));
            Assert.That(wallet.Owns(GameBalance.PistolUnlock), Is.False);
            Assert.That(wallet.Purchase(GameBalance.PistolUnlock, 110), Is.EqualTo(TransactionResult.Applied));
            Assert.That(wallet.Purchase(GameBalance.PistolUnlock, 110), Is.EqualTo(TransactionResult.Duplicate));
            Assert.That(wallet.Cash, Is.EqualTo(90));
            Assert.That(wallet.Owns(GameBalance.PistolUnlock), Is.True);
        }

        [Test]
        public void Fine_IsCappedByWallet_AndNeverTouchesBank()
        {
            Wallet wallet = NewWallet(100);
            Assert.That(wallet.Deposit(70), Is.EqualTo(TransactionResult.Applied));
            Assert.That(wallet.Fine("fine:1", 90), Is.EqualTo(30), "Only what is in the wallet can be taken.");
            Assert.That(wallet.Fine("fine:1", 90), Is.EqualTo(0), "The same fine is not charged twice.");
            Assert.That(wallet.Cash, Is.EqualTo(0));
            Assert.That(wallet.Bank, Is.EqualTo(70));
            Assert.That(wallet.Withdraw(100), Is.EqualTo(TransactionResult.InsufficientFunds));
            Assert.That(wallet.Withdraw(70), Is.EqualTo(TransactionResult.Applied));
            Assert.That(wallet.Cash, Is.EqualTo(70));
        }

        [Test]
        public void RememberedTransactions_AreBounded()
        {
            Wallet wallet = NewWallet(0);
            for (int i = 0; i < PlayerProfile.MaxRememberedTransactions + 50; i++) wallet.Apply("t" + i, 1);
            Assert.That(wallet.Profile.processedTransactions.Count, Is.EqualTo(PlayerProfile.MaxRememberedTransactions));
            Assert.That(wallet.Apply("t" + (PlayerProfile.MaxRememberedTransactions + 49), 1), Is.EqualTo(TransactionResult.Duplicate));
        }

        [Test]
        public void Balance_PricesAndFines_ComeFromData()
        {
            var balance = ScriptableObject.CreateInstance<GameBalance>();
            balance.pawnRate = 0.5f;
            balance.fineBase = 10;
            balance.finePerLevel = 20;
            Assert.That(balance.PawnPrice(38), Is.EqualTo(19));
            Assert.That(balance.PawnPrice(1), Is.EqualTo(1), "Nothing sells for zero.");
            Assert.That(balance.FineFor(3), Is.EqualTo(70));
            Assert.That(balance.OfficersFor(2), Is.EqualTo(3));
            Assert.That(balance.OfficersFor(99), Is.EqualTo(5));
            Object.DestroyImmediate(balance);
        }
    }

    public class SaveSystemTests
    {
        string folder;
        SaveSystem saves;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "smalltown-save-tests-" + System.Guid.NewGuid().ToString("N"));
            saves = new SaveSystem(folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        [Test]
        public void NoFile_StartsNewProfile_WithStartingMoney()
        {
            PlayerProfile profile = saves.Load(20, out LoadStatus status);
            Assert.That(status, Is.EqualTo(LoadStatus.NewProfile));
            Assert.That(profile.wallet, Is.EqualTo(20));
            Assert.That(profile.profileId, Is.Not.Empty);
        }

        [Test]
        public void SaveThenLoad_RestoresPurchasesMoneyAndIdentity()
        {
            var wallet = new Wallet(PlayerProfile.CreateNew(300));
            wallet.Purchase(GameBalance.PistolUnlock, 110);
            wallet.Deposit(50);
            wallet.Apply("job:pizza:1", 60);
            wallet.Profile.jobsCompleted = 1;
            Assert.That(saves.Save(wallet.Profile), Is.True);

            PlayerProfile loaded = new SaveSystem(folder).Load(20, out LoadStatus status);
            Assert.That(status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(loaded.profileId, Is.EqualTo(wallet.Profile.profileId));
            Assert.That(loaded.wallet, Is.EqualTo(200));
            Assert.That(loaded.bank, Is.EqualTo(50));
            Assert.That(loaded.unlocks, Does.Contain(GameBalance.PistolUnlock));
            Assert.That(loaded.jobsCompleted, Is.EqualTo(1));
            Assert.That(new Wallet(loaded).Apply("job:pizza:1", 60), Is.EqualTo(TransactionResult.Duplicate));
        }

        [Test]
        public void CorruptMainFile_FallsBackToBackup()
        {
            PlayerProfile profile = PlayerProfile.CreateNew(100);
            saves.Save(profile);            // becomes the backup on the next save
            profile.wallet = 250;
            saves.Save(profile);
            File.WriteAllText(saves.MainPath, "{ \"version\": 2, \"wallet\": 2");   // truncated mid-write

            PlayerProfile loaded = saves.Load(20, out LoadStatus status);
            Assert.That(status, Is.EqualTo(LoadStatus.RecoveredFromBackup));
            Assert.That(loaded.wallet, Is.EqualTo(100));
            Assert.That(loaded.profileId, Is.EqualTo(profile.profileId));
            Assert.That(Directory.GetFiles(folder, "*corrupt*"), Is.Not.Empty, "The damaged file is kept for inspection.");
        }

        [Test]
        public void EverythingCorrupt_StartsFresh_WithoutThrowing()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(saves.MainPath, "not json at all");
            File.WriteAllText(saves.BackupPath, "");
            PlayerProfile loaded = saves.Load(20, out LoadStatus status);
            Assert.That(status, Is.EqualTo(LoadStatus.ResetAfterCorruption));
            Assert.That(loaded.wallet, Is.EqualTo(20));
            Assert.That(File.Exists(saves.MainPath), Is.False);
            Assert.That(saves.Save(loaded), Is.True, "Saving works again straight away.");
        }

        [Test]
        public void NewerVersion_IsKept_NotOverwritten()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(saves.MainPath, "{\"version\": 999, \"wallet\": 5000, \"profileId\": \"future\"}");
            PlayerProfile loaded = saves.Load(20, out LoadStatus status);
            Assert.That(status, Is.EqualTo(LoadStatus.ResetNewerVersion));
            Assert.That(loaded.wallet, Is.EqualTo(20));
            string[] kept = Directory.GetFiles(folder, "*newer*");
            Assert.That(kept, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllText(kept[0]), Does.Contain("5000"));
        }

        [Test]
        public void OlderVersion_IsMigrated()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(saves.MainPath, "{\"version\": 1, \"wallet\": 80, \"bank\": 40, \"profileId\": \"old\", \"unlocks\": [\"equipment.pistol\"]}");
            PlayerProfile loaded = saves.Load(20, out LoadStatus status);
            Assert.That(status, Is.EqualTo(LoadStatus.Migrated));
            Assert.That(loaded.version, Is.EqualTo(PlayerProfile.CurrentVersion));
            Assert.That(loaded.wallet, Is.EqualTo(80));
            Assert.That(loaded.lifetimeEarnings, Is.EqualTo(120));
            Assert.That(loaded.unlocks, Does.Contain("equipment.pistol"));
            Assert.That(loaded.processedTransactions, Is.Not.Null);
        }

        [Test]
        public void NegativeOrMissingValues_AreSanitised()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(saves.MainPath, "{\"version\": 2, \"wallet\": -500, \"bank\": -1}");
            PlayerProfile loaded = saves.Load(20, out _);
            Assert.That(loaded.wallet, Is.EqualTo(0));
            Assert.That(loaded.bank, Is.EqualTo(0));
            Assert.That(loaded.profileId, Is.Not.Empty);
        }
    }

    public class RobberyStateMachineTests
    {
        static RobberyStateMachine.Config Config => new RobberyStateMachine.Config
        {
            bundles = 3,
            firstBundleDelay = 2f,
            bundleInterval = 1f,
            alarmDelay = 3.5f,
            cooldownSeconds = 30f,
            maxHoldUpSeconds = 20f,
            absenceGraceSeconds = 2f,
        };

        static void Run(RobberyStateMachine machine, float seconds, bool present = true)
        {
            for (float t = 0f; t < seconds; t += 0.1f) machine.Tick(0.1f, present);
        }

        [Test]
        public void FullRobbery_DispensesExactlyTheRegister_ThenCoolsDown()
        {
            var machine = new RobberyStateMachine(Config);
            int dispensed = 0, alarms = 0, ended = 0;
            bool emptied = false;
            machine.BundleDispensed += _ => dispensed++;
            machine.AlarmTriggered += () => alarms++;
            machine.Ended += e => { ended++; emptied = e; };

            Assert.That(machine.State, Is.EqualTo(RobberyState.Idle));
            Assert.That(machine.TryStart(), Is.True);
            Assert.That(machine.TryStart(), Is.False, "Cannot start a robbery that is already running.");
            Run(machine, 1.5f);
            Assert.That(dispensed, Is.EqualTo(0), "The cashier needs a moment to open the register.");
            Run(machine, 10f);

            Assert.That(dispensed, Is.EqualTo(3));
            Assert.That(alarms, Is.EqualTo(1));
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(emptied, Is.True);
            Assert.That(machine.State, Is.EqualTo(RobberyState.Cooldown));
            Assert.That(machine.BundlesRemaining, Is.EqualTo(0));
        }

        [Test]
        public void Cooldown_BlocksFarming_UntilRestock()
        {
            var machine = new RobberyStateMachine(Config);
            int dispensed = 0, restocks = 0;
            machine.BundleDispensed += _ => dispensed++;
            machine.Restocked += () => restocks++;
            machine.TryStart();
            Run(machine, 10f);
            Assert.That(machine.TryStart(), Is.False, "An empty register cannot be robbed again.");
            Run(machine, 20f);
            Assert.That(machine.TryStart(), Is.False);
            Assert.That(dispensed, Is.EqualTo(3), "Nothing more comes out during the cooldown.");

            Run(machine, 15f);
            Assert.That(restocks, Is.EqualTo(1));
            Assert.That(machine.State, Is.EqualTo(RobberyState.Idle));
            Assert.That(machine.BundlesRemaining, Is.EqualTo(3));
            Assert.That(machine.TryStart(), Is.True, "After restocking it can be robbed again.");
        }

        [Test]
        public void LeavingEarly_EndsTheHoldUp_AndKeepsWhatWasNotTaken()
        {
            var machine = new RobberyStateMachine(Config);
            int dispensed = 0;
            bool? emptied = null;
            machine.BundleDispensed += _ => dispensed++;
            machine.Ended += e => emptied = e;
            machine.TryStart();
            Run(machine, 2.5f);             // one bundle out
            Run(machine, 3f, present: false);
            Assert.That(dispensed, Is.EqualTo(1), "The register does not pay an empty shop.");
            Assert.That(emptied, Is.False);
            Assert.That(machine.State, Is.EqualTo(RobberyState.Cooldown));
            Assert.That(machine.AlarmRaised, Is.True, "Even a quick robbery gets reported.");
        }

        [Test]
        public void Alarm_GoesOffAtItsDelay_EvenMidRobbery()
        {
            var machine = new RobberyStateMachine(Config);
            machine.TryStart();
            Run(machine, 3f);
            Assert.That(machine.AlarmRaised, Is.False);
            Run(machine, 0.7f);
            Assert.That(machine.AlarmRaised, Is.True);
            Assert.That(machine.State, Is.EqualTo(RobberyState.HoldUp));
        }

        [Test]
        public void HoldUp_TimesOut()
        {
            var config = Config;
            config.bundles = 100;
            config.bundleInterval = 5f;
            var machine = new RobberyStateMachine(config);
            machine.TryStart();
            Run(machine, 21f);
            Assert.That(machine.State, Is.EqualTo(RobberyState.Cooldown));
        }
    }

    public class WantedTrackerTests
    {
        static void Run(WantedTracker tracker, float seconds, bool seen)
        {
            for (float t = 0f; t < seconds; t += 0.1f) tracker.Tick(0.1f, seen, Vector3.one);
        }

        [Test]
        public void Reports_RaiseTheLevel_ButNeverLowerIt()
        {
            var tracker = new WantedTracker(10f, 30f);
            Assert.That(tracker.IsWanted, Is.False);
            tracker.ReportCrime(2, Vector3.zero);
            tracker.ReportCrime(1, Vector3.zero);
            Assert.That(tracker.Level, Is.EqualTo(2));
            tracker.ReportCrime(9, Vector3.zero);
            Assert.That(tracker.Level, Is.EqualTo(WantedTracker.MaxLevel));
        }

        [Test]
        public void Escape_NeedsTheFullTimeUnseen()
        {
            var tracker = new WantedTracker(10f, 30f);
            int escapes = 0;
            tracker.Escaped += _ => escapes++;
            tracker.ReportCrime(1, Vector3.zero);

            Run(tracker, 8f, seen: false);
            Assert.That(tracker.EscapeProgress, Is.GreaterThan(0.7f));
            Run(tracker, 0.5f, seen: true);      // spotted: the timer starts over
            Assert.That(tracker.EscapeProgress, Is.EqualTo(0f));
            Run(tracker, 8f, seen: false);
            Assert.That(tracker.IsWanted, Is.True);
            Run(tracker, 2.5f, seen: false);
            Assert.That(tracker.IsWanted, Is.False);
            Assert.That(escapes, Is.EqualTo(1));
        }

        [Test]
        public void BeingSeen_UpdatesLastKnownPosition_HidingDoesNot()
        {
            var tracker = new WantedTracker(10f, 30f);
            tracker.ReportCrime(1, new Vector3(5f, 0f, 5f));
            tracker.Tick(0.1f, false, new Vector3(50f, 0f, 50f));
            Assert.That(tracker.LastKnownPosition, Is.EqualTo(new Vector3(5f, 0f, 5f)), "Police do not know where an unseen suspect went.");
            tracker.Tick(0.1f, true, new Vector3(9f, 0f, 9f));
            Assert.That(tracker.LastKnownPosition, Is.EqualTo(new Vector3(9f, 0f, 9f)));
        }

        [Test]
        public void LongChaseAtLevelTwo_Escalates()
        {
            var tracker = new WantedTracker(10f, 5f);
            tracker.ReportCrime(2, Vector3.zero);
            Run(tracker, 4f, seen: true);
            Assert.That(tracker.Level, Is.EqualTo(2));
            Run(tracker, 2f, seen: true);
            Assert.That(tracker.Level, Is.EqualTo(3));

            var minor = new WantedTracker(10f, 5f);
            minor.ReportCrime(1, Vector3.zero);
            Run(minor, 20f, seen: true);
            Assert.That(minor.Level, Is.EqualTo(1), "A level 1 chase does not escalate on its own.");
        }

        [Test]
        public void Clear_IsNotAnEscape()
        {
            var tracker = new WantedTracker(10f, 30f);
            int escapes = 0, changes = 0;
            tracker.Escaped += _ => escapes++;
            tracker.LevelChanged += _ => changes++;
            tracker.ReportCrime(3, Vector3.zero);
            tracker.Clear();
            Assert.That(tracker.Level, Is.EqualTo(0));
            Assert.That(escapes, Is.EqualTo(0), "Being arrested must not pay out as a getaway.");
            Assert.That(changes, Is.EqualTo(2));
            Run(tracker, 30f, seen: false);
            Assert.That(escapes, Is.EqualTo(0));
        }
    }
}
