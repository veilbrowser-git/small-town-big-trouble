using System.Collections;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SmallTown.Networking
{
    /// <summary>
    /// Starts and stops sessions. Solo play is a host that accepts nobody, so
    /// there is one code path for one player or four. Local network play uses
    /// a direct address; nothing here needs a cloud service.
    /// </summary>
    public static class NetLauncher
    {
        public const ushort DefaultPort = 7777;
        public const int MaxPlayers = 4;
        public const string MenuScene = "Menu";
        public const string TownScene = "Town";

        static bool leaving;
        static Runner runner;

        /// <summary>True when the running session is single-player.</summary>
        public static bool Solo { get; private set; }
        /// <summary>Why the player was last returned to the menu, for the menu to show once.</summary>
        public static string LastMessage { get; set; }
        public static ushort Port { get; private set; } = DefaultPort;

        sealed class Runner : MonoBehaviour
        {
        }

        static NetworkManager EnsureManager()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                var prefab = Resources.Load<GameObject>("NetworkManager");
                if (prefab == null)
                {
                    Debug.LogError("[SmallTown] Resources/NetworkManager prefab is missing. Run Small Town > Setup > Run Everything.");
                    return null;
                }
                manager = Object.Instantiate(prefab).GetComponent<NetworkManager>();
                manager.name = "NetworkManager";
            }
            if (runner == null)
            {
                var host = new GameObject("NetLauncher");
                Object.DontDestroyOnLoad(host);
                runner = host.AddComponent<Runner>();
            }
            manager.OnClientStopped -= OnClientStopped;
            manager.OnClientStopped += OnClientStopped;
            leaving = false;
            return manager;
        }

        static UnityTransport Transport(NetworkManager manager) => (UnityTransport)manager.NetworkConfig.NetworkTransport;

        /// <summary>Starts a private single-player session in the scene that is already loaded.</summary>
        public static bool StartSolo()
        {
            NetworkManager manager = EnsureManager();
            if (manager == null || manager.IsListening) return manager != null;
            Solo = true;
            manager.ConnectionApprovalCallback = Approve;
            // Solo still opens a socket, but only on this machine and on a random port.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Port = (ushort)Random.Range(20000, 60000);
                Transport(manager).SetConnectionData("127.0.0.1", Port, "127.0.0.1");
                if (manager.StartHost()) return true;
            }
            Debug.LogError("[SmallTown] Could not start a solo session.");
            return false;
        }

        /// <summary>Hosts a co-op game on the local network and loads the town.</summary>
        public static bool StartHost(ushort port = DefaultPort)
        {
            NetworkManager manager = EnsureManager();
            if (manager == null) return false;
            if (manager.IsListening) manager.Shutdown();
            Solo = false;
            Port = port;
            manager.ConnectionApprovalCallback = Approve;
            Transport(manager).SetConnectionData("127.0.0.1", port, "0.0.0.0");
            if (!manager.StartHost())
            {
                LastMessage = "Could not host on port " + port + ". Is another game already hosting?";
                return false;
            }
            if (SceneManager.GetActiveScene().name != TownScene)
            {
                manager.SceneManager.LoadScene(TownScene, LoadSceneMode.Single);
            }
            return true;
        }

        /// <summary>Joins a host by address. The host's scene is loaded automatically once connected.</summary>
        public static bool StartClient(string address, ushort port = DefaultPort)
        {
            NetworkManager manager = EnsureManager();
            if (manager == null) return false;
            if (manager.IsListening) manager.Shutdown();
            Solo = false;
            Port = port;
            Transport(manager).SetConnectionData(address, port);
            return manager.StartClient();
        }

        static void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            NetworkManager manager = NetworkManager.Singleton;
            bool isHostItself = request.ClientNetworkId == NetworkManager.ServerClientId;
            bool room = manager.ConnectedClientsIds.Count < MaxPlayers;
            response.Approved = isHostItself || (!Solo && room);
            response.CreatePlayerObject = true;
            response.Pending = false;
            if (!response.Approved) response.Reason = Solo ? "That game is single-player." : "The game is full (" + MaxPlayers + " players).";
        }

        static void OnClientStopped(bool wasHost)
        {
            if (wasHost || leaving) return;
            // The host went away, or we never reached it. Either way: back to the menu, with a reason.
            // Say it in plain words. The transport's own reason is only useful when the
            // host refused us (full game, single-player game).
            NetworkManager manager = NetworkManager.Singleton;
            string detail = manager != null ? manager.DisconnectReason : null;
            bool wasPlaying = SceneManager.GetActiveScene().name == TownScene;
            string reason = wasPlaying
                ? "The host ended the game."
                : "Could not join that game." + (string.IsNullOrEmpty(detail) ? "" : " " + detail);
            Leave(reason);
        }

        /// <summary>Ends the session (if any) and returns to the menu. Safe to call from network callbacks.</summary>
        public static void Leave(string message = null)
        {
            if (leaving) return;
            leaving = true;
            LastMessage = message;
            if (runner != null) runner.StartCoroutine(LeaveRoutine());
            else ShutdownNow();
        }

        static IEnumerator LeaveRoutine()
        {
            // Wait a frame so we are not tearing the session down from inside its own callback.
            yield return null;
            ShutdownNow();
            SceneManager.LoadScene(MenuScene);
        }

        /// <summary>Stops the session without changing scene.</summary>
        public static void ShutdownNow()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null)
            {
                manager.OnClientStopped -= OnClientStopped;
                if (manager.IsListening) manager.Shutdown();
                Object.Destroy(manager.gameObject);
            }
            Solo = false;
        }

        /// <summary>This machine's address on the local network, for friends to type in.</summary>
        public static string LocalAddress()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // No packet is sent; this only asks the OS which interface it would use.
                    socket.Connect("10.255.255.255", 1);
                    if (socket.LocalEndPoint is IPEndPoint endPoint) return endPoint.Address.ToString();
                }
            }
            catch (SocketException)
            {
            }
            return "127.0.0.1";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            leaving = false;
            runner = null;
            Solo = false;
            LastMessage = null;
            Port = DefaultPort;
        }
    }
}
