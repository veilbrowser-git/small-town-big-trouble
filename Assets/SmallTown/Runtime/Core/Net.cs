using Unity.Netcode;
using UnityEngine;

namespace SmallTown
{
    /// <summary>
    /// One place that answers "is this a network session, and am I in charge?".
    /// Offline (no session running) this machine is always the authority, which
    /// is what lets every gameplay system run unchanged in solo tests.
    /// </summary>
    public static class Net
    {
        public static NetworkManager Manager => NetworkManager.Singleton;

        /// <summary>True while hosting or connected as a client.</summary>
        public static bool Active => Manager != null && Manager.IsListening;

        /// <summary>True on the machine that runs the world: the host, or any machine playing offline.</summary>
        public static bool IsHost => !Active || Manager.IsServer;

        /// <summary>True on a machine that is connected to someone else's game.</summary>
        public static bool IsClientOnly => Active && !Manager.IsServer;

        public static ulong LocalClientId => Active ? Manager.LocalClientId : 0;

        public static int PlayerCount => Active && Manager.IsServer ? Manager.ConnectedClientsIds.Count : 1;

        /// <summary>
        /// Creates a world object. In a session only the host may do this, and a
        /// prefab that is a network object is spawned for everyone.
        /// </summary>
        public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation) where T : Component
        {
            T instance = Object.Instantiate(prefab, position, rotation);
            if (Active && Manager.IsServer)
            {
                var networkObject = instance.GetComponent<NetworkObject>();
                if (networkObject != null && !networkObject.IsSpawned) networkObject.Spawn(true);
            }
            return instance;
        }

        /// <summary>Removes a world object for everyone. Safe to call for plain objects too.</summary>
        public static void Despawn(GameObject target)
        {
            if (target == null) return;
            var networkObject = target.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
            {
                if (Manager != null && Manager.IsServer) networkObject.Despawn(true);
                return;
            }
            Object.Destroy(target);
        }
    }
}
