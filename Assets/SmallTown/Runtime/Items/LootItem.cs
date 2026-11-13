using System;
using System.Collections.Generic;
using SmallTown.Interaction;
using UnityEngine;

namespace SmallTown.Items
{
    /// <summary>
    /// Economic facts about a physical item, kept apart from each other on
    /// purpose: who owns it, whether it is stolen, what it is worth, and
    /// whether it has already been paid out. Where it physically is (free,
    /// held, in a bag) lives on <see cref="GrabbableItem"/>.
    /// </summary>
    [RequireComponent(typeof(GrabbableItem))]
    [DisallowMultipleComponent]
    public sealed class LootItem : MonoBehaviour
    {
        static readonly List<LootItem> Registry = new List<LootItem>();

        [Tooltip("Stable ID of the item type.")]
        public string definitionId = "loot";
        public string displayName = "Loot";
        [Min(0)] public int value = 10;
        [Tooltip("Cash turns into wallet money once you have got away. Everything else is sold at the pawn shop.")]
        public bool isCash;
        [Tooltip("Who holds title: a shop ID such as 'shop:quickpaw', or a player's profile ID.")]
        public string owner = "";
        public bool stolen;

        string instanceId;

        /// <summary>True once this item has been turned into money. It can never pay out again.</summary>
        public bool PaidOut { get; private set; }
        /// <summary>Unique per physical item. Sale and payout transaction IDs are built from it.</summary>
        public string InstanceId => instanceId ??= Guid.NewGuid().ToString("N");
        public GrabbableItem Item { get; private set; }
        public static IReadOnlyList<LootItem> All => Registry;

        void Awake() => Item = GetComponent<GrabbableItem>();
        void OnEnable() => Registry.Add(this);
        void OnDisable() => Registry.Remove(this);

        public void MarkStolen() => stolen = true;

        /// <summary>Marks the item as converted to money. Returns false if it already was.</summary>
        public bool TryMarkPaidOut()
        {
            if (PaidOut) return false;
            PaidOut = true;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Registry.Clear();
    }
}
