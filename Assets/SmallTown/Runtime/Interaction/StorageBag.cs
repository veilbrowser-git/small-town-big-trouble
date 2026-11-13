using System;
using System.Collections.Generic;
using SmallTown.Locomotion;
using TMPro;
using UnityEngine;

namespace SmallTown.Interaction
{
    /// <summary>
    /// A satchel worn on the hip. Let go of an item with your hand inside the
    /// bag to store it; grip with an empty hand inside the bag to take the most
    /// recent item back out. The bag follows the body, not the head's every
    /// glance, so it stays where the player expects to reach.
    /// </summary>
    public sealed class StorageBag : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [Min(1)] public int capacity = 4;
        [Min(0.05f)] public float radius = 0.17f;
        [Tooltip("Position relative to the head, in body-facing space.")]
        public Vector3 bodyOffset = new Vector3(-0.27f, -0.5f, 0.08f);
        [Tooltip("The bag turns to follow the head only after the head has turned this far.")]
        [Range(0f, 90f)] public float followAngle = 35f;
        [Min(0.02f)] public float storedItemSize = 0.09f;
        [SerializeField] Transform slotRoot;
        [SerializeField] Transform visual;
        [SerializeField] TMP_Text countLabel;

        readonly List<GrabbableItem> items = new List<GrabbableItem>();
        float yaw;
        bool yawSet;
        float pulse;

        public event Action<StorageBag> Changed;
        public event Action<GrabbableItem> Stored;
        public event Action<GrabbableItem> Taken;
        public event Action<GrabbableItem> Rejected;

        public IReadOnlyList<GrabbableItem> Items => items;
        public int UsedSpace
        {
            get
            {
                int used = 0;
                foreach (var item in items) used += item.storageSize;
                return used;
            }
        }

        public void Configure(PlayerRig owner, Transform slots, Transform bagVisual, TMP_Text label)
        {
            rig = owner;
            slotRoot = slots;
            visual = bagVisual;
            countLabel = label;
        }

        void OnEnable()
        {
            if (rig != null) rig.Simulated += Follow;
            RefreshLabel();
        }

        void OnDisable()
        {
            if (rig != null) rig.Simulated -= Follow;
        }

        void Follow(float deltaTime)
        {
            Transform head = rig.Head;
            if (head == null) return;

            float headYaw = rig.BodyYaw.eulerAngles.y;
            if (!yawSet)
            {
                yaw = headYaw;
                yawSet = true;
            }
            float difference = Mathf.DeltaAngle(yaw, headYaw);
            if (Mathf.Abs(difference) > followAngle) yaw += difference - Mathf.Sign(difference) * followAngle;

            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            transform.SetPositionAndRotation(head.position + facing * bodyOffset, facing);

            if (visual != null)
            {
                pulse = Mathf.MoveTowards(pulse, 0f, deltaTime * 4f);
                visual.localScale = Vector3.one * (1f + pulse * 0.25f);
            }
        }

        public bool Contains(Vector3 worldPoint) => (worldPoint - transform.position).sqrMagnitude <= radius * radius;

        public bool HasRoomFor(GrabbableItem item) => item != null && item.storable && UsedSpace + item.storageSize <= capacity;

        /// <summary>Stores an item. <paramref name="from"/> is the hand letting go, if any.</summary>
        public bool TryStore(GrabbableItem item, HandGrabber from = null)
        {
            if (item == null || items.Contains(item)) return false;
            if (!HasRoomFor(item))
            {
                GameFeedback.Play(FeedbackSound.Reject, transform.position, 0.7f);
                if (from != null) rig.Input?.Haptic(from.Hand, 0.9f, 0.15f);
                Rejected?.Invoke(item);
                return false;
            }

            Transform slot = slotRoot != null ? slotRoot : transform;
            item.EnterStorage(slot, storedItemSize);
            items.Add(item);
            LayoutItems();

            pulse = 1f;
            GameFeedback.Play(FeedbackSound.Store, transform.position, 0.8f);
            if (from != null) rig.Input?.Haptic(from.Hand, 0.6f, 0.08f);
            RefreshLabel();
            Stored?.Invoke(item);
            Changed?.Invoke(this);
            return true;
        }

        /// <summary>Removes the most recently stored item and places it at the given pose.</summary>
        public GrabbableItem TakeLast(Vector3 position, Quaternion rotation)
        {
            if (items.Count == 0) return null;
            GrabbableItem item = items[items.Count - 1];
            return Take(item, position, rotation) ? item : null;
        }

        public bool Take(GrabbableItem item, Vector3 position, Quaternion rotation)
        {
            if (!items.Remove(item)) return false;
            item.ExitStorage(position, rotation);
            LayoutItems();
            pulse = 0.6f;
            GameFeedback.Play(FeedbackSound.Take, transform.position, 0.8f);
            RefreshLabel();
            Taken?.Invoke(item);
            Changed?.Invoke(this);
            return true;
        }

        /// <summary>Removes an item from the bag without putting it back in the world (sold, confiscated).</summary>
        public bool Remove(GrabbableItem item)
        {
            if (!items.Remove(item)) return false;
            LayoutItems();
            RefreshLabel();
            Changed?.Invoke(this);
            return true;
        }

        void LayoutItems()
        {
            // Stored items sit in a small ring so the player can see what is inside.
            for (int i = 0; i < items.Count; i++)
            {
                float angle = i * Mathf.PI * 2f / Mathf.Max(1, capacity);
                items[i].transform.localPosition = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.045f;
                items[i].transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Call after changing capacity so the label matches.</summary>
        public void Refresh() => RefreshLabel();

        void RefreshLabel()
        {
            if (countLabel != null) countLabel.text = UsedSpace + "/" + capacity;
        }
    }
}
