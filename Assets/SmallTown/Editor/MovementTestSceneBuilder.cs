using SmallTown.Interaction;
using SmallTown.Locomotion;
using SmallTown.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SmallTown.EditorTools
{
    /// <summary>
    /// Generates the movement gym: one station per locomotion case, laid out in
    /// a ring around the spawn point. Note the scale: the player's eyes are only
    /// about 0.8 m above the ground, so everything here is built low.
    /// </summary>
    public static class MovementTestSceneBuilder
    {
        public const string ScenePath = ContentBuilder.SceneFolder + "/MovementTest.unity";

        static Material ground, wall, accent, deck, climb, sign, dark;

        [MenuItem("Small Town/Setup/Rebuild Movement Test Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ground = ContentBuilder.Mat("GymGround", ContentBuilder.Hex("7EC8A0"));
            wall = ContentBuilder.Mat("GymWall", ContentBuilder.Hex("F2E3C6"));
            accent = ContentBuilder.Mat("GymAccent", ContentBuilder.Hex("F28C6B"));
            deck = ContentBuilder.Mat("GymDeck", ContentBuilder.Hex("6FA8DC"));
            sign = ContentBuilder.Mat("GymSign", ContentBuilder.Hex("33475B"));
            dark = ContentBuilder.Mat("GymDark", ContentBuilder.Hex("2B2B33"));
            climb = ContentBuilder.ClimbableMaterial();

            BuildLighting();
            var world = new GameObject("World");

            // Ground slab with its top face at y = 0, and a low kerb so nobody slides off the edge.
            ContentBuilder.Box("Ground", world.transform, new Vector3(0f, -0.5f, 0f), new Vector3(44f, 1f, 44f), ground);
            Kerb(world.transform, 22f);

            var spawn = new GameObject("SpawnPoint");
            spawn.transform.position = Vector3.zero;
            spawn.AddComponent<PlayerSpawnPoint>();

            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentBuilder.PlayerRigPath);
            var rigObject = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rigObject.transform.position = Vector3.zero;
            var rig = rigObject.GetComponent<PlayerRig>();

            BuildWallStation(world.transform, new Vector3(-7f, 0f, 4f));
            BuildCornerStation(world.transform, new Vector3(-7f, 0f, -4f));
            BuildRampStation(world.transform, new Vector3(0f, 0f, 9f));
            BuildStepStation(world.transform, new Vector3(7f, 0f, 6f));
            BuildCeilingStation(world.transform, new Vector3(8f, 0f, -1f));
            BuildPlatformStation(world.transform, new Vector3(5f, 0f, -8f));
            BuildClimbStation(world.transform, new Vector3(-4f, 0f, -10f));
            BuildGrabStation(world.transform, new Vector3(2.2f, 0f, 2.4f));
            BuildTutorialBoard(rig, new Vector3(0f, 0f, 3.4f));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Build order (Town first) is set by TownSceneBuilder.
        }

        internal static void BuildLighting()
        {
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = ContentBuilder.Hex("FFF4DC");
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ContentBuilder.Hex("BFE3F7");
            RenderSettings.ambientEquatorColor = ContentBuilder.Hex("E9E2D0");
            RenderSettings.ambientGroundColor = ContentBuilder.Hex("7A8F7A");
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
        }

        static void Kerb(Transform parent, float half)
        {
            const float height = 0.6f;
            ContentBuilder.Box("KerbN", parent, new Vector3(0f, height / 2f, half), new Vector3(half * 2f, height, 0.4f), wall);
            ContentBuilder.Box("KerbS", parent, new Vector3(0f, height / 2f, -half), new Vector3(half * 2f, height, 0.4f), wall);
            ContentBuilder.Box("KerbE", parent, new Vector3(half, height / 2f, 0f), new Vector3(0.4f, height, half * 2f), wall);
            ContentBuilder.Box("KerbW", parent, new Vector3(-half, height / 2f, 0f), new Vector3(0.4f, height, half * 2f), wall);
        }

        static Transform Station(Transform parent, string name, Vector3 position, string caption, Vector3 signOffset, float signYaw)
        {
            var station = new GameObject(name);
            station.transform.SetParent(parent, false);
            station.transform.position = position;
            Sign(station.transform, signOffset, signYaw, name.ToUpperInvariant(), caption);
            return station.transform;
        }

        static void Sign(Transform parent, Vector3 localPosition, float yaw, string title, string caption)
        {
            var root = new GameObject("Sign");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            ContentBuilder.Box("Post", root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.06f, 0.6f, 0.06f), dark);
            ContentBuilder.Box("Board", root.transform, new Vector3(0f, 0.85f, 0f), new Vector3(1.3f, 0.55f, 0.05f), sign);
            // Text faces -Z of the sign so it reads when the sign's back is toward the station.
            var text = ContentBuilder.Label("<b>" + title + "</b>\n<size=70%>" + caption + "</size>", root.transform,
                new Vector3(0f, 0.85f, -0.03f), new Vector2(1.2f, 0.5f), 1.1f, Color.white);
            text.transform.localRotation = Quaternion.identity;
        }

        // ------------------------------------------------------------------ Stations

        static void BuildWallStation(Transform parent, Vector3 position)
        {
            Transform s = Station(parent, "Walls", position, "Push off. Hands and body stop at the surface.", new Vector3(2.4f, 0f, -1.5f), 90f);
            ContentBuilder.Box("LongWall", s, new Vector3(-1f, 1.1f, 0f), new Vector3(0.3f, 2.2f, 6f), wall);
            // Chimney: two faces 0.9 m apart for alternating wall pushes.
            ContentBuilder.Box("ChimneyA", s, new Vector3(0.6f, 1.25f, 2.6f), new Vector3(1.6f, 2.5f, 0.3f), accent);
            ContentBuilder.Box("ChimneyB", s, new Vector3(0.6f, 1.25f, 1.4f), new Vector3(1.6f, 2.5f, 0.3f), accent);
        }

        static void BuildCornerStation(Transform parent, Vector3 position)
        {
            Transform s = Station(parent, "Corners", position, "Slide along inside and outside corners.", new Vector3(2.6f, 0f, 1.6f), 90f);
            ContentBuilder.Box("InnerA", s, new Vector3(-1.5f, 1f, 0f), new Vector3(0.3f, 2f, 4f), wall);
            ContentBuilder.Box("InnerB", s, new Vector3(0f, 1f, -2f), new Vector3(3.3f, 2f, 0.3f), wall);
            ContentBuilder.Box("Pillar", s, new Vector3(0.6f, 1f, 0.4f), new Vector3(0.7f, 2f, 0.7f), accent);
            var wedge = ContentBuilder.Box("AngledWall", s, new Vector3(1.6f, 1f, -1.1f), new Vector3(0.3f, 2f, 2f), accent);
            wedge.transform.localRotation = Quaternion.Euler(0f, 40f, 0f);
        }

        static void Ramp(Transform parent, string name, Vector3 basePosition, float angle, float rise, float width, Material material)
        {
            // A ramp whose low edge starts at basePosition and climbs toward +Z.
            float length = rise / Mathf.Sin(angle * Mathf.Deg2Rad);
            float run = rise / Mathf.Tan(angle * Mathf.Deg2Rad);
            const float thickness = 0.2f;
            var ramp = ContentBuilder.Box(name, parent, Vector3.zero, new Vector3(width, thickness, length), material);
            ramp.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
            Vector3 surfaceCentre = basePosition + new Vector3(0f, rise / 2f, run / 2f);
            ramp.transform.localPosition = surfaceCentre - ramp.transform.localRotation * Vector3.up * (thickness / 2f);
        }

        static void BuildRampStation(Transform parent, Vector3 position)
        {
            Transform s = Station(parent, "Ramps", position, "15 and 30 degrees are walkable. 55 is too steep to stand on.", new Vector3(0f, 0f, -1.4f), 180f);
            const float rise = 1f;
            Ramp(s, "Ramp15", new Vector3(-3f, 0f, 0f), 15f, rise, 1.8f, deck);
            Ramp(s, "Ramp30", new Vector3(0f, 0f, 0f), 30f, rise, 1.8f, deck);
            Ramp(s, "Ramp55", new Vector3(3f, 0f, 0f), 55f, rise, 1.8f, accent);
            // Shared top deck behind the longest ramp.
            float deckZ = rise / Mathf.Tan(15f * Mathf.Deg2Rad) + 1f;
            ContentBuilder.Box("TopDeck", s, new Vector3(0f, rise / 2f, deckZ), new Vector3(8f, rise, 2f), wall);
            ContentBuilder.Box("Fill30", s, new Vector3(0f, rise / 2f, (rise / Mathf.Tan(30f * Mathf.Deg2Rad) + deckZ - 1f) / 2f),
                new Vector3(1.8f, rise, deckZ - 1f - rise / Mathf.Tan(30f * Mathf.Deg2Rad)), wall);
            ContentBuilder.Box("Fill55", s, new Vector3(3f, rise / 2f, (rise / Mathf.Tan(55f * Mathf.Deg2Rad) + deckZ - 1f) / 2f),
                new Vector3(1.8f, rise, deckZ - 1f - rise / Mathf.Tan(55f * Mathf.Deg2Rad)), wall);
        }

        static void BuildStepStation(Transform parent, Vector3 position)
        {
            Transform s = Station(parent, "Steps", position, "Small steps are walked over. The tall ledge needs a push.", new Vector3(-1.6f, 0f, -1.2f), 180f);
            const int count = 6;
            const float rise = 0.15f;
            const float tread = 0.4f;
            for (int i = 0; i < count; i++)
            {
                float height = rise * (i + 1);
                ContentBuilder.Box("Step" + i, s, new Vector3(0f, height / 2f, tread * i + tread / 2f), new Vector3(1.8f, height, tread), i % 2 == 0 ? deck : wall);
            }
            float top = rise * count;
            ContentBuilder.Box("Landing", s, new Vector3(0f, top / 2f, tread * count + 1f), new Vector3(1.8f, top, 2f), wall);
            // Tall ledge beside the stairs: too high to step, low enough to mantle with a push.
            ContentBuilder.Box("TallLedge", s, new Vector3(2.2f, 0.275f, 1.4f), new Vector3(1.8f, 0.55f, 2.8f), accent);
        }

        static void BuildCeilingStation(Transform parent, Vector3 position)
        {
            Transform s = Station(parent, "Low ceilings", position, "Duck in real life to fit under the low one.", new Vector3(-2.2f, 0f, 0f), -90f);
            Tunnel(s, "HighTunnel", new Vector3(0f, 0f, 1.6f), 1.15f, deck);
            Tunnel(s, "LowTunnel", new Vector3(0f, 0f, -1.6f), 0.72f, accent);
        }

        static void Tunnel(Transform parent, string name, Vector3 position, float clearance, Material material)
        {
            var t = new GameObject(name);
            t.transform.SetParent(parent, false);
            t.transform.localPosition = position;
            const float length = 3f;
            const float width = 1.2f;
            ContentBuilder.Box("Roof", t.transform, new Vector3(length / 2f, clearance + 0.1f, 0f), new Vector3(length, 0.2f, width + 0.4f), material);
            ContentBuilder.Box("SideA", t.transform, new Vector3(length / 2f, (clearance + 0.2f) / 2f, width / 2f + 0.1f), new Vector3(length, clearance + 0.2f, 0.2f), wall);
            ContentBuilder.Box("SideB", t.transform, new Vector3(length / 2f, (clearance + 0.2f) / 2f, -width / 2f - 0.1f), new Vector3(length, clearance + 0.2f, 0.2f), wall);
        }

        static void BuildPlatformStation(Transform parent, Vector3 position)
        {
            Transform s = Station(parent, "Moving platforms", position, "Ride them. Stepping off keeps the platform's speed, no extra launch.", new Vector3(-2f, 0f, 2.2f), 0f);
            GameObject slider = ContentBuilder.Box("Slider", s, new Vector3(0f, 0.2f, 0f), new Vector3(2f, 0.4f, 2f), deck);
            AddPlatform(slider, new Vector3(5f, 0f, 0f), 4f);

            GameObject lift = ContentBuilder.Box("Lift", s, new Vector3(-3f, 0.1f, -1f), new Vector3(1.8f, 0.2f, 1.8f), accent);
            AddPlatform(lift, new Vector3(0f, 2f, 0f), 3f);
            ContentBuilder.Box("LiftDeck", s, new Vector3(-5.2f, 1f, -1f), new Vector3(2.4f, 2f, 2.4f), wall);
        }

        static void AddPlatform(GameObject go, Vector3 travel, float seconds)
        {
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var platform = go.AddComponent<MovingPlatform>();
            platform.travel = travel;
            platform.secondsPerLeg = seconds;
            platform.pauseSeconds = 1.5f;
        }

        static void BuildClimbStation(Transform parent, Vector3 position)
        {
            Transform s = Station(parent, "Climbing", position, "Yellow stripes can be gripped. Climb to the roof, ramp back down.", new Vector3(1.8f, 0f, 2.2f), 0f);
            ContentBuilder.Box("Building", s, new Vector3(0f, 1.3f, -1.5f), new Vector3(4f, 2.6f, 3f), wall);
            var face = ContentBuilder.Box("ClimbFace", s, new Vector3(0f, 1.3f, 0.06f), new Vector3(1.4f, 2.6f, 0.12f), climb);
            face.AddComponent<LocomotionSurface>().climbable = true;
            for (int i = 0; i < 4; i++)
            {
                var ledge = ContentBuilder.Box("Ledge" + i, s, new Vector3(i % 2 == 0 ? -0.35f : 0.35f, 0.55f + i * 0.55f, 0.22f), new Vector3(0.6f, 0.08f, 0.22f), climb);
                ledge.AddComponent<LocomotionSurface>().climbable = true;
            }
            // Gentle way back down from the roof.
            Ramp(s, "RoofRamp", new Vector3(3.2f, 0f, -8.4f), 24f, 2.6f, 1.6f, deck);
            ContentBuilder.Box("RoofBridge", s, new Vector3(2.6f, 2.5f, -2.5f), new Vector3(2.8f, 0.2f, 1.2f), deck);
        }

        static void BuildGrabStation(Transform parent, Vector3 position)
        {
            Transform s = Station(parent, "Carrying", position, "Grip to grab. Throw. Bag it on your left hip. The trunk needs two paws.", new Vector3(1.6f, 0f, 0.4f), -150f);
            ContentBuilder.Box("TableTop", s, new Vector3(0f, 0.42f, 0f), new Vector3(1.4f, 0.06f, 0.7f), accent);
            ContentBuilder.Box("TableLegA", s, new Vector3(-0.6f, 0.2f, 0f), new Vector3(0.08f, 0.4f, 0.6f), dark);
            ContentBuilder.Box("TableLegB", s, new Vector3(0.6f, 0.2f, 0f), new Vector3(0.08f, 0.4f, 0.6f), dark);

            var crate = AssetDatabase.LoadAssetAtPath<GameObject>(ContentBuilder.CratePath);
            var ball = AssetDatabase.LoadAssetAtPath<GameObject>(ContentBuilder.BallPath);
            var trunk = AssetDatabase.LoadAssetAtPath<GameObject>(ContentBuilder.HeavyCratePath);
            for (int i = 0; i < 3; i++) Place(crate, s, new Vector3(-0.45f + i * 0.22f, 0.54f, 0.1f));
            for (int i = 0; i < 3; i++) Place(ball, s, new Vector3(0.25f + i * 0.17f, 0.53f, -0.1f));
            Place(trunk, s, new Vector3(-0.2f, 0.2f, -1.2f));

            // Throwing target: an open bin.
            Transform bin = new GameObject("Bin").transform;
            bin.SetParent(s, false);
            bin.localPosition = new Vector3(-0.4f, 0f, 2.6f);
            ContentBuilder.Box("Base", bin, new Vector3(0f, 0.03f, 0f), new Vector3(0.8f, 0.06f, 0.8f), dark);
            ContentBuilder.Box("A", bin, new Vector3(0.4f, 0.3f, 0f), new Vector3(0.05f, 0.6f, 0.8f), deck);
            ContentBuilder.Box("B", bin, new Vector3(-0.4f, 0.3f, 0f), new Vector3(0.05f, 0.6f, 0.8f), deck);
            ContentBuilder.Box("C", bin, new Vector3(0f, 0.3f, 0.4f), new Vector3(0.8f, 0.6f, 0.05f), deck);
            ContentBuilder.Box("D", bin, new Vector3(0f, 0.3f, -0.4f), new Vector3(0.8f, 0.6f, 0.05f), deck);
        }

        static void Place(GameObject prefab, Transform parent, Vector3 localPosition)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
        }

        // ------------------------------------------------------------------ Tutorial

        static void BuildTutorialBoard(PlayerRig rig, Vector3 position)
        {
            var root = new GameObject("Tutorial");
            root.transform.position = position;

            ContentBuilder.Box("Board", root.transform, new Vector3(0f, 1.05f, 0.05f), new Vector3(2.4f, 1.1f, 0.08f), sign);
            ContentBuilder.Box("LegA", root.transform, new Vector3(-1f, 0.25f, 0.05f), new Vector3(0.08f, 0.5f, 0.08f), dark);
            ContentBuilder.Box("LegB", root.transform, new Vector3(1f, 0.25f, 0.05f), new Vector3(0.08f, 0.5f, 0.08f), dark);

            TextMeshPro title = ContentBuilder.Label("Title", root.transform, new Vector3(0f, 1.42f, 0f), new Vector2(2.2f, 0.3f), 2.2f, ContentBuilder.Hex("FFD166"));
            TextMeshPro body = ContentBuilder.Label("Body", root.transform, new Vector3(0f, 1.03f, 0f), new Vector2(2.2f, 0.6f), 1.15f, Color.white);
            TextMeshPro progress = ContentBuilder.Label("Progress", root.transform, new Vector3(0f, 0.62f, 0f), new Vector2(2.2f, 0.16f), 0.95f, ContentBuilder.Hex("9FE2BF"));

            HandGrabber left = rig.HandTransform(Hand.Left).GetComponent<HandGrabber>();
            HandGrabber right = rig.HandTransform(Hand.Right).GetComponent<HandGrabber>();
            var bag = rig.GetComponentInChildren<StorageBag>();

            var tutorial = root.AddComponent<TutorialController>();
            tutorial.Configure(rig, left, right, bag, title, body, progress);

            PunchButton skip = Button(root.transform, "SKIP", new Vector3(-0.75f, 0.32f, -0.12f), ContentBuilder.Mat("ButtonRed", ContentBuilder.Hex("E0453A")));
            PunchButton replay = Button(root.transform, "REPLAY", new Vector3(0.75f, 0.32f, -0.12f), ContentBuilder.Mat("ButtonGreen", ContentBuilder.Hex("3FB56F")));
            UnityEventTools.AddPersistentListener(skip.onPressed, tutorial.Skip);
            UnityEventTools.AddPersistentListener(replay.onPressed, tutorial.Restart);

            // The way back to the game is always one slap away.
            // Off to the side on its own post, so nobody strides into it by accident.
            ContentBuilder.Box("TownPost", root.transform, new Vector3(-1.85f, 0.3f, 0f), new Vector3(0.08f, 0.6f, 0.08f), dark);
            PunchButton town = Button(root.transform, "TOWN", new Vector3(-1.85f, 0.62f, -0.06f), ContentBuilder.Mat("ButtonBlue", ContentBuilder.Hex("3F6FB5")));
            ContentBuilder.Label("Slap to go to town", root.transform, new Vector3(-1.85f, 0.86f, -0.06f), new Vector2(0.8f, 0.12f), 0.5f, Color.white);
            var loader = town.gameObject.AddComponent<SceneLoadButton>();
            loader.sceneName = "Town";
            UnityEventTools.AddPersistentListener(town.onPressed, loader.Load);
        }

        public static PunchButton Button(Transform parent, string text, Vector3 localPosition, Material material)
        {
            var root = new GameObject(text + "Button") { layer = GameLayers.Interactable };
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            ContentBuilder.Box("Housing", root.transform, new Vector3(0f, 0f, 0.07f), new Vector3(0.5f, 0.3f, 0.08f), dark, false);
            GameObject cap = ContentBuilder.Box("Cap", root.transform, Vector3.zero, new Vector3(0.42f, 0.22f, 0.08f), material, false);
            // The cap is a scaled cube, so the label lives on the unscaled root to keep its proportions.
            ContentBuilder.Label("<b>" + text + "</b>", root.transform, new Vector3(0f, 0f, -0.045f), new Vector2(0.4f, 0.2f), 0.9f, Color.white);

            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.44f, 0.26f, 0.16f);
            var button = root.AddComponent<PunchButton>();
            button.SetCap(cap.transform);
            return button;
        }
    }
}
