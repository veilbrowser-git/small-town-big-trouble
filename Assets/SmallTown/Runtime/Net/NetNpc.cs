using SmallTown.Crime;
using SmallTown.Npc;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace SmallTown.Networking
{
    /// <summary>
    /// Shares a townsperson or officer with every player. The host thinks for
    /// them; clients switch the thinking off and just show the result: where
    /// they are (a NetworkTransform does that), what pose they are in, and
    /// whether they are on duty at all.
    /// </summary>
    [RequireComponent(typeof(NpcBody))]
    public sealed class NetNpc : NetworkBehaviour
    {
        readonly NetworkVariable<byte> pose = new NetworkVariable<byte>();
        readonly NetworkVariable<bool> present = new NetworkVariable<bool>(true);
        readonly NetworkVariable<Vector3> reach = new NetworkVariable<Vector3>();
        NpcBody body;

        void Awake() => body = GetComponent<NpcBody>();

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Push();
                return;
            }
            // Clients never run NPC brains or path-finding.
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            var officer = GetComponent<PoliceOfficer>();
            if (officer != null) officer.enabled = false;
            var pedestrian = GetComponent<Pedestrian>();
            if (pedestrian != null) pedestrian.enabled = false;

            pose.OnValueChanged += (_, __) => Apply();
            present.OnValueChanged += (_, __) => Apply();
            reach.OnValueChanged += (_, __) => Apply();
            Apply();
        }

        void LateUpdate()
        {
            if (IsSpawned && IsServer) Push();
        }

        void Push()
        {
            if ((byte)body.pose != pose.Value) pose.Value = (byte)body.pose;
            if (body.Present != present.Value) present.Value = body.Present;
            if (body.pose == NpcPose.Reach && (body.reachTarget - reach.Value).sqrMagnitude > 0.01f) reach.Value = body.reachTarget;
        }

        void Apply()
        {
            body.pose = (NpcPose)pose.Value;
            body.reachTarget = reach.Value;
            body.SetPresent(present.Value);
        }
    }
}
