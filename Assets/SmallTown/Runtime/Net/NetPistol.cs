using SmallTown.Items;
using Unity.Netcode;
using UnityEngine;

namespace SmallTown.Networking
{
    /// <summary>
    /// Shares pistol shots. The shooter sees and hears their own shot at once.
    /// What the bullet hits, and who gets blamed, is decided on the host, which
    /// then shows the shot to everyone else. Other players are never hit.
    /// </summary>
    [RequireComponent(typeof(Pistol))]
    public sealed class NetPistol : NetworkBehaviour
    {
        Pistol pistol;

        void Awake() => pistol = GetComponent<Pistol>();

        public override void OnNetworkSpawn() => pistol.ShotRelay = Relay;

        public override void OnNetworkDespawn()
        {
            if (pistol != null) pistol.ShotRelay = null;
        }

        bool Relay(Vector3 origin, Vector3 direction)
        {
            if (!IsSpawned) return false;
            if (IsServer)
            {
                // The host shot: it resolves the hit itself; just show everyone else.
                ShotFxRpc(origin, direction, NetworkManager.LocalClientId);
                return false;
            }
            FireRpc(origin, direction);
            return true;
        }

        [Rpc(SendTo.Server)]
        void FireRpc(Vector3 origin, Vector3 direction, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            // Only the player holding this pistol can fire it.
            var item = GetComponent<NetItem>();
            if (item != null && item.HolderClientId != sender) return;
            GameSession session = GameSession.Instance;
            PlayerState shooter = session != null ? session.FindPlayer(sender) : null;
            pistol.PlayShot(origin, direction);
            pistol.ResolveShot(origin, direction.normalized, shooter);
            ShotFxRpc(origin, direction, sender);
        }

        [Rpc(SendTo.NotServer)]
        void ShotFxRpc(Vector3 origin, Vector3 direction, ulong shooterClientId)
        {
            if (shooterClientId != NetworkManager.LocalClientId) pistol.PlayShot(origin, direction);
        }
    }
}
