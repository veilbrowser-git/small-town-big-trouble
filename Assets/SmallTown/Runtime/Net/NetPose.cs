using Unity.Netcode;
using UnityEngine;

namespace SmallTown.Networking
{
    /// <summary>
    /// Shares where an object is. Whoever owns it publishes its position and
    /// rotation; everyone else glides toward the latest value. The owner is the
    /// player holding (or who last held) an item, and the host for NPCs and the
    /// patrol car. For physics objects only the owner simulates; on every other
    /// machine the body is kinematic and simply follows.
    /// </summary>
    public sealed class NetPose : NetworkBehaviour
    {
        struct Snapshot : INetworkSerializeByMemcpy
        {
            public Vector3 position;
            public Quaternion rotation;
            public bool valid;
        }

        [Tooltip("Switch the rigidbody between simulated (owner) and following (everyone else).")]
        public bool managePhysics;
        [Min(1f)] public float smoothing = 18f;
        [Tooltip("Further than this from the latest value, snap instead of gliding (teleports, respawns).")]
        [Min(0.1f)] public float snapDistance = 2.5f;

        const float PositionEpsilon = 0.003f;
        const float AngleEpsilon = 1f;

        readonly NetworkVariable<Snapshot> shared = new NetworkVariable<Snapshot>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        Rigidbody body;

        void Awake() => body = GetComponent<Rigidbody>();

        public override void OnNetworkSpawn()
        {
            ApplyAuthority();
            if (!IsOwner && shared.Value.valid) transform.SetPositionAndRotation(shared.Value.position, shared.Value.rotation);
        }

        public override void OnGainedOwnership() => ApplyAuthority();
        public override void OnLostOwnership() => ApplyAuthority();

        void ApplyAuthority()
        {
            if (!managePhysics || body == null || !IsSpawned) return;
            if (!IsOwner)
            {
                body.isKinematic = true;
            }
            else if (body.isKinematic)
            {
                // Taking over a body that was only following: let physics have it again,
                // unless the item is tucked away in a satchel.
                var item = GetComponent<SmallTown.Interaction.GrabbableItem>();
                if (item == null || item.State != SmallTown.Interaction.ItemState.Stored) body.isKinematic = false;
            }
        }

        void Update()
        {
            if (!IsSpawned) return;
            Snapshot current = shared.Value;
            if (IsOwner)
            {
                bool moved = !current.valid ||
                             (transform.position - current.position).sqrMagnitude > PositionEpsilon * PositionEpsilon ||
                             Quaternion.Angle(transform.rotation, current.rotation) > AngleEpsilon;
                if (moved) shared.Value = new Snapshot { position = transform.position, rotation = transform.rotation, valid = true };
                return;
            }

            if (!current.valid) return;
            if ((transform.position - current.position).sqrMagnitude > snapDistance * snapDistance)
            {
                transform.SetPositionAndRotation(current.position, current.rotation);
                return;
            }
            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, current.position, t),
                Quaternion.Slerp(transform.rotation, current.rotation, t));
        }
    }
}
