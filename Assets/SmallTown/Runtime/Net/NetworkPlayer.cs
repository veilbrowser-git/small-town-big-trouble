using SmallTown.Avatar;
using SmallTown.Crime;
using SmallTown.Economy;
using SmallTown.Locomotion;
using Unity.Netcode;
using UnityEngine;

namespace SmallTown.Networking
{
    /// <summary>Where a player's head and paws are. Written by that player's own machine.</summary>
    public struct PlayerPose : INetworkSerializeByMemcpy
    {
        public Vector3 head;
        public Quaternion headRotation;
        public Vector3 left;
        public Quaternion leftRotation;
        public Vector3 right;
        public Quaternion rightRotation;
        public float bodyYaw;
        /// <summary>Bumped whenever the player teleports on purpose (respawn, arrest, unstuck).</summary>
        public byte teleportCount;
    }

    /// <summary>
    /// One player in a session. Each machine owns its own: it publishes where
    /// its head and paws are (never smoothed locally, always live), and shows
    /// everyone else's as a smoothed red panda. The host keeps each player's
    /// money and wanted level and sends them back to that player.
    /// </summary>
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        const byte PistolBit = 1;
        const byte SatchelBit = 2;

        [Header("Remote avatar (hidden for the owner)")]
        [SerializeField] GameObject avatarRoot;
        [SerializeField] Transform head;
        [SerializeField] Transform torso;
        [SerializeField] Transform[] upperArms = new Transform[2];
        [SerializeField] Transform[] forearms = new Transform[2];
        [SerializeField] Transform[] hands = new Transform[2];
        [SerializeField] Renderer[] tintable = new Renderer[0];
        [SerializeField] Material[] palettes = new Material[0];
        [SerializeField] Collider bodyCollider;
        public float armThickness = 0.16f;
        public float armSegment = 0.46f;
        [Tooltip("How quickly a remote player's avatar follows their latest reported pose.")]
        public float smoothing = 16f;

        readonly NetworkVariable<PlayerPose> pose = new NetworkVariable<PlayerPose>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<byte> colour = new NetworkVariable<byte>();
        readonly NetworkVariable<int> cash = new NetworkVariable<int>();
        readonly NetworkVariable<int> bank = new NetworkVariable<int>();
        readonly NetworkVariable<byte> unlocks = new NetworkVariable<byte>();
        readonly NetworkVariable<int> jobs = new NetworkVariable<int>();
        readonly NetworkVariable<int> robberies = new NetworkVariable<int>();
        readonly NetworkVariable<int> arrests = new NetworkVariable<int>();
        readonly NetworkVariable<byte> wantedLevel = new NetworkVariable<byte>();
        readonly NetworkVariable<bool> seen = new NetworkVariable<bool>();
        readonly NetworkVariable<float> escape = new NetworkVariable<float>();

        GameSession session;
        PlayerState state;
        PlayerRig rig;
        MotionValidator validator;
        PlayerPose shown;
        bool hasShown;
        byte teleports;
        bool profileSent;

        /// <summary>Host side: this player's session state. Null until the town has loaded.</summary>
        public PlayerState State => state;
        public PlayerPose Pose => pose.Value;
        public int Cash => cash.Value;
        public int WantedLevel => wantedLevel.Value;
        public int ColourIndex => colour.Value;

        public void Configure(GameObject avatar, Transform headPart, Transform torsoPart, Transform[] upper, Transform[] lower,
            Transform[] handParts, Renderer[] renderers, Material[] variants, Collider body)
        {
            avatarRoot = avatar;
            head = headPart;
            torso = torsoPart;
            upperArms = upper;
            forearms = lower;
            hands = handParts;
            tintable = renderers;
            palettes = variants;
            bodyCollider = body;
        }

        public override void OnNetworkSpawn()
        {
            name = "NetworkPlayer " + OwnerClientId + (IsOwner ? " (me)" : "");
            // You never see your own remote avatar; your rig draws your body.
            if (avatarRoot != null) avatarRoot.SetActive(!IsOwner);
            if (bodyCollider != null) bodyCollider.enabled = !IsOwner;
            colour.OnValueChanged += (_, __) => ApplyColour();
            if (IsServer) colour.Value = (byte)(OwnerClientId % 4);
            ApplyColour();

            if (IsOwner && !IsServer)
            {
                cash.OnValueChanged += (_, __) => MirrorWallet();
                bank.OnValueChanged += (_, __) => MirrorWallet();
                unlocks.OnValueChanged += (_, __) => MirrorWallet();
                jobs.OnValueChanged += (_, __) => MirrorWallet();
                robberies.OnValueChanged += (_, __) => MirrorWallet();
                arrests.OnValueChanged += (_, __) => MirrorWallet();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && session != null && state != null && !state.IsLocal) session.RemovePlayer(state.ClientId);
            Unhook();
        }

