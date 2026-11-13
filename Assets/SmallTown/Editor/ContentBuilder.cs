using System.Collections.Generic;
using System.IO;
using SmallTown.Avatar;
using SmallTown.Interaction;
using SmallTown.Locomotion;
using SmallTown.RigInput;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;

namespace SmallTown.EditorTools
{
    /// <summary>
    /// Builds every generated asset from code: tuning assets, materials, the
    /// player rig prefab, item prefabs and the development scenes. Nothing here
    /// is hand-authored, so the whole project can be restored with one command.
    /// </summary>
    public static class ContentBuilder
    {
        public const string ArtFolder = ProjectSetup.Root + "/Art";
        public const string MaterialFolder = ArtFolder + "/Materials";
        public const string PrefabFolder = ProjectSetup.Root + "/Prefabs";
        public const string SceneFolder = ProjectSetup.Root + "/Scenes";
        public const string LocomotionSettingsPath = ProjectSetup.SettingsFolder + "/HandLocomotionSettings.asset";
        public const string PlayerRigPath = PrefabFolder + "/PlayerRig.prefab";
        public const string CratePath = PrefabFolder + "/Items/Crate.prefab";
        public const string BallPath = PrefabFolder + "/Items/Ball.prefab";
        public const string HeavyCratePath = PrefabFolder + "/Items/HeavyTrunk.prefab";

        // Palette taken from the red panda character sheet.
        public static readonly Color Cream = Hex("F6DFC0");
        public static readonly Color Tan = Hex("C9A27A");
        public static readonly Color Orange = Hex("D2562B");
        public static readonly Color Maroon = Hex("6E2F2A");
        public static readonly Color DarkBrown = Hex("4A2420");

        static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

        [MenuItem("Small Town/Setup/2. Build Prefabs and Scenes")]
        public static void BuildAll()
        {
            MaterialCache.Clear();
            ProjectSetup.EnsureFolder(MaterialFolder);
            ProjectSetup.EnsureFolder(PrefabFolder + "/Items");
            ProjectSetup.EnsureFolder(SceneFolder);

            HandLocomotionSettings settings = EnsureLocomotionSettings();
            CharacterBuilder.Reset();
            AssetDatabase.Refresh();
            CharacterBuilder.BakeParts();
            BuildItemPrefabs();
            BuildPlayerRig(settings);
            MovementTestSceneBuilder.Build();
            TownSceneBuilder.Build();
            AssetDatabase.SaveAssets();
            Debug.Log("[SmallTown] Prefabs and scenes built.");
        }

        // ------------------------------------------------------------------ Shared helpers

        public static Color Hex(string rgb)
        {
            ColorUtility.TryParseHtmlString("#" + rgb, out Color color);
            return color;
        }

