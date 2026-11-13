using System.Collections.Generic;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Npc;
using TMPro;
using UnityEngine;

namespace SmallTown.Jobs
{
    /// <summary>Someone waiting for a delivery. Lights a beacon while they are the active drop-off.</summary>
    public sealed class DeliveryCustomer : MonoBehaviour
    {
        public string placeName = "the house";
        [SerializeField] BoxZone zone;
        [SerializeField] GameObject beacon;
        [SerializeField] NpcBody body;

        public BoxZone Zone => zone;

        public void Configure(BoxZone dropZone, GameObject beaconObject, NpcBody npc)
        {
            zone = dropZone;
            beacon = beaconObject;
            body = npc;
        }

        void Awake() => SetWaiting(false);

        public void SetWaiting(bool waiting)
        {
            if (beacon != null) beacon.SetActive(waiting);
            if (body != null) body.pose = waiting ? NpcPose.Wave : NpcPose.Idle;
        }

        public void Celebrate()
        {
            if (body != null) body.pose = NpcPose.HandsUp;
        }
    }
}