        void ApplyColour()
        {
            if (palettes.Length == 0) return;
            Material material = palettes[colour.Value % palettes.Length];
            foreach (Renderer r in tintable)
            {
                if (r != null) r.sharedMaterial = material;
            }
        }

        // ------------------------------------------------------------------ Binding to the scene's session

        void Bind()
        {
            GameSession current = GameSession.Instance;
            if (current == session) return;
            Unhook();
            session = current;
            state = null;
            profileSent = false;
            if (session == null) return;

            if (IsOwner)
            {
                session.RebindLocal(OwnerClientId);
                rig = session.Rig;
                if (rig != null) rig.Teleported += OnTeleported;
            }
            if (IsServer)
            {
                if (IsOwner)
                {
                    state = session.Local;
                }
                else
                {
                    state = session.AddPlayer(OwnerClientId, message => ToastRpc(message));
                    validator = new MotionValidator(session.Rig != null && session.Rig.Motor != null && session.Rig.Motor.Settings != null
                        ? session.Rig.Motor.Settings.maxSpeed
                        : 14f);
                }
                state.Index = (int)(OwnerClientId % 4);
            }
        }

        void Unhook()
        {
            if (rig != null) rig.Teleported -= OnTeleported;
            rig = null;
        }

        void OnTeleported() => teleports++;

        // ------------------------------------------------------------------ Per frame

        void Update()
        {
            if (!IsSpawned) return;
            Bind();
            if (IsOwner) OwnerUpdate();
            if (IsServer) HostUpdate();
            if (!IsOwner) ShowRemote();
            if (IsOwner && !IsServer) ClientView();
        }

        void OwnerUpdate()
        {
            if (rig == null || rig.Head == null) return;
            Transform leftHand = rig.HandTransform(Hand.Left);
            Transform rightHand = rig.HandTransform(Hand.Right);
            pose.Value = new PlayerPose
            {
                head = rig.Head.position,
                headRotation = rig.Head.rotation,
                left = leftHand != null ? leftHand.position : rig.Head.position,
                leftRotation = leftHand != null ? leftHand.rotation : Quaternion.identity,
                right = rightHand != null ? rightHand.position : rig.Head.position,
                rightRotation = rightHand != null ? rightHand.rotation : Quaternion.identity,
                bodyYaw = rig.BodyYaw.eulerAngles.y,
                teleportCount = teleports,
            };

            if (!IsServer && !profileSent && session != null)
            {
                // Hand the host our saved profile. It keeps the authoritative copy from here on.
                profileSent = true;
                PlayerProfile profile = session.Wallet.Profile;
                SubmitProfileRpc(profile.profileId, profile.wallet, profile.bank, UnlockBits(session.Wallet),
                    profile.jobsCompleted, profile.robberiesCompleted, profile.timesArrested);
            }
        }

        void HostUpdate()
        {
            if (state == null || session == null) return;
            if (!state.IsLocal)
            {
                PlayerPose current = pose.Value;
                state.Head = current.head;
                state.Feet = current.head + Vector3.down * 0.8f;
                if (validator != null && !validator.Check(current.head, Time.deltaTime, current.teleportCount))
                {
                    // An impossible move. Put them back where they last made sense.
                    CorrectPositionRpc(validator.LastValid);
                    validator.Reset();
                }
            }

            Wallet wallet = state.Wallet;
            Set(cash, wallet.Cash);
            Set(bank, wallet.Bank);
            Set(jobs, wallet.Profile.jobsCompleted);
            Set(robberies, wallet.Profile.robberiesCompleted);
            Set(arrests, wallet.Profile.timesArrested);
            byte bits = UnlockBits(wallet);
            if (unlocks.Value != bits) unlocks.Value = bits;
            byte level = (byte)state.Wanted.Level;
            if (wantedLevel.Value != level) wantedLevel.Value = level;
            if (seen.Value != state.Wanted.SeenByPolice) seen.Value = state.Wanted.SeenByPolice;
            float progress = Mathf.Round(state.Wanted.EscapeProgress * 20f) / 20f;
            if (!Mathf.Approximately(escape.Value, progress)) escape.Value = progress;
        }

        static void Set(NetworkVariable<int> variable, int value)
        {
            if (variable.Value != value) variable.Value = value;
        }

        static byte UnlockBits(Wallet wallet)
        {
            byte bits = 0;
            if (wallet.Owns(GameBalance.PistolUnlock)) bits |= PistolBit;
            if (wallet.Owns(GameBalance.SatchelUnlock)) bits |= SatchelBit;
            return bits;
        }

        void ClientView()
        {
            if (session == null) return;
            NetSession relay = NetSession.Instance;
            session.SetRemoteView(new HudView
            {
                cash = cash.Value,
                wantedLevel = wantedLevel.Value,
                seenByPolice = seen.Value,
                escapeProgress = escape.Value,
                objective = relay != null ? relay.Objective : "",
            });
        }