        public static HandLocomotionSettings EnsureLocomotionSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<HandLocomotionSettings>(LocomotionSettingsPath);
            if (settings == null)
            {
                ProjectSetup.EnsureFolder(ProjectSetup.SettingsFolder);
                settings = ScriptableObject.CreateInstance<HandLocomotionSettings>();
                AssetDatabase.CreateAsset(settings, LocomotionSettingsPath);
            }
            settings.surfaceMask = GameLayers.LocomotionSurfaces;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        public static Material Mat(string name, Color color)
        {
            if (MaterialCache.TryGetValue(name, out Material cached) && cached != null) return cached;
            string path = MaterialFolder + "/" + name + ".mat";
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                ProjectSetup.EnsureFolder(MaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", Color.black);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            MaterialCache[name] = material;
            return material;
        }

        /// <summary>An unlit material, optionally alpha blended. Used for effects and the screen fade.</summary>
        public static Material UnlitMaterial(string name, Color color, bool transparent)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                ProjectSetup.EnsureFolder(MaterialFolder);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            material.SetColor("_BaseColor", color);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 100;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Yellow and black hazard stripes: the game-wide cue for "this surface can be gripped and climbed".</summary>
        public static Material ClimbableMaterial()
        {
            const string texturePath = ArtFolder + "/Textures/ClimbStripes.png";
            if (!File.Exists(texturePath))
            {
                ProjectSetup.EnsureFolder(ArtFolder + "/Textures");
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color yellow = Hex("F4C430");
                Color dark = Hex("2B2B33");
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        texture.SetPixel(x, y, ((x + y) / 16) % 2 == 0 ? yellow : dark);
                    }
                }
                texture.Apply();
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath);
            }
            Material material = Mat("Climbable", Color.white);
            var stripes = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.SetTexture("_BaseMap", stripes);
            material.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            EditorUtility.SetDirty(material);
            return material;
        }

        public static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material,
            bool collider = true, int layer = GameLayers.World)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            box.layer = layer;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(box.GetComponent<Collider>());
            return box;
        }

        public static TextMeshPro Label(string text, Transform parent, Vector3 localPosition, Vector2 size, float fontSize,
            Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var label = go.AddComponent<TextMeshPro>();
            label.rectTransform.sizeDelta = size;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
            return label;
        }

        static GameObject SavePrefab(GameObject root, string path)
        {
            ProjectSetup.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ Items

        static void BuildItemPrefabs()
        {
            // Light crate.
            var crate = Box("Crate", null, Vector3.zero, Vector3.one * 0.16f, Mat("CrateWood", Hex("D9A441")), true, GameLayers.Item);
            Box("Band", crate.transform, Vector3.zero, new Vector3(1.04f, 0.2f, 1.04f), Mat("CrateBand", Hex("8A5A2B")), false, GameLayers.Item);
            AddItem(crate, "crate", "Crate", 1.2f, false, true);
            SavePrefab(crate, CratePath);

            // Ball.
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.layer = GameLayers.Item;
            ball.transform.localScale = Vector3.one * 0.14f;
            ball.GetComponent<MeshRenderer>().sharedMaterial = Mat("BallRed", Hex("E0453A"));
            AddItem(ball, "ball", "Ball", 0.4f, false, true);
            SavePrefab(ball, BallPath);

            // Heavy trunk: one hand drags it, two hands lift it.
            var trunk = Box("HeavyTrunk", null, Vector3.zero, new Vector3(0.8f, 0.36f, 0.42f), Mat("TrunkBlue", Hex("3F6FB5")), true, GameLayers.Item);
            Box("Lid", trunk.transform, new Vector3(0f, 0.42f, 0f), new Vector3(1.03f, 0.18f, 1.05f), Mat("TrunkTrim", Hex("27456F")), false, GameLayers.Item);
            Box("HandleL", trunk.transform, new Vector3(-0.53f, 0.15f, 0f), new Vector3(0.06f, 0.14f, 0.4f), Mat("Climbable", Color.white), false, GameLayers.Item);
            Box("HandleR", trunk.transform, new Vector3(0.53f, 0.15f, 0f), new Vector3(0.06f, 0.14f, 0.4f), Mat("Climbable", Color.white), false, GameLayers.Item);
            var heavy = AddItem(trunk, "heavy_trunk", "Heavy Trunk", 26f, true, false);
            heavy.handForce = 170f;
            SavePrefab(trunk, HeavyCratePath);
        }

        static GrabbableItem AddItem(GameObject go, string id, string displayName, float mass, bool heavy, bool storable)
        {
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.angularDamping = 0.4f;
            var item = go.AddComponent<GrabbableItem>();
            item.itemId = id;
            item.displayName = displayName;
            item.heavy = heavy;
            item.storable = storable;
            return item;
        }

        // ------------------------------------------------------------------ Player rig

        static InputActionProperty Pose(string name, string binding, string controlType)
        {
            return new InputActionProperty(new InputAction(name, InputActionType.Value, binding, expectedControlType: controlType));
        }

        static TrackedPoseDriver AddPoseDriver(GameObject go, string device)
        {
            bool head = device == "<XRHMD>";
            var driver = go.AddComponent<TrackedPoseDriver>();
            driver.positionInput = Pose("Position", device + (head ? "/centerEyePosition" : "/devicePosition"), "Vector3");
            driver.rotationInput = Pose("Rotation", device + (head ? "/centerEyeRotation" : "/deviceRotation"), "Quaternion");
            driver.trackingStateInput = Pose("TrackingState", device + "/trackingState", "Integer");
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            return driver;
        }

        public static GameObject BuildPlayerRig(HandLocomotionSettings settings)
        {
            ClimbableMaterial();
            var root = new GameObject("PlayerRig");

            // --- Tracking space.
            var offset = new GameObject("CameraOffset");
            offset.transform.SetParent(root.transform, false);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(offset.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 250f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Hex("8FD3F4");
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            var headDriver = AddPoseDriver(cameraObject, "<XRHMD>");

            var leftController = new GameObject("LeftController");
            leftController.transform.SetParent(offset.transform, false);
            leftController.transform.localPosition = new Vector3(-0.2f, 1.08f, 0.2f);
            var leftDriver = AddPoseDriver(leftController, "<XRController>{LeftHand}");

            var rightController = new GameObject("RightController");
            rightController.transform.SetParent(offset.transform, false);
            rightController.transform.localPosition = new Vector3(0.2f, 1.08f, 0.2f);
            var rightDriver = AddPoseDriver(rightController, "<XRController>{RightHand}");

            // --- Virtual hands.
            GameObject leftHand = BuildPaw("LeftHand", root.transform, true);
            GameObject rightHand = BuildPaw("RightHand", root.transform, false);

            // --- Physics proxies (detached from the rig at runtime).
            var proxies = new GameObject("Proxies");
            proxies.transform.SetParent(root.transform, false);

            var bodyProxy = new GameObject("BodyProxy") { layer = GameLayers.PlayerBody };
            bodyProxy.transform.SetParent(proxies.transform, false);
            var bodyRigidbody = bodyProxy.AddComponent<Rigidbody>();
            bodyRigidbody.isKinematic = true;
            bodyRigidbody.useGravity = false;
            var bodyCapsule = bodyProxy.AddComponent<CapsuleCollider>();
            bodyCapsule.radius = settings.bodyRadius;
            bodyCapsule.height = settings.standingBodyLength + settings.bodyRadius;
            var bodyMarker = bodyProxy.AddComponent<PlayerBodyMarker>();

            Rigidbody leftProxy = BuildHandProxy("LeftHandProxy", proxies.transform, settings, out SphereCollider leftSphere, out PlayerBodyMarker leftMarker);
            Rigidbody rightProxy = BuildHandProxy("RightHandProxy", proxies.transform, settings, out _, out PlayerBodyMarker rightMarker);

            // --- Core systems.
            var motor = root.AddComponent<HandLocomotionMotor>();
            motor.Configure(settings, root.transform, bodyCapsule, leftSphere);

            var xrInput = root.AddComponent<XRRigInputProvider>();
            var simulatedInput = root.AddComponent<SimulatedRigInputProvider>();

            var rig = root.AddComponent<PlayerRig>();
            rig.Configure(cameraObject.transform, leftController.transform, rightController.transform,
                leftHand.transform, rightHand.transform, motor, xrInput, simulatedInput,
                new Behaviour[] { headDriver, leftDriver, rightDriver });

            bodyMarker.rig = rig;
            leftMarker.rig = rig;
            leftMarker.isHand = true;
            leftMarker.hand = Hand.Left;
            rightMarker.rig = rig;
            rightMarker.isHand = true;
            rightMarker.hand = Hand.Right;

            var proxyDriver = root.AddComponent<RigPhysicsProxies>();
            proxyDriver.Configure(rig, bodyRigidbody, leftProxy, rightProxy);

            root.AddComponent<HandContactFeedback>().Configure(rig);

            var origin = root.AddComponent<XROrigin>();
            origin.Camera = camera;
            origin.Origin = root;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            // --- Bag.
            StorageBag bag = BuildBag(root.transform, rig);

            // --- Grabbing.
            leftHand.AddComponent<HandGrabber>().Configure(rig, Hand.Left, bag);
            rightHand.AddComponent<HandGrabber>().Configure(rig, Hand.Right, bag);

            // --- Body visuals.
            BuildAvatar(root.transform, rig);

            // --- HUD: toast messages, wrist device, screen fade.
            var toastObject = new GameObject("HudToast");
            toastObject.transform.SetParent(root.transform, false);
            TextMeshPro toastLabel = Label("", toastObject.transform, Vector3.zero, new Vector2(1.3f, 0.3f), 0.75f, Hex("FFF4DC"));
            toastLabel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            toastLabel.outlineWidth = 0.2f;
            toastLabel.outlineColor = new Color32(30, 30, 36, 255);
            toastObject.AddComponent<HudToast>().Configure(rig, toastLabel);

            BuildWristDevice(leftHand.transform);

            GameObject fadeQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fadeQuad.name = "ScreenFade";
            Object.DestroyImmediate(fadeQuad.GetComponent<Collider>());
            fadeQuad.transform.SetParent(cameraObject.transform, false);
            fadeQuad.transform.localPosition = new Vector3(0f, 0f, 0.09f);
            fadeQuad.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
            var fadeRenderer = fadeQuad.GetComponent<MeshRenderer>();
            fadeRenderer.sharedMaterial = UnlitMaterial("ScreenFade", new Color(0f, 0f, 0f, 0f), true);
            fadeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fadeRenderer.enabled = false;
            root.AddComponent<SmallTown.UI.ScreenFader>().Configure(fadeRenderer);

            return SavePrefab(root, PlayerRigPath);
        }

        static void BuildWristDevice(Transform leftHand)
        {
            // Sits on the back of the left wrist. The back of a left hand faces -X in grip space.
            var device = new GameObject("WristDevice");
            device.transform.SetParent(leftHand, false);
            device.transform.localPosition = new Vector3(-0.045f, 0.0f, -0.09f);
            device.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            Box("Case", device.transform, new Vector3(0f, 0f, 0.012f), new Vector3(0.13f, 0.085f, 0.016f), Mat("GymDark", Hex("2B2B33")), false);
            Box("Screen", device.transform, new Vector3(0f, 0f, 0.003f), new Vector3(0.118f, 0.074f, 0.004f), Mat("GymSign", Hex("33475B")), false);
            TextMeshPro money = Label("$0", device.transform, new Vector3(-0.022f, 0.02f, 0f), new Vector2(0.07f, 0.03f), 0.2f, Hex("9FE2BF"), TextAlignmentOptions.Left);
            TextMeshPro objective = Label("", device.transform, new Vector3(0f, -0.006f, 0f), new Vector2(0.11f, 0.03f), 0.085f, Color.white);
            TextMeshPro status = Label("", device.transform, new Vector3(0f, -0.028f, 0f), new Vector2(0.11f, 0.014f), 0.075f, Hex("FFD166"));

            Material on = Mat("WantedOn", Hex("E0453A"));
            Material off = Mat("WantedOff", Hex("4A5568"));
            var pips = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                pips[i] = Box("Wanted" + i, device.transform, new Vector3(0.022f + i * 0.014f, 0.022f, 0f), new Vector3(0.01f, 0.01f, 0.004f), off, false)
                    .GetComponent<Renderer>();
            }
            device.AddComponent<SmallTown.UI.WristDevice>().Configure(money, objective, status, pips, on, off);
        }

        static Rigidbody BuildHandProxy(string name, Transform parent, HandLocomotionSettings settings,
            out SphereCollider sphere, out PlayerBodyMarker marker)
        {
            var go = new GameObject(name) { layer = GameLayers.PlayerHand };
            go.transform.SetParent(parent, false);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            sphere = go.AddComponent<SphereCollider>();
            sphere.radius = settings.handRadius;
            marker = go.AddComponent<PlayerBodyMarker>();
            return body;
        }

        static GameObject BuildPaw(string name, Transform parent, bool left)
        {
            var hand = new GameObject(name);
            hand.transform.SetParent(parent, false);
            hand.transform.localPosition = new Vector3(left ? -0.2f : 0.2f, 1.08f, 0.2f);

            // Built palm-down, then rolled so the palm faces inward like a real grip pose.
            var visual = new GameObject("Paw");
            visual.transform.SetParent(hand.transform, false);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, left ? 90f : -90f);

            if (CharacterBuilder.Part(left ? "HandL" : "HandR") != null)
            {
                Transform paw = CharacterBuilder.PartObject(left ? "HandL" : "HandR", visual.transform, CharacterBuilder.Palette(), Vector3.one * 0.1f);
                paw.localScale = Vector3.one * CharacterBuilder.HandScale;
                return hand;
            }

            Material fur = Mat("PandaMaroon", Maroon);
            Material pad = Mat("PandaCream", Cream);
            Box("Palm", visual.transform, Vector3.zero, new Vector3(0.095f, 0.04f, 0.1f), fur, false);
            Box("PalmPad", visual.transform, new Vector3(0f, -0.022f, -0.005f), new Vector3(0.055f, 0.008f, 0.05f), pad, false);
            for (int i = 0; i < 4; i++)
            {
                float x = (i - 1.5f) * 0.026f;
                Box("Finger" + i, visual.transform, new Vector3(x, 0f, 0.075f), new Vector3(0.021f, 0.034f, 0.055f), fur, false);
                Box("FingerPad" + i, visual.transform, new Vector3(x, -0.019f, 0.08f), new Vector3(0.015f, 0.006f, 0.022f), pad, false);
            }
            float thumbSide = left ? 0.058f : -0.058f;
            Box("Thumb", visual.transform, new Vector3(thumbSide, 0f, 0.015f), new Vector3(0.03f, 0.032f, 0.05f), fur, false);
            return hand;
        }

        static StorageBag BuildBag(Transform parent, PlayerRig rig)
        {
            var bagObject = new GameObject("Bag");
            bagObject.transform.SetParent(parent, false);
            bagObject.transform.localPosition = new Vector3(-0.27f, 1.1f, 0.08f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(bagObject.transform, false);
            Material canvas = Mat("BagCanvas", Hex("8A9A5B"));
            Material trim = Mat("BagTrim", Hex("5B6B3A"));
            Box("Back", visual.transform, new Vector3(0f, -0.02f, -0.06f), new Vector3(0.24f, 0.2f, 0.02f), canvas, false);
            Box("Front", visual.transform, new Vector3(0f, -0.04f, 0.06f), new Vector3(0.24f, 0.16f, 0.02f), canvas, false);
            Box("Left", visual.transform, new Vector3(-0.12f, -0.04f, 0f), new Vector3(0.02f, 0.16f, 0.12f), canvas, false);
            Box("Right", visual.transform, new Vector3(0.12f, -0.04f, 0f), new Vector3(0.02f, 0.16f, 0.12f), canvas, false);
            Box("Bottom", visual.transform, new Vector3(0f, -0.12f, 0f), new Vector3(0.24f, 0.02f, 0.12f), trim, false);
            Box("Strap", visual.transform, new Vector3(0f, 0.12f, -0.06f), new Vector3(0.05f, 0.1f, 0.015f), trim, false);

            var slots = new GameObject("Slots");
            slots.transform.SetParent(bagObject.transform, false);
            slots.transform.localPosition = new Vector3(0f, -0.04f, 0f);

            // Lies flat on the back rim so it reads when the player looks down at their hip.
            TextMeshPro label = Label("0/4", bagObject.transform, new Vector3(0f, 0.085f, -0.075f), new Vector2(0.2f, 0.08f), 0.55f, Cream);
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var bag = bagObject.AddComponent<StorageBag>();
            bag.Configure(rig, slots.transform, visual.transform, label);
            return bag;
        }

        static void BuildAvatar(Transform parent, PlayerRig rig)
        {
            var avatarRoot = new GameObject("Avatar");
            avatarRoot.transform.SetParent(parent, false);

            Material orange = Mat("PandaOrange", Orange);
            Material maroon = Mat("PandaMaroon", Maroon);
            Material cream = Mat("PandaCream", Cream);
            Material dark = Mat("PandaDark", DarkBrown);

            if (CharacterBuilder.Part("Torso") != null)
            {
                Material palette = CharacterBuilder.Palette();
                Transform torsoPart = CharacterBuilder.PartObject("Torso", avatarRoot.transform, palette, Vector3.one);
                var upperParts = new Transform[2];
                var lowerParts = new Transform[2];
                for (int i = 0; i < 2; i++)
                {
                    string side = i == 0 ? "Left" : "Right";
                    upperParts[i] = CharacterBuilder.PartObject("UpperArm", avatarRoot.transform, palette, Vector3.one, side + "UpperArm");
                    lowerParts[i] = CharacterBuilder.PartObject("Forearm", avatarRoot.transform, palette, Vector3.one, side + "Forearm");
                }
                parent.gameObject.AddComponent<PandaAvatar>().Configure(rig, torsoPart, null, upperParts, lowerParts);
                return;
            }

            var torso = new GameObject("Torso");
            torso.transform.SetParent(avatarRoot.transform, false);
            Box("Shoulders", torso.transform, new Vector3(0f, -0.07f, -0.02f), new Vector3(0.4f, 0.14f, 0.2f), orange, false);
            Box("Chest", torso.transform, new Vector3(0f, -0.2f, -0.02f), new Vector3(0.32f, 0.16f, 0.19f), maroon, false);
            Box("Belly", torso.transform, new Vector3(0f, -0.33f, -0.02f), new Vector3(0.26f, 0.12f, 0.17f), dark, false);
            Box("Bib", torso.transform, new Vector3(0f, -0.1f, 0.085f), new Vector3(0.14f, 0.09f, 0.02f), cream, false);
            for (int i = 0; i < 5; i++)
            {
                Box("Tail" + i, torso.transform, new Vector3(0f, -0.3f + i * 0.05f, -0.16f - i * 0.045f),
                    new Vector3(0.11f, 0.09f, 0.07f), i % 2 == 0 ? orange : maroon, false);
            }

            var upper = new Transform[2];
            var lower = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                upper[i] = Box(side + "UpperArm", avatarRoot.transform, Vector3.zero, Vector3.one * 0.08f, orange, false).transform;
                lower[i] = Box(side + "Forearm", avatarRoot.transform, Vector3.zero, Vector3.one * 0.08f, maroon, false).transform;
            }

            var avatar = parent.gameObject.AddComponent<PandaAvatar>();
            avatar.Configure(rig, torso.transform, null, upper, lower);
        }
    }
}
