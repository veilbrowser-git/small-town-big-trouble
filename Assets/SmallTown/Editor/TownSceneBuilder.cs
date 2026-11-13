using System;
using System.Collections.Generic;
using System.IO;
using SmallTown.Crime;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using SmallTown.Jobs;
using SmallTown.Locomotion;
using SmallTown.Networking;
using SmallTown.Npc;
using SmallTown.UI;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace SmallTown.EditorTools
{
    /// <summary>
    /// Assembles the Town scene from the Blender export. Town.fbx supplies what
    /// you see; Town.layout.json supplies colliders, climbable surfaces, zones,
    /// sign text and named markers. Every gameplay station is placed at a
    /// marker, so moving something in Blender moves it in the game.
    /// </summary>
    public static class TownSceneBuilder
    {
        public const string ScenePath = ContentBuilder.SceneFolder + "/Town.unity";
        const string ModelPath = CharacterBuilder.ModelFolder + "/Town.fbx";
        const string LayoutPath = CharacterBuilder.ModelFolder + "/Town.layout.json";
        const string NavMeshPath = ContentBuilder.SceneFolder + "/Town_NavMesh.asset";

        [Serializable] class Box { public string n; public float[] c; public float[] s; public float[] r; }
        [Serializable] class Mark { public string n; public float[] p; public float yaw; }
        [Serializable] class Sign { public string t; public float[] p; public float yaw; public float w; public float h; public string col; public float font; public float d; }
        [Serializable] class Layout { public int version; public Box[] colliders; public Box[] climbables; public Box[] zones; public Mark[] markers; public Sign[] signs; }

        static readonly Dictionary<string, Transform> Markers = new Dictionary<string, Transform>();
        // Things clients must see exactly as the host has them (see NetSession).
        static readonly List<TMP_Text> SharedLabels = new List<TMP_Text>();
        static readonly List<GameObject> SharedToggles = new List<GameObject>();
        static readonly List<PunchButton> SharedButtons = new List<PunchButton>();
        static readonly Dictionary<string, Box> Zones = new Dictionary<string, Box>();

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        static Transform M(string name)
        {
            if (!Markers.TryGetValue(name, out Transform marker)) throw new InvalidOperationException("Town layout has no marker '" + name + "'.");
            return marker;
        }

        static Transform[] MarkerSeries(string prefix)
        {
            var list = new List<Transform>();
            for (int i = 0; Markers.TryGetValue(prefix + i, out Transform marker); i++) list.Add(marker);
            return list.ToArray();
        }

        [MenuItem("Small Town/Setup/Rebuild Town Scene")]
        public static void Build()
        {
            if (!File.Exists(ModelPath) || !File.Exists(LayoutPath))
            {
                Debug.LogError("[SmallTown] Town model or layout missing. Run Tools/blender/build_town.py first.");
                return;
            }
            Markers.Clear();
            Zones.Clear();
            SharedLabels.Clear();
            SharedToggles.Clear();
            SharedButtons.Clear();
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            GameBalance balance = ItemBuilder.EnsureBalance();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MovementTestSceneBuilder.BuildLighting();

            Dictionary<string, Mesh> props = BuildVisuals();
            GameObject collision = BuildCollision(layout);
            BuildClimbables(layout);
            BuildSigns(layout);
            BuildMarkers(layout);
            foreach (Box zone in layout.zones) Zones[zone.n] = zone;
            BakeNavMesh(collision);

            // --- Player.
            Transform spawn = M("PlayerSpawn");
            spawn.gameObject.AddComponent<PlayerSpawnPoint>();
            var rigObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ContentBuilder.PlayerRigPath));
            rigObject.transform.position = spawn.position;
            var rig = rigObject.GetComponent<PlayerRig>();
            var bag = rigObject.GetComponentInChildren<StorageBag>();

            // --- Session services. The town always runs inside a session, even solo.
            new GameObject("NetBootstrap").AddComponent<TownNetBootstrap>();
            var game = new GameObject("Game");
            var session = game.AddComponent<GameSession>();
            session.Configure(balance, rig, bag);
            game.AddComponent<JobManager>();
            game.AddComponent<LootBanker>();
            var arrest = game.AddComponent<ArrestController>();
            arrest.Configure(M("PoliceRelease"));

            // --- Prefabs.
            LootItem cash = ItemBuilder.BuildCash();
            LootItem[] merch = ItemBuilder.BuildMerchandise(balance);
            Pizza pizza = ItemBuilder.BuildPizza();
            Pistol pistol = ItemBuilder.BuildPistol();
            Topping[] toppings =
            {
                ItemBuilder.BuildTopping("cheese", "Cheese", ContentBuilder.Hex("FFD86B")),
                ItemBuilder.BuildTopping("pepperoni", "Pepperoni", ContentBuilder.Hex("B8322A")),
                ItemBuilder.BuildTopping("mushroom", "Mushroom", ContentBuilder.Hex("CDB89A")),
            };
            GameObject officer = CharacterBuilder.BuildOfficerPrefab();
            GameObject cashierPrefab = CharacterBuilder.BuildStaticNpcPrefab("Cashier", "Cashier", "Visor");
            GameObject chefPrefab = CharacterBuilder.BuildStaticNpcPrefab("Chef", "Chef", null);
            GameObject brokerPrefab = CharacterBuilder.BuildStaticNpcPrefab("Broker", "Broker", null);
            string[] citizens = { "CitizenA", "CitizenB", "CitizenC" };
            var customerPrefabs = new GameObject[citizens.Length];
            var pedestrianPrefabs = new GameObject[citizens.Length];
            for (int i = 0; i < citizens.Length; i++)
            {
                customerPrefabs[i] = CharacterBuilder.BuildStaticNpcPrefab("Customer_" + citizens[i], citizens[i], null);
                pedestrianPrefabs[i] = CharacterBuilder.BuildPedestrianPrefab(citizens[i]);
            }

            BuildPizzaPlace(pizza, toppings, chefPrefab, customerPrefabs);
            BuildStore(cash, merch, cashierPrefab);
            BuildPawnShop(game, pistol, brokerPrefab);
            BuildPolice(game, officer, arrest, props);
            BuildPedestrians(pedestrianPrefabs);
            BuildApartment();
            BuildWater();

            // --- Networking: the shared noticeboard, the player others see, and the manager.
            NetBuilder.MakeShared(game, false, false, false);
            game.AddComponent<NetSession>().Configure(SharedLabels.ToArray(), SharedToggles.ToArray(), SharedButtons.ToArray(), arrest);
            GameObject networkPlayer = NetBuilder.BuildNetworkPlayer();
            NetBuilder.BuildNetworkManager(networkPlayer);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            // Scene objects only get their network IDs once the scene exists on disk.
            int shared = NetBuilder.RefreshSceneHashes();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[SmallTown] Town has " + shared + " shared scene objects, " + SharedLabels.Count + " shared labels, " +
                      SharedToggles.Count + " shared switches, " + SharedButtons.Count + " shared buttons.");

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuSceneBuilder.ScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(MovementTestSceneBuilder.ScenePath, true),
            };
            Debug.Log("[SmallTown] Town scene built: " + layout.colliders.Length + " colliders, " + layout.markers.Length + " markers.");
        }

        // ------------------------------------------------------------------ Static world

        static Dictionary<string, Mesh> BuildVisuals()
        {
            var props = new Dictionary<string, Mesh>();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "TownVisuals";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Material palette = CharacterBuilder.Palette();

            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                GameObject go = filter.gameObject;
                if (go.name.StartsWith("Prop_"))
                {
                    props[go.name] = CharacterBuilder.BakeToAsset(filter, go.name);
                    UnityEngine.Object.DestroyImmediate(go);
                    continue;
                }
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = palette;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }

            // The model must land exactly where the layout file says. Check one chunk that
            // is off-centre on both axes, which would expose a mirrored or rotated import.
            Transform pizza = instance.transform.Find("Building_Pizza");
            if (pizza != null)
            {
                Bounds b = pizza.GetComponent<MeshRenderer>().bounds;
                bool ok = Mathf.Abs(b.center.x - -14.75f) < 0.6f && Mathf.Abs(b.center.z - 9f) < 1.2f && b.min.y > -0.1f;
                if (ok) Debug.Log("[SmallTown] Town model alignment OK. Pizza building centre " + b.center.ToString("F2"));
                else Debug.LogError("[SmallTown] Town model is misaligned with its layout. Pizza building centre " + b.center.ToString("F2") + ", expected about (-14.75, y, 9).");
            }
            return props;
        }

        static GameObject BuildCollision(Layout layout)
        {
            var root = new GameObject("TownCollision") { isStatic = true };
            var groups = new Dictionary<string, GameObject>();
            foreach (Box box in layout.colliders)
            {
                if (!groups.TryGetValue(box.n, out GameObject group))
                {
                    group = new GameObject("COL_" + box.n) { isStatic = true, layer = GameLayers.World };
                    group.transform.SetParent(root.transform, false);
                    groups[box.n] = group;
                }
                Vector3 rotation = V(box.r);
                if (rotation.sqrMagnitude < 1e-6f)
                {
                    var collider = group.AddComponent<BoxCollider>();
                    collider.center = V(box.c);
                    collider.size = V(box.s);
                }
                else
                {
                    var tilted = new GameObject("Tilted") { isStatic = true, layer = GameLayers.World };
                    tilted.transform.SetParent(group.transform, false);
                    tilted.transform.SetPositionAndRotation(V(box.c), Quaternion.Euler(rotation));
                    tilted.AddComponent<BoxCollider>().size = V(box.s);
                }
            }
            return root;
        }

        static void BuildClimbables(Layout layout)
        {
            var root = new GameObject("Climbables");
            Material stripes = ContentBuilder.ClimbableMaterial();
            foreach (Box box in layout.climbables)
            {
                GameObject go = ContentBuilder.Box("Climbable", root.transform, V(box.c), V(box.s), stripes);
                go.transform.rotation = Quaternion.Euler(V(box.r));
                go.isStatic = true;
                go.AddComponent<LocomotionSurface>().climbable = true;
            }
        }

        static void BuildSigns(Layout layout)
        {
            var root = new GameObject("SignText");
            foreach (Sign sign in layout.signs)
            {
                ColorUtility.TryParseHtmlString("#" + sign.col, out Color colour);
                Quaternion rotation = Quaternion.Euler(0f, sign.yaw, 0f);
                TextMeshPro label = ContentBuilder.Label("<b>" + sign.t + "</b>", root.transform,
                    V(sign.p) + rotation * new Vector3(0f, 0f, -(sign.d * 0.5f + 0.006f)),
                    new Vector2(sign.w * 0.94f, sign.h * 0.86f), sign.font, colour);
                label.transform.rotation = rotation;
                label.enableAutoSizing = true;
                label.fontSizeMin = 0.3f;
                label.fontSizeMax = sign.h * 9f;
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }

        static void BuildMarkers(Layout layout)
        {
            var root = new GameObject("Markers");
            foreach (Mark mark in layout.markers)
            {
                var go = new GameObject("MARK_" + mark.n);
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(V(mark.p), Quaternion.Euler(0f, mark.yaw, 0f));
                Markers[mark.n] = go.transform;
            }
        }

        static void BakeNavMesh(GameObject collision)
        {
            // NPCs are the same small size as the player, so shrink the default agent.
            var settingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (settingsAsset.Length > 0)
            {
                var serialized = new SerializedObject(settingsAsset[0]);
                SerializedProperty agent = serialized.FindProperty("m_Settings");
                if (agent != null && agent.arraySize > 0)
                {
                    SerializedProperty first = agent.GetArrayElementAtIndex(0);
                    first.FindPropertyRelative("agentRadius").floatValue = 0.22f;
                    first.FindPropertyRelative("agentHeight").floatValue = 1.0f;
                    first.FindPropertyRelative("agentClimb").floatValue = 0.25f;
                    first.FindPropertyRelative("agentSlope").floatValue = 40f;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            var surface = collision.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << GameLayers.World;
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                Debug.LogError("[SmallTown] NavMesh bake produced no data.");
                return;
            }
            AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            Debug.Log("[SmallTown] NavMesh baked: " + triangulation.indices.Length / 3 + " triangles.");
        }

        // ------------------------------------------------------------------ Small builders

        /// <summary>A pivot whose -Z (the readable/pressable side) points the way the marker faces.</summary>
        static Transform FacingPivot(string name, Vector3 position, float facingYaw, Transform parent = null)
        {
            var pivot = new GameObject(name);
            if (parent != null) pivot.transform.SetParent(parent, false);
            pivot.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, facingYaw + 180f, 0f));
            return pivot.transform;
        }

        static TextMeshPro Text(Transform pivot, string text, Vector3 local, Vector2 size, float font, Color colour)
        {
            return ContentBuilder.Label(text, pivot, local, size, font, colour);
        }

        static GameObject Npc(GameObject prefab, Transform marker, string name)
        {
            var npc = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            npc.name = name;
            npc.transform.SetPositionAndRotation(marker.position, marker.rotation);
            npc.GetComponent<NpcBody>().Pose(0f);
            return npc;
        }

        static BoxZone Zone(string name, Vector3 centre, Vector3 size, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = centre;
            var zone = go.AddComponent<BoxZone>();
            zone.size = size;
            return zone;
        }

        static Material ButtonMaterial(string name, string hex) => ContentBuilder.Mat(name, ContentBuilder.Hex(hex));

        // ------------------------------------------------------------------ Pizza restaurant

        static void BuildPizzaPlace(Pizza pizzaPrefab, Topping[] toppings, GameObject chefPrefab, GameObject[] customerPrefabs)
        {
            var root = new GameObject("PizzaPlace");
            Npc(chefPrefab, M("PizzaChef"), "Chef");

            string[] bins = { "Bin_Cheese", "Bin_Pepperoni", "Bin_Mushroom" };
            for (int i = 0; i < bins.Length; i++)
            {
                var bin = new GameObject(bins[i]);
                bin.transform.SetParent(root.transform, false);
                bin.transform.position = M(bins[i]).position + Vector3.up * 0.04f;
                bin.AddComponent<IngredientBin>().Configure(toppings[i]);
                Vector3 binPosition = M(bins[i]).position;
                Transform tag = FacingPivot("Tag", new Vector3(binPosition.x, 0.45f, binPosition.z - 0.57f), 180f, bin.transform);
                Text(tag, toppings[i].displayName, Vector3.zero, new Vector2(0.45f, 0.1f), 0.45f, Color.white);
            }

            BoxZone oven = Zone("OvenZone", M("PizzaOven").position + Vector3.up * 0.17f, new Vector3(0.62f, 0.4f, 0.5f), root.transform);
            BoxZone boxing = Zone("BoxingZone", M("PizzaBoxing").position + Vector3.up * 0.15f, new Vector3(0.6f, 0.36f, 0.55f), root.transform);

            string[] places = { "the pink house", "the yellow house", "the teal house", "the bait hut on the dock" };
            Transform[] customerMarkers = MarkerSeries("Customer_");
            var customers = new DeliveryCustomer[customerMarkers.Length];
            Material beaconMaterial = ContentBuilder.UnlitMaterial("Beacon", ContentBuilder.Hex("FFD166"), false);
            for (int i = 0; i < customerMarkers.Length; i++)
            {
                GameObject npc = Npc(customerPrefabs[i % customerPrefabs.Length], customerMarkers[i], "Customer " + i);
                BoxZone zone = Zone("DropZone", customerMarkers[i].position + Vector3.up * 0.7f, new Vector3(2f, 1.8f, 2f), npc.transform);
                GameObject beacon = ContentBuilder.Box("Beacon", npc.transform, new Vector3(0f, 2.3f, 0f), new Vector3(0.14f, 1.6f, 0.14f), beaconMaterial, false);
                ContentBuilder.Box("BeaconTip", beacon.transform, new Vector3(0f, -0.62f, 0f), new Vector3(3f, 0.16f, 3f), beaconMaterial, false);
                var customer = npc.AddComponent<DeliveryCustomer>();
                customer.placeName = places[i % places.Length];
                customer.Configure(zone, beacon, npc.GetComponent<NpcBody>());
                customers[i] = customer;
                SharedToggles.Add(beacon);
            }

            // Order terminal on the wall.
            Transform terminalMarker = M("PizzaTerminal");
            Transform terminal = FacingPivot("OrderTerminal", terminalMarker.position, terminalMarker.eulerAngles.y, root.transform);
            ContentBuilder.Box("Board", terminal, new Vector3(0f, 0.28f, 0.02f), new Vector3(1.3f, 0.62f, 0.04f), ContentBuilder.Mat("GymSign", ContentBuilder.Hex("33475B")), false);
            TextMeshPro order = Text(terminal, "ORDERS", new Vector3(0f, 0.28f, -0.01f), new Vector2(1.2f, 0.54f), 0.8f, Color.white);

            var job = root.AddComponent<PizzaDeliveryJob>();
            job.jobId = "job.pizza";
            job.displayName = "Pizza delivery";
            job.Configure(pizzaPrefab, M("PizzaBase"), oven, boxing, customers, order);

            PunchButton take = MovementTestSceneBuilder.Button(terminal, "ORDER", new Vector3(-0.33f, -0.24f, -0.06f), ButtonMaterial("ButtonGreen", "3FB56F"));
            PunchButton cancel = MovementTestSceneBuilder.Button(terminal, "CANCEL", new Vector3(0.33f, -0.24f, -0.06f), ButtonMaterial("ButtonRed", "E0453A"));
            UnityEventTools.AddPersistentListener(take.onPressed, job.TakeOrder);
            UnityEventTools.AddPersistentListener(cancel.onPressed, job.CancelOrder);
            SharedLabels.Add(order);
            SharedButtons.Add(take);
            SharedButtons.Add(cancel);
        }

        // ------------------------------------------------------------------ Convenience store

        static void BuildStore(LootItem cash, LootItem[] merch, GameObject cashierPrefab)
        {
            Box zoneBox = Zones["Store"];
            var root = new GameObject("QuickPawMart");
            root.transform.position = V(zoneBox.c);
            BoxZone zone = Zone("StoreZone", V(zoneBox.c), V(zoneBox.s), root.transform);

            GameObject cashier = Npc(cashierPrefab, M("Cashier"), "Cashier");

            Transform statusMarker = M("StoreStatusSign");
            Transform statusPivot = FacingPivot("StatusSign", statusMarker.position, statusMarker.eulerAngles.y, root.transform);
            ContentBuilder.Box("Board", statusPivot, new Vector3(0f, 0f, 0.015f), new Vector3(1.1f, 0.42f, 0.03f), ContentBuilder.Mat("GymDark", ContentBuilder.Hex("2B2B33")), false);
            TextMeshPro status = Text(statusPivot, "REGISTER", new Vector3(0f, 0f, -0.006f), new Vector2(1.0f, 0.36f), 0.8f, ContentBuilder.Hex("9FE2BF"));

            var site = root.AddComponent<RobberySite>();
            site.Configure(zone, cashier.GetComponent<NpcBody>(), M("RegisterCash"), M("StoreAlarm"), cash, MarkerSeries("Merch_"), merch, status);
            SharedLabels.Add(status);
        }

        // ------------------------------------------------------------------ Pawn shop

        static void BuildPawnShop(GameObject game, Pistol pistolPrefab, GameObject brokerPrefab)
        {
            var root = new GameObject("PawnShop");
            Npc(brokerPrefab, M("PawnBroker"), "Broker");

            BoxZone tray = Zone("SellTray", M("PawnSell").position + Vector3.up * 0.2f, new Vector3(0.95f, 0.5f, 0.55f), root.transform);
            Transform signMarker = M("PawnPayoutSign");
            Transform signPivot = FacingPivot("PayoutSign", signMarker.position, signMarker.eulerAngles.y, root.transform);
            ContentBuilder.Box("Board", signPivot, new Vector3(0f, 0f, 0.015f), new Vector3(1.3f, 0.5f, 0.03f), ContentBuilder.Mat("GymDark", ContentBuilder.Hex("2B2B33")), false);
            TextMeshPro payout = Text(signPivot, "WE BUY ANYTHING", new Vector3(0f, 0f, -0.006f), new Vector2(1.2f, 0.44f), 0.75f, ContentBuilder.Hex("FFD166"));
            root.AddComponent<PawnShop>().Configure(tray, payout);
            SharedLabels.Add(payout);

            var equipment = game.AddComponent<OwnedEquipment>();
            equipment.Configure(pistolPrefab, M("EquipmentTable"), M("ShopPistol"));

            ShopOffer pistolOffer = Offer(root.transform, "ShopPistol", GameBalance.PistolUnlock, "Pistol", "ButtonBlue", "3F6FB5");
            UnityEventTools.AddPersistentListener(pistolOffer.purchased, equipment.OnPistolPurchased);
            Offer(root.transform, "ShopSatchel", GameBalance.SatchelUnlock, "Big Satchel", "ButtonGreen", "3FB56F");
        }

        static ShopOffer Offer(Transform parent, string markerName, string unlockId, string displayName, string materialName, string hex)
        {
            Transform marker = M(markerName);
            // Price card standing on the counter, BUY button on the counter's front face.
            Transform card = FacingPivot("Offer_" + displayName, marker.position + new Vector3(0f, 0.22f, 0.12f), 180f, parent);
            ContentBuilder.Box("Card", card, new Vector3(0f, 0f, 0.012f), new Vector3(0.62f, 0.3f, 0.024f), ContentBuilder.Mat("GymSign", ContentBuilder.Hex("33475B")), false);
            TextMeshPro label = Text(card, displayName, new Vector3(0f, 0f, -0.004f), new Vector2(0.56f, 0.26f), 0.6f, Color.white);

            var offer = card.gameObject.AddComponent<ShopOffer>();
            offer.unlockId = unlockId;
            offer.displayName = displayName;
            offer.Configure(label);

            Transform buttonPivot = FacingPivot("BuyPivot", new Vector3(marker.position.x, 0.36f, marker.position.z - 0.36f), 180f, card);
            PunchButton buy = MovementTestSceneBuilder.Button(buttonPivot, "BUY", Vector3.zero, ButtonMaterial(materialName, hex));
            UnityEventTools.AddPersistentListener(buy.onPressed, offer.BuyPressed);
            SharedButtons.Add(buy);
            return offer;
        }

        // ------------------------------------------------------------------ Police

        static void BuildPolice(GameObject game, GameObject officerPrefab, ArrestController arrest, Dictionary<string, Mesh> props)
        {
            var root = new GameObject("Police");
            Material palette = CharacterBuilder.Palette();

            // Patrol car: kinematic, on the Vehicle layer so hands and bodies treat it as solid.
            Transform carMarker = M("PatrolCar");
            var car = new GameObject("PatrolCar") { layer = GameLayers.Vehicle };
            car.transform.SetParent(root.transform, false);
            car.transform.SetPositionAndRotation(carMarker.position, carMarker.rotation);
            if (props.TryGetValue("Prop_PatrolCar", out Mesh carMesh))
            {
                var visual = new GameObject("Visual") { layer = GameLayers.Vehicle };
                visual.transform.SetParent(car.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = carMesh;
                visual.AddComponent<MeshRenderer>().sharedMaterial = palette;
            }
            var carBody = car.AddComponent<Rigidbody>();
            carBody.isKinematic = true;
            var carCollider = car.AddComponent<BoxCollider>();
            carCollider.center = new Vector3(0f, 0.43f, 0f);
            carCollider.size = new Vector3(0.98f, 0.86f, 1.92f);
            var lights = new Renderer[2];
            lights[0] = ContentBuilder.Box("LightRed", car.transform, new Vector3(-0.18f, 0.9f, -0.1f), new Vector3(0.32f, 0.14f, 0.2f),
                ContentBuilder.UnlitMaterial("SirenRed", ContentBuilder.Hex("FF4D4D"), false), false, GameLayers.Vehicle).GetComponent<Renderer>();
            lights[1] = ContentBuilder.Box("LightBlue", car.transform, new Vector3(0.18f, 0.9f, -0.1f), new Vector3(0.32f, 0.14f, 0.2f),
                ContentBuilder.UnlitMaterial("SirenBlue", ContentBuilder.Hex("4D7BFF"), false), false, GameLayers.Vehicle).GetComponent<Renderer>();
            var patrolCar = car.AddComponent<PatrolCar>();
            patrolCar.Configure(lights);
            NetBuilder.MakeShared(car, true, false, false);
            SharedToggles.Add(lights[0].gameObject);
            SharedToggles.Add(lights[1].gameObject);

            // Roadblocks: hidden until wanted level 3.
            var blocks = new List<GameObject>();
            foreach (string name in new[] { "Roadblock_W", "Roadblock_E" })
            {
                Transform marker = M(name);
                var block = new GameObject(name) { layer = GameLayers.World };
                block.transform.SetParent(root.transform, false);
                block.transform.SetPositionAndRotation(marker.position, marker.rotation);
                if (props.TryGetValue("Prop_Roadblock", out Mesh blockMesh))
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var piece = new GameObject("Barrier" + i);
                        piece.transform.SetParent(block.transform, false);
                        piece.transform.localPosition = new Vector3(i == 0 ? -1.25f : 1.25f, 0f, 0f);
                        piece.AddComponent<MeshFilter>().sharedMesh = blockMesh;
                        piece.AddComponent<MeshRenderer>().sharedMaterial = palette;
                    }
                }
                var collider = block.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.4f, 0f);
                collider.size = new Vector3(5f, 0.8f, 0.4f);
                blocks.Add(block);
                SharedToggles.Add(block);
            }

            var dispatcher = game.AddComponent<PoliceDispatcher>();
            dispatcher.Configure(officerPrefab.GetComponent<PoliceOfficer>(), MarkerSeries("PoliceSpawn_"), MarkerSeries("Patrol_"),
                patrolCar, blocks.ToArray(), arrest);
        }

        static void BuildPedestrians(GameObject[] prefabs)
        {
            var root = new GameObject("Pedestrians");
            Transform[] points = MarkerSeries("Wander_");
            int[] starts = { 0, 4, 8 };
            for (int i = 0; i < prefabs.Length; i++)
            {
                Transform start = points[starts[i % starts.Length] % points.Length];
                var npc = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i]);
                npc.transform.SetParent(root.transform, false);
                npc.transform.SetPositionAndRotation(start.position, Quaternion.Euler(0f, i * 120f, 0f));
                npc.GetComponent<NpcBody>().Pose(0f);
                npc.GetComponent<Pedestrian>().Configure(npc.GetComponent<NpcBody>(), points);
            }
        }

        // ------------------------------------------------------------------ Apartment and sea

        static void BuildApartment()
        {
            var root = new GameObject("Apartment");
            Transform boardMarker = M("ApartmentBoard");
            Transform board = FacingPivot("HomeBoard", boardMarker.position, boardMarker.eulerAngles.y, root.transform);
            ContentBuilder.Box("Board", board, new Vector3(0f, 0f, 0.02f), new Vector3(2.0f, 0.9f, 0.04f), ContentBuilder.Mat("GymSign", ContentBuilder.Hex("33475B")), false);
            Text(board, "<b>HOME, SWEET HOME</b>", new Vector3(0f, 0.32f, -0.006f), new Vector2(1.9f, 0.2f), 1.3f, ContentBuilder.Hex("FFD166"));
            Text(board,
                "You have $20 and two good paws.\n" +
                "<b>Work:</b> Slice of Trouble, across the street, needs a delivery panda.\n" +
                "<b>Trouble:</b> the pawn shop sells a pistol. QuickPaw Mart has a full register.\n" +
                "<b>Getting away:</b> stay out of sight until the heat dies down. Stolen cash in your satchel becomes yours once you escape.\n" +
                "Hold A + X if you get stuck.",
                new Vector3(0f, -0.1f, -0.006f), new Vector2(1.85f, 0.6f), 0.62f, Color.white);

            Transform gymMarker = M("GymButton");
            Transform gymPivot = FacingPivot("GymPivot", gymMarker.position, gymMarker.eulerAngles.y, root.transform);
            PunchButton gym = MovementTestSceneBuilder.Button(gymPivot, "GYM", Vector3.zero, ButtonMaterial("ButtonBlue", "3F6FB5"));
            var loader = gym.gameObject.AddComponent<SceneLoadButton>();
            loader.sceneName = "MovementTest";
            loader.soloOnly = true;
            UnityEventTools.AddPersistentListener(gym.onPressed, loader.Load);
            Text(gymPivot, "Movement lessons", new Vector3(0f, 0.24f, -0.01f), new Vector2(0.7f, 0.12f), 0.5f, Color.white);

            PunchButton menu = MovementTestSceneBuilder.Button(gymPivot, "MENU", new Vector3(0.7f, 0f, 0f), ButtonMaterial("ButtonRed", "E0453A"));
            var leave = menu.gameObject.AddComponent<SceneLoadButton>();
            leave.sceneName = "Menu";
            UnityEventTools.AddPersistentListener(menu.onPressed, leave.Load);
            Text(gymPivot, "Leave game", new Vector3(0.7f, 0.24f, -0.01f), new Vector2(0.7f, 0.12f), 0.5f, Color.white);
        }

        static void BuildWater()
        {
            if (!Zones.TryGetValue("Water", out Box water)) return;
            BoxZone zone = Zone("Sea", V(water.c), V(water.s));
            zone.gameObject.AddComponent<WaterZone>().Configure(M("DockRespawn"));
        }
    }
}