        void MirrorWallet()
        {
            if (session == null) return;
            session.MirrorWallet(cash.Value, bank.Value, (unlocks.Value & PistolBit) != 0, (unlocks.Value & SatchelBit) != 0,
                jobs.Value, robberies.Value, arrests.Value);
        }

        void ShowRemote()
        {
            if (avatarRoot == null || !avatarRoot.activeSelf) return;
            PlayerPose target = pose.Value;
            if (target.headRotation.x == 0f && target.headRotation.y == 0f && target.headRotation.z == 0f && target.headRotation.w == 0f) return;

            float distance = hasShown ? (target.head - shown.head).magnitude : 999f;
            if (distance > 3f)
            {
                shown = target; // a teleport: do not glide across the map
                hasShown = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
                shown.head = Vector3.Lerp(shown.head, target.head, t);
                shown.headRotation = Quaternion.Slerp(shown.headRotation, target.headRotation, t);
                shown.left = Vector3.Lerp(shown.left, target.left, t);
                shown.leftRotation = Quaternion.Slerp(shown.leftRotation, target.leftRotation, t);
                shown.right = Vector3.Lerp(shown.right, target.right, t);
                shown.rightRotation = Quaternion.Slerp(shown.rightRotation, target.rightRotation, t);
                shown.bodyYaw = Mathf.LerpAngle(shown.bodyYaw, target.bodyYaw, t);
            }

            transform.SetPositionAndRotation(shown.head + Vector3.down * 0.8f, Quaternion.Euler(0f, shown.bodyYaw, 0f));
            Quaternion facing = Quaternion.Euler(0f, shown.bodyYaw, 0f);
            if (head != null) head.SetPositionAndRotation(shown.head, shown.headRotation);
            if (torso != null) torso.SetPositionAndRotation(shown.head + Vector3.down * 0.19f, facing);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Vector3 hand = i == 0 ? shown.left : shown.right;
                Quaternion handRotation = i == 0 ? shown.leftRotation : shown.rightRotation;
                Vector3 shoulder = shown.head + facing * new Vector3(side * 0.21f, -0.26f, 0f);
                ArmSolver.Solve(shoulder, hand, armSegment, facing, side, upperArms[i], forearms[i], armThickness);
                if (hands[i] != null)
                {
                    // Paw meshes are modelled palm-down; rig hands are rolled so the palm faces inward.
                    hands[i].SetPositionAndRotation(hand, handRotation * Quaternion.Euler(0f, 0f, i == 0 ? 90f : -90f));
                }
            }
        }

        // ------------------------------------------------------------------ Messages

        [Rpc(SendTo.Server)]
        void SubmitProfileRpc(string profileId, int walletCash, int bankCash, byte unlockBits, int jobCount, int robberyCount, int arrestCount)
        {
            if (state == null || state.IsLocal || session == null) return;
            // The host trusts the profile a friend brings (see GameSession for the trust model).
            var profile = PlayerProfile.CreateNew(0);
            profile.profileId = string.IsNullOrEmpty(profileId) ? profile.profileId : profileId;
            profile.wallet = Mathf.Max(0, walletCash);
            profile.bank = Mathf.Max(0, bankCash);
            if ((unlockBits & PistolBit) != 0) profile.unlocks.Add(GameBalance.PistolUnlock);
            if ((unlockBits & SatchelBit) != 0) profile.unlocks.Add(GameBalance.SatchelUnlock);
            profile.jobsCompleted = jobCount;
            profile.robberiesCompleted = robberyCount;
            profile.timesArrested = arrestCount;
            state.ReplaceWallet(new Wallet(profile));
        }

        [Rpc(SendTo.Owner)]
        void ToastRpc(string message) => HudToast.Show(message);

        /// <summary>Host: play the arrest on this player's machine.</summary>
        public void SendArrest(Vector3 releasePosition, float yaw, ArrestResult result)
        {
            validator?.Reset();
            ArrestRpc(releasePosition, yaw, result.fine, result.confiscatedItems, result.wantedLevel);
        }

        [Rpc(SendTo.Owner)]
        void ArrestRpc(Vector3 releasePosition, float yaw, int fine, int confiscated, int level)
        {
            var arrest = FindAnyObjectByType<ArrestController>();
            if (arrest != null)
            {
                arrest.Present(releasePosition, yaw, new ArrestResult { fine = fine, confiscatedItems = confiscated, wantedLevel = level });
            }
        }

        [Rpc(SendTo.Owner)]
        void CorrectPositionRpc(Vector3 headPosition)
        {
            if (rig != null) rig.TeleportHead(headPosition);
        }

        /// <summary>Development only: gives this player money so automated co-op runs can afford things.</summary>
        public void RequestDevMoney(int amount) => DevMoneyRpc(amount);

        [Rpc(SendTo.Server)]
        void DevMoneyRpc(int amount)
        {
            if (!Debug.isDebugBuild || state == null) return;
            state.Wallet.Apply("dev:" + System.Guid.NewGuid().ToString("N"), Mathf.Clamp(amount, 0, 10000));
        }
    }
}
