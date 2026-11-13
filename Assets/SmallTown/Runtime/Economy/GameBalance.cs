using System;
using UnityEngine;

namespace SmallTown.Economy
{
    [Serializable]
    public sealed class MerchandiseType
    {
        public string id = "snack";
        public string displayName = "Snack";
        [Min(0)] public int value = 10;
        public Color colour = Color.white;
        public Vector3 size = new Vector3(0.12f, 0.16f, 0.08f);
    }

    /// <summary>Every price, payout, timer and limit in one data asset, so balancing never needs a code change.</summary>
    [CreateAssetMenu(menuName = "Small Town/Game Balance", fileName = "GameBalance")]
    public sealed class GameBalance : ScriptableObject
    {
        public const string PistolUnlock = "equipment.pistol";
        public const string SatchelUnlock = "equipment.big_satchel";

        [Header("Start")]
        [Min(0)] public int startingMoney = 20;

        [Header("Pizza delivery")]
        [Min(0)] public int pizzaPayout = 60;
        [Min(0)] public int pizzaSpeedTip = 25;
        [Min(1f)] public float pizzaTipSeconds = 120f;
        [Min(0.5f)] public float pizzaCookSeconds = 5f;

        [Header("Shop prices")]
        [Min(0)] public int pistolPrice = 110;
        [Min(0)] public int satchelPrice = 180;
        [Min(1)] public int baseSatchelCapacity = 4;
        [Min(1)] public int bigSatchelCapacity = 8;

        [Header("Convenience store robbery")]
        [Min(1)] public int registerBundles = 5;
        [Min(1)] public int bundleValue = 45;
        [Min(0.1f)] public float firstBundleDelay = 2f;
        [Min(0.1f)] public float bundleInterval = 1.4f;
        [Min(0f)] public float alarmDelay = 7f;
        [Min(1f)] public float robberyCooldown = 150f;
        [Min(1f)] public float maxHoldUpSeconds = 40f;
        [Min(0.1f)] public float holdUpAimSeconds = 0.8f;
        [Min(1f)] public float holdUpRange = 5f;
        public MerchandiseType[] merchandise =
        {
            new MerchandiseType { id = "snack", displayName = "Paw Puffs", value = 12, colour = new Color(0.96f, 0.77f, 0.19f) },
            new MerchandiseType { id = "soda", displayName = "Fizz Soda", value = 9, colour = new Color(0.88f, 0.27f, 0.23f), size = new Vector3(0.08f, 0.16f, 0.08f) },
            new MerchandiseType { id = "gadget", displayName = "Pocket Radio", value = 38, colour = new Color(0.25f, 0.44f, 0.71f), size = new Vector3(0.16f, 0.1f, 0.06f) },
        };

        [Header("Pawn shop")]
        [Range(0f, 1f)] public float pawnRate = 0.6f;

        [Header("Wanted level and police")]
        [Min(1f)] public float escapeSeconds = 20f;
        [Tooltip("Seconds of being chased at level 2 before it becomes level 3.")]
        [Min(1f)] public float escalateSeconds = 35f;
        public int[] officersPerLevel = { 1, 1, 3, 5 };
        [Min(1f)] public float sightRange = 22f;
        [Range(10f, 360f)] public float sightAngle = 150f;
        [Min(0f)] public float alwaysNoticeRange = 3f;
        [Min(0.1f)] public float arrestRange = 1.0f;
        [Min(0.1f)] public float arrestSeconds = 1.2f;
        [Min(1f)] public float searchSeconds = 18f;
        [Min(0.5f)] public float officerPatrolSpeed = 1.4f;
        public float[] officerChaseSpeed = { 2.4f, 2.4f, 2.9f, 3.3f };
        [Min(0f)] public float witnessReportDelay = 3f;
        [Min(0f)] public float stunSeconds = 4f;

        [Header("Arrest")]
        [Min(0)] public int fineBase = 30;
        [Min(0)] public int finePerLevel = 30;

        public int FineFor(int wantedLevel) => fineBase + finePerLevel * Mathf.Max(0, wantedLevel);
        public int OfficersFor(int level) => officersPerLevel[Mathf.Clamp(level, 0, officersPerLevel.Length - 1)];
        public float ChaseSpeedFor(int level) => officerChaseSpeed[Mathf.Clamp(level, 0, officerChaseSpeed.Length - 1)];
        public int PawnPrice(int value) => Mathf.Max(1, Mathf.RoundToInt(value * pawnRate));
    }
}
