using System.Collections.Generic;
using System.Reflection;
using SmallTown.Networking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

namespace SmallTown.EditorTools
{
    /// <summary>
    /// Builds the networking assets: makes prefabs shared objects, builds the
    /// player prefab other people see, and the NetworkManager prefab with its
    /// list of spawnable prefabs.
    /// </summary>
    public static class NetBuilder
    {
        public const string ManagerPath = ProjectSetup.Root + "/Resources/NetworkManager.prefab";
        public const string PrefabListPath = ProjectSetup.SettingsFolder + "/NetworkPrefabs.asset";
        public const string PlayerPath = ContentBuilder.PrefabFolder + "/NetworkPlayer.prefab";

        static readonly List<GameObject> Spawnable = new List<GameObject>();

        public static void Reset() => Spawnable.Clear();

        /// <summary>Remembers a prefab that the host creates at runtime, so every machine can spawn it.</summary>
        public static void Register(GameObject prefab)
        {
            if (prefab != null && !Spawnable.Contains(prefab)) Spawnable.Add(prefab);
        }

        /// <summary>
        /// Turns an object into a shared one. ownerMoves: whoever owns it moves it
        /// (held items); otherwise only the host does (NPCs, the patrol car).
        /// </summary>
        public static NetworkObject MakeShared(GameObject root, bool syncTransform, bool ownerMoves, bool physics)
        {
            var networkObject = root.GetComponent<NetworkObject>();
            if (networkObject == null) networkObject = root.AddComponent<NetworkObject>();
            // If its owner leaves, the object stays and the host takes it over.
            networkObject.DontDestroyWithOwner = true;

            if (syncTransform)
            {
                // The owner publishes the pose: the holder for items, the host for NPCs
                // and the patrol car (which the host always owns).
                var pose = root.GetComponent<NetPose>();
                if (pose == null) pose = root.AddComponent<NetPose>();
                pose.managePhysics = physics;
            }
            return networkObject;
        }

        /// <summary>
        /// Network objects are identified by a hash that the editor normally fills
        /// in when you touch them. Objects made by script need it done explicitly.
        /// </summary>
        public static void RefreshHash(NetworkObject networkObject)
        {
            MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (validate != null) validate.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);
        }

        public static uint HashOf(NetworkObject networkObject)
        {
            FieldInfo field = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field != null ? (uint)field.GetValue(networkObject) : 0u;
        }

        /// <summary>Checks every saved prefab has its hash, fixing any that do not.</summary>
        public static void VerifyPrefabHash(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            if (networkObject == null) return;
            if (HashOf(networkObject) == 0)
            {
                RefreshHash(networkObject);
                AssetDatabase.SaveAssetIfDirty(prefab);
            }
            if (HashOf(networkObject) == 0) Debug.LogError("[SmallTown] Network prefab has no ID hash: " + path);
        }

        // ------------------------------------------------------------------ Player others see

        public static GameObject BuildNetworkPlayer()
        {
            var root = new GameObject("NetworkPlayer") { layer = GameLayers.RemotePlayer };
            var avatar = new GameObject("Avatar") { layer = GameLayers.RemotePlayer };
            avatar.transform.SetParent(root.transform, false);

            Material material = CharacterBuilder.Palette();
            Transform head = CharacterBuilder.PartObject("Head", avatar.transform, material, new Vector3(0.32f, 0.3f, 0.3f));
            Transform torso = CharacterBuilder.PartObject("Torso", avatar.transform, material, new Vector3(0.4f, 0.44f, 0.24f));
            var upper = new Transform[2];
            var lower = new Transform[2];
            var hands = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                upper[i] = CharacterBuilder.PartObject("UpperArm", avatar.transform, material, Vector3.one, side + "UpperArm");
                lower[i] = CharacterBuilder.PartObject("Forearm", avatar.transform, material, Vector3.one, side + "Forearm");
                hands[i] = CharacterBuilder.PartObject(i == 0 ? "HandL" : "HandR", avatar.transform, material, new Vector3(0.11f, 0.05f, 0.14f), side + "Hand");
                if (CharacterBuilder.Part(i == 0 ? "HandL" : "HandR") != null) hands[i].localScale = Vector3.one * CharacterBuilder.HandScale;
            }
            head.localPosition = new Vector3(0f, 0.8f, 0f);
            torso.localPosition = new Vector3(0f, 0.61f, 0f);
            GameLayers.SetLayerRecursively(root, GameLayers.RemotePlayer);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.5f, 0f);
            capsule.radius = 0.2f;
            capsule.height = 1f;

            // Each player in a session gets a different fur colour so friends can tell each other apart.
            Material[] variants =
            {
                CharacterBuilder.Palette(),
                CharacterBuilder.Palette("CitizenC"),
                CharacterBuilder.Palette("Broker"),
                CharacterBuilder.Palette("CitizenB"),
            };

            MakeShared(root, false, false, false).DontDestroyWithOwner = false;
            var player = root.AddComponent<NetworkPlayer>();
            player.Configure(avatar, head, torso, upper, lower, hands, avatar.GetComponentsInChildren<Renderer>(), variants, capsule);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Object.DestroyImmediate(root);
            VerifyPrefabHash(PlayerPath);
            return prefab;
        }

        // ------------------------------------------------------------------ NetworkManager

        public static void BuildNetworkManager(GameObject playerPrefab)
        {
            ProjectSetup.EnsureFolder(ProjectSetup.Root + "/Resources");

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
            }
            var serialized = new SerializedObject(list);
            SerializedProperty entries = serialized.FindProperty("List");
            entries.ClearArray();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            foreach (GameObject prefab in Spawnable) list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);

            var root = new GameObject("NetworkManager");
            var transport = root.AddComponent<UnityTransport>();
            var manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = playerPrefab,
                ConnectionApproval = true,
                EnableSceneManagement = true,
                TickRate = 30,
            };
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            manager.RunInBackground = true;

            PrefabUtility.SaveAsPrefabAsset(root, ManagerPath);
            Object.DestroyImmediate(root);
            Debug.Log("[SmallTown] NetworkManager built with " + Spawnable.Count + " spawnable prefabs.");
        }

        /// <summary>Gives every network object in the open scene its ID hash. Call after the scene has been saved once.</summary>
        public static int RefreshSceneHashes()
        {
            int count = 0;
            var seen = new HashSet<uint>();
            foreach (NetworkObject networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                RefreshHash(networkObject);
                uint hash = HashOf(networkObject);
                if (hash == 0) Debug.LogError("[SmallTown] Scene network object has no ID hash: " + networkObject.name, networkObject);
                else if (!seen.Add(hash)) Debug.LogError("[SmallTown] Duplicate network ID hash on " + networkObject.name, networkObject);
                count++;
            }
            return count;
        }
    }
}
