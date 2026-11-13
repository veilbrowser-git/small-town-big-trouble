using SmallTown.Crime;
using SmallTown.Interaction;
using SmallTown.Jobs;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SmallTown.Networking
{
    /// <summary>
    /// The town's shared noticeboard. Everything a client needs to see of the
    /// host's simulation that is not an object in its own right goes through
    /// here: what every sign and terminal says, which beacons and roadblocks
    /// are switched on, world sounds, the current job objective, and button
    /// presses travelling the other way so the host can act on them for the
    /// player who pressed.
    /// </summary>
    public sealed class NetSession : NetworkBehaviour
    {
        const float PushInterval = 0.15f;

        [SerializeField] TMP_Text[] labels = new TMP_Text[0];
        [SerializeField] GameObject[] toggles = new GameObject[0];
        [SerializeField] PunchButton[] buttons = new PunchButton[0];
        [SerializeField] ArrestController arrest;

        NetworkList<FixedString512Bytes> labelTexts;
        readonly NetworkVariable<ulong> toggleBits = new NetworkVariable<ulong>();
        readonly NetworkVariable<FixedString128Bytes> objective = new NetworkVariable<FixedString128Bytes>();
        float pushAt;

        public static NetSession Instance { get; private set; }
        public string Objective => objective.Value.ToString();

        public void Configure(TMP_Text[] sharedLabels, GameObject[] sharedToggles, PunchButton[] sharedButtons, ArrestController arrestController)
        {
            labels = sharedLabels;
            toggles = sharedToggles;
            buttons = sharedButtons;
            arrest = arrestController;
        }

        void Awake()
        {
            Instance = this;
            labelTexts = new NetworkList<FixedString512Bytes>();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                if (buttons[i] != null) buttons[i].Relay = _ => RelayPress(index);
            }

            if (IsServer)
            {
                labelTexts.Clear();
                for (int i = 0; i < labels.Length; i++) labelTexts.Add(Text(labels[i]));
                GameFeedback.SharedRelay = (sound, position, volume, pitch) => SoundRpc((byte)sound, position, volume, pitch);
                if (arrest != null) arrest.RemotePresentation = PresentArrest;
                NetworkManager.OnClientDisconnectCallback += OnClientLeft;
                return;
            }

            labelTexts.OnListChanged += _ => ApplyLabels();
            toggleBits.OnValueChanged += (_, __) => ApplyToggles();
            ApplyLabels();
            ApplyToggles();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                GameFeedback.SharedRelay = null;
                if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
            }
            foreach (PunchButton button in buttons)
            {
                if (button != null) button.Relay = null;
            }
        }

        static FixedString512Bytes Text(TMP_Text label)
        {
            var text = new FixedString512Bytes();
            if (label != null && label.text != null) text.CopyFromTruncated(label.text);
            return text;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || Time.unscaledTime < pushAt) return;
            pushAt = Time.unscaledTime + PushInterval;

            for (int i = 0; i < labels.Length && i < labelTexts.Count; i++)
            {
                FixedString512Bytes text = Text(labels[i]);
                if (labelTexts[i] != text) labelTexts[i] = text;
            }

            ulong bits = 0;
            for (int i = 0; i < toggles.Length && i < 64; i++)
            {
                if (toggles[i] != null && toggles[i].activeSelf) bits |= 1UL << i;
            }
            if (toggleBits.Value != bits) toggleBits.Value = bits;

            Job job = JobManager.Instance != null ? JobManager.Instance.Active : null;
            var current = new FixedString128Bytes();
            if (job != null) current.CopyFromTruncated(job.Objective);
            if (objective.Value != current) objective.Value = current;
        }

        void ApplyLabels()
        {
            for (int i = 0; i < labels.Length && i < labelTexts.Count; i++)
            {
                if (labels[i] != null) labels[i].text = labelTexts[i].ToString();
            }
        }

        void ApplyToggles()
        {
            ulong bits = toggleBits.Value;
            for (int i = 0; i < toggles.Length && i < 64; i++)
            {
                bool on = (bits & (1UL << i)) != 0;
                if (toggles[i] != null && toggles[i].activeSelf != on) toggles[i].SetActive(on);
            }
        }

        // ------------------------------------------------------------------ Buttons

        bool RelayPress(int index)
        {
            if (!IsSpawned || IsServer) return false;
            PressRpc(index);
            return true;
        }

        [Rpc(SendTo.Server)]
        void PressRpc(int index, RpcParams rpc = default)
        {
            if (index < 0 || index >= buttons.Length || buttons[index] == null) return;
            GameSession session = GameSession.Instance;
            PlayerState player = session != null ? session.FindPlayer(rpc.Receive.SenderClientId) : null;
            if (player == null) return;
            PunchButton button = buttons[index];
            session.RunAs(player, () => button.onPressed.Invoke());
        }

        // ------------------------------------------------------------------ Sounds and arrests

        [Rpc(SendTo.NotServer)]
        void SoundRpc(byte sound, Vector3 position, float volume, float pitch)
        {
            GameFeedback.Play((FeedbackSound)sound, position, volume, pitch);
        }

        void PresentArrest(PlayerState player, Vector3 releasePosition, float yaw, ArrestResult result)
        {
            if (NetworkManager.ConnectedClients.TryGetValue(player.ClientId, out NetworkClient client) && client.PlayerObject != null)
            {
                var networkPlayer = client.PlayerObject.GetComponent<NetworkPlayer>();
                if (networkPlayer != null) networkPlayer.SendArrest(releasePosition, yaw, result);
            }
        }

        void OnClientLeft(ulong clientId)
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;
            PlayerState player = session.FindPlayer(clientId);
            if (player == null || player.IsLocal) return;
            session.RemovePlayer(clientId);
            session.TellAll("A player left the game.");
        }

        /// <summary>Host tool: cancels whatever job is running, for when a mission gets stuck.</summary>
        public void HostResetJob()
        {
            if (IsSpawned && !IsServer) return;
            if (JobManager.Instance != null) JobManager.Instance.CancelActive();
        }
    }
}
