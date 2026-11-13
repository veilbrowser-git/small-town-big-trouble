using System;

namespace SmallTown.Crime
{
    public enum RobberyState
    {
        /// <summary>Open for business; can be robbed.</summary>
        Idle,
        /// <summary>The cashier is complying and the register is paying out.</summary>
        HoldUp,
        /// <summary>Robbed recently. Nothing more to take until it restocks.</summary>
        Cooldown,
    }

    /// <summary>
    /// The rules of a register robbery with no Unity dependencies, so they can
    /// be tested exhaustively. The register holds a fixed number of bundles and
    /// only refills after the cooldown, which is what stops endless farming.
    /// </summary>
    public sealed class RobberyStateMachine
    {
        public struct Config
        {
            public int bundles;
            public float firstBundleDelay;
            public float bundleInterval;
            public float alarmDelay;
            public float cooldownSeconds;
            public float maxHoldUpSeconds;
            /// <summary>How long the robber can step out before the hold-up ends.</summary>
            public float absenceGraceSeconds;
        }

        readonly Config config;
        float holdUpTime;
        float nextBundleAt;
        float absentTime;

        public RobberyState State { get; private set; } = RobberyState.Idle;
        public int BundlesRemaining { get; private set; }
        public int BundlesDispensed => config.bundles - BundlesRemaining;
        public bool AlarmRaised { get; private set; }
        public float CooldownRemaining { get; private set; }

        public event Action Started;
        /// <summary>One bundle left the register. Argument: how many have been dispensed so far.</summary>
        public event Action<int> BundleDispensed;
        public event Action AlarmTriggered;
        /// <summary>The hold-up is over. True if the register was emptied.</summary>
        public event Action<bool> Ended;
        public event Action Restocked;

        public RobberyStateMachine(Config configuration)
        {
            config = configuration;
            BundlesRemaining = config.bundles;
        }

        /// <summary>Begins a hold-up. Refused unless the site is idle and stocked.</summary>
        public bool TryStart()
        {
            if (State != RobberyState.Idle || BundlesRemaining <= 0) return false;
            State = RobberyState.HoldUp;
            holdUpTime = 0f;
            absentTime = 0f;
            nextBundleAt = config.firstBundleDelay;
            AlarmRaised = false;
            Started?.Invoke();
            return true;
        }

        public void Tick(float deltaTime, bool robberPresent)
        {
            switch (State)
            {
                case RobberyState.HoldUp:
                    TickHoldUp(deltaTime, robberPresent);
                    break;
                case RobberyState.Cooldown:
                    CooldownRemaining -= deltaTime;
                    if (CooldownRemaining <= 0f)
                    {
                        CooldownRemaining = 0f;
                        BundlesRemaining = config.bundles;
                        AlarmRaised = false;
                        State = RobberyState.Idle;
                        Restocked?.Invoke();
                    }
                    break;
            }
        }

        void TickHoldUp(float deltaTime, bool robberPresent)
        {
            holdUpTime += deltaTime;
            absentTime = robberPresent ? 0f : absentTime + deltaTime;

            if (!AlarmRaised && holdUpTime >= config.alarmDelay)
            {
                AlarmRaised = true;
                AlarmTriggered?.Invoke();
            }

            while (robberPresent && BundlesRemaining > 0 && holdUpTime >= nextBundleAt)
            {
                BundlesRemaining--;
                nextBundleAt += config.bundleInterval;
                BundleDispensed?.Invoke(BundlesDispensed);
            }

            bool emptied = BundlesRemaining <= 0;
            if (emptied || absentTime >= config.absenceGraceSeconds || holdUpTime >= config.maxHoldUpSeconds)
            {
                // A robbery always alerts someone, even a very fast one.
                if (!AlarmRaised)
                {
                    AlarmRaised = true;
                    AlarmTriggered?.Invoke();
                }
                State = RobberyState.Cooldown;
                CooldownRemaining = config.cooldownSeconds;
                Ended?.Invoke(emptied);
            }
        }
    }
}
