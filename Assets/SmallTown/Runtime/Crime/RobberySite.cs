using System.Collections.Generic;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using SmallTown.Npc;
using TMPro;
using UnityEngine;

namespace SmallTown.Crime
{
    /// <summary>
    /// A shop that can be held up. Point a pistol at the cashier (or fire it
    /// inside) to start; the register then pays out a fixed number of cash
    /// bundles, an alarm goes off, and the shop stays empty until it restocks.
    /// Merchandise carried out without a hold-up is shoplifting.
    /// </summary>
    public sealed class RobberySite : MonoBehaviour
    {
        [Tooltip("Stable ID. Also the owner written onto this shop's goods.")]
        public string siteId = "shop:quickpaw";
        public string displayName = "QuickPaw Mart";
        [SerializeField] BoxZone zone;
        [SerializeField] NpcBody cashier;
        [SerializeField] Transform cashSpawn;
        [SerializeField] Transform alarmPoint;
        [SerializeField] LootItem cashPrefab;
        [SerializeField] Transform[] merchSpots = new Transform[0];
        [SerializeField] LootItem[] merchPrefabs = new LootItem[0];
        [SerializeField] TMP_Text statusLabel;

        readonly List<LootItem> stock = new List<LootItem>();
        float aimTime;
        float alarmSoundAt;
        float alarmUntil;
        bool anyCashThisRobbery;

        public RobberyStateMachine Machine { get; private set; }
        public BoxZone Zone => zone;
        public IReadOnlyList<LootItem> Stock => stock;

        public void Configure(BoxZone shopZone, NpcBody cashierBody, Transform cashPoint, Transform alarm, LootItem cash,
            Transform[] spots, LootItem[] merch, TMP_Text label)
        {
            zone = shopZone;
            cashier = cashierBody;
            cashSpawn = cashPoint;
            alarmPoint = alarm;
            cashPrefab = cash;
            merchSpots = spots;
            merchPrefabs = merch;
            statusLabel = label;
        }

        void Start()
        {
            GameBalance balance = GameSession.Instance.Balance;
            Machine = new RobberyStateMachine(new RobberyStateMachine.Config
            {
                bundles = balance.registerBundles,
                firstBundleDelay = balance.firstBundleDelay,
                bundleInterval = balance.bundleInterval,
                alarmDelay = balance.alarmDelay,
                cooldownSeconds = balance.robberyCooldown,
                maxHoldUpSeconds = balance.maxHoldUpSeconds,
                absenceGraceSeconds = 3f,
            });
            Machine.Started += OnStarted;
            Machine.BundleDispensed += OnBundle;
            Machine.AlarmTriggered += OnAlarm;
            Machine.Ended += OnEnded;
            Machine.Restocked += OnRestocked;
            Pistol.Fired += OnPistolFired;
            Restock();
            RefreshStatus();
        }

        void OnDestroy() => Pistol.Fired -= OnPistolFired;

        void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the site. Public so tests can step it.</summary>
        public void Tick(float deltaTime)
        {
            GameSession session = GameSession.Instance;
            if (session == null || Machine == null) return;
            Vector3 playerHead = session.Rig.Head.position;
            bool playerInside = zone.Contains(playerHead);

            Machine.Tick(deltaTime, playerInside);

            if (Machine.State == RobberyState.Idle)
            {
                bool aimed = false;
                if (playerInside)
                {
                    foreach (Pistol pistol in Pistol.All)
                    {
                        if (pistol.IsHeld && AimedAtCashier(pistol, session.Balance.holdUpRange)) aimed = true;
                    }
                }
                aimTime = aimed ? aimTime + deltaTime : 0f;
                if (aimTime >= session.Balance.holdUpAimSeconds) StartHoldUp();
            }

            CheckShoplifting(session);

            if (Time.time < alarmUntil && Time.time >= alarmSoundAt)
            {
                alarmSoundAt = Time.time + 0.7f;
                GameFeedback.Play(FeedbackSound.Alarm, alarmPoint != null ? alarmPoint.position : transform.position);
            }
            RefreshStatus();
        }

        /// <summary>Starts the hold-up if the shop is open and stocked.</summary>
        public bool StartHoldUp() => Machine.TryStart();

        bool AimedAtCashier(Pistol pistol, float range)
        {
            if (cashier == null) return false;
            Vector3 toCashier = cashier.EyePosition - pistol.AimOrigin;
            float distance = toCashier.magnitude;
            if (distance > range || distance < 0.05f) return false;
            if (Vector3.Angle(pistol.AimDirection, toCashier) > 16f) return false;
            return !Physics.Linecast(pistol.AimOrigin, cashier.EyePosition, 1 << GameLayers.World, QueryTriggerInteraction.Ignore);
        }

