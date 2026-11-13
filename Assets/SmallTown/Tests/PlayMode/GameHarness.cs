using System;
using SmallTown.Crime;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using SmallTown.Jobs;
using SmallTown.Npc;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace SmallTown.Tests
{
    /// <summary>A player rig plus a game session (wallet, wanted level, jobs) built in code, with saving switched off.</summary>
    public sealed class GameHarness : IDisposable
    {
        public readonly RigHarness Player;
        public readonly GameSession Session;
        public readonly GameBalance Balance;
        public readonly GameObject Root;

        public GameHarness(Vector3 feet, Action<GameBalance> tune = null, string saveFolder = null)
        {
            // Saving is off unless a test passes its own temporary folder.
            GameSession.PersistenceEnabled = saveFolder != null;
            GameSession.SaveDirectoryOverride = saveFolder;
            Player = new RigHarness(feet);
            Balance = ScriptableObject.CreateInstance<GameBalance>();
            tune?.Invoke(Balance);

            Root = new GameObject("Game");
            Root.SetActive(false);
            Session = Root.AddComponent<GameSession>();
            Session.Configure(Balance, Player.Rig, Player.Bag);
            Root.AddComponent<JobManager>();
            Root.SetActive(true);
        }

        public Wallet Wallet => Session.Wallet;

        public BoxZone Zone(Vector3 centre, Vector3 size, string name = "Zone")
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.transform.position = centre;
            var zone = go.AddComponent<BoxZone>();
            zone.size = size;
            return zone;
        }

        public Transform Point(Vector3 position, string name = "Point")
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.transform.position = position;
            return go.transform;
        }

        /// <summary>A kinematic grabbable cube, so it stays exactly where a test puts it.</summary>
        public GrabbableItem Item(Vector3 position, bool storable = true)
        {
            GrabbableItem item = Player.AddItem(position, 0.1f, 0.5f, false, storable);
            item.Body.isKinematic = true;
            return item;
        }

        public LootItem Loot(Vector3 position, int value, bool cash = false, bool stolen = true, string name = "Loot")
        {
            GrabbableItem item = Item(position);
            var loot = item.gameObject.AddComponent<LootItem>();
            loot.displayName = name;
            loot.value = value;
            loot.isCash = cash;
            loot.stolen = stolen;
            return loot;
        }

        public NpcBody Npc(Vector3 position, float yaw = 0f)
        {
            var go = new GameObject("Npc");
            go.transform.SetParent(Root.transform, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go.AddComponent<NpcBody>();
        }

        /// <summary>A grabbable pistol built in code, lying at the given position and pointing along +Z.</summary>
        public Pistol MakePistol(Vector3 position)
        {
            GrabbableItem item = Player.AddItem(position, 0.08f, 0.5f);
            item.Body.isKinematic = true;
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(item.transform, false);
            muzzle.localPosition = new Vector3(0f, 0f, 1.2f);
            var pistol = item.gameObject.AddComponent<Pistol>();
            pistol.Configure(muzzle, null, null, null);
            return pistol;
        }

        /// <summary>Bakes a NavMesh over everything the rig harness has built so far.</summary>
        public void BakeNavMesh()
        {
            var surface = Root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << GameLayers.World;
            Physics.SyncTransforms();
            surface.BuildNavMesh();
        }

        /// <summary>An inactive officer to use as the dispatcher's prefab.</summary>
        public PoliceOfficer OfficerTemplate()
        {
            var go = new GameObject("OfficerTemplate");
            go.SetActive(false);
            go.transform.SetParent(Root.transform, false);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.22f;
            agent.height = 1f;
            agent.angularSpeed = 720f;
            agent.acceleration = 40f;
            agent.stoppingDistance = 0.3f;
            var body = go.AddComponent<NpcBody>();
            var officer = go.AddComponent<PoliceOfficer>();
            officer.Configure(body);
            return officer;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(Root);
            foreach (LootItem loot in Object.FindObjectsByType<LootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(loot.gameObject);
            foreach (Pizza pizza in Object.FindObjectsByType<Pizza>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(pizza.gameObject);
            foreach (Topping topping in Object.FindObjectsByType<Topping>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(topping.gameObject);
            Player.Dispose();
            Object.DestroyImmediate(Balance);
            GameSession.PersistenceEnabled = false;
            GameSession.SaveDirectoryOverride = null;
            NavMesh.RemoveAllNavMeshData();
        }
    }
}