        void OnPistolFired(Pistol pistol)
        {
            if (Machine != null && Machine.State == RobberyState.Idle && zone.Contains(pistol.AimOrigin)) StartHoldUp();
        }

        void OnStarted()
        {
            aimTime = 0f;
            anyCashThisRobbery = false;
            if (cashier != null) cashier.pose = NpcPose.HandsUp;
            HudToast.Show("Hold-up! Grab the cash as it comes out.");
            CrimeEvents.Raise(CrimeKind.ArmedRobbery, transform.position, 2);
        }

        void OnBundle(int count)
        {
            if (cashPrefab == null || cashSpawn == null) return;
            GameBalance balance = GameSession.Instance.Balance;
            Vector3 jitter = new Vector3((count % 3 - 1) * 0.12f, 0.05f + count * 0.012f, (count % 2) * 0.1f);
            LootItem cash = Instantiate(cashPrefab, cashSpawn.position + jitter, cashSpawn.rotation);
            cash.value = balance.bundleValue;
            cash.isCash = true;
            cash.owner = siteId;
            cash.MarkStolen();
            anyCashThisRobbery = true;
            GameFeedback.Play(FeedbackSound.Cash, cashSpawn.position);
        }

        void OnAlarm()
        {
            alarmUntil = Time.time + 12f;
            Vector3 position = alarmPoint != null ? alarmPoint.position : transform.position;
            GameSession.Instance.Wanted.ReportCrime(2, position);
            HudToast.Show("The alarm is ringing. Police are on the way.");
        }

        void OnEnded(bool emptied)
        {
            if (cashier != null) cashier.pose = NpcPose.Cower;
            if (anyCashThisRobbery) GameSession.Instance.Wallet.Profile.robberiesCompleted++;
            GameSession.Instance.MarkDirty();
            HudToast.Show(emptied ? "Register is empty. Get out of here!" : "The hold-up is over.");
        }

        void OnRestocked()
        {
            if (cashier != null) cashier.pose = NpcPose.Idle;
            Restock();
        }

        /// <summary>Fills every empty shelf spot. Goods that are still in place are left alone.</summary>
        public void Restock()
        {
            while (stock.Count < merchSpots.Length) stock.Add(null);
            if (merchPrefabs.Length == 0) return;
            for (int i = 0; i < merchSpots.Length; i++)
            {
                LootItem existing = stock[i];
                bool inPlace = existing != null && !existing.stolen &&
                               (existing.transform.position - merchSpots[i].position).sqrMagnitude < 0.3f * 0.3f;
                if (inPlace) continue;
                LootItem prefab = merchPrefabs[i % merchPrefabs.Length];
                LootItem item = Instantiate(prefab, merchSpots[i].position + Vector3.up * 0.09f, merchSpots[i].rotation);
                item.owner = siteId;
                stock[i] = item;
            }
        }

        void CheckShoplifting(GameSession session)
        {
            for (int i = 0; i < stock.Count; i++)
            {
                LootItem item = stock[i];
                if (item == null || item.stolen || item.owner != siteId) continue;
                if (zone.Contains(item.transform.position)) continue;

                item.MarkStolen();
                // During a hold-up the alarm already covers it. Otherwise the cashier shouts.
                if (Machine.State == RobberyState.Idle)
                {
                    CrimeEvents.Raise(CrimeKind.Shoplifting, item.transform.position, 1);
                    session.Wanted.ReportCrime(1, item.transform.position);
                    HudToast.Show("\"Hey! You didn't pay for that!\"");
                }
            }
        }

        void RefreshStatus()
        {
            if (statusLabel == null || Machine == null) return;
            switch (Machine.State)
            {
                case RobberyState.Idle:
                    statusLabel.text = "<b>REGISTER</b>\nOpen";
                    break;
                case RobberyState.HoldUp:
                    statusLabel.text = "<b>HOLD-UP</b>\n" + Machine.BundlesRemaining + " bundles left";
                    break;
                default:
                    int seconds = Mathf.CeilToInt(Machine.CooldownRemaining);
                    statusLabel.text = "<b>CLEANED OUT</b>\nRestock in " + (seconds / 60) + ":" + (seconds % 60).ToString("00");
                    break;
            }
        }
    }
}
