using System;
using SmallTown.Interaction;
using SmallTown.Locomotion;
using SmallTown.RigInput;
using SmallTown.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SmallTown.Tests
{
    /// <summary>
    /// A complete player rig built in code and stepped by hand, so tests can
    /// script exact controller motion at any frame rate. Everything it creates
    /// is destroyed on Dispose.
    /// </summary>
    public sealed class RigHarness : IDisposable
    {
        public const float HeadHeight = 1.6f;
        public static readonly Vector3 RestLeft = new Vector3(-0.2f, -0.5f, 0.2f);
        public static readonly Vector3 RestRight = new Vector3(0.2f, -0.5f, 0.2f);

        public readonly GameObject Root;
        public readonly PlayerRig Rig;
        public readonly HandLocomotionMotor Motor;
        public readonly ManualRigInputProvider Input;
        public readonly HandLocomotionSettings Settings;
        public readonly Transform Head;
        public readonly Transform Left;
        public readonly Transform Right;
        public readonly HandGrabber LeftGrabber;
        public readonly HandGrabber RightGrabber;
        public readonly StorageBag Bag;
        public GameObject Floor { get; private set; }
        readonly GameObject scenery = new GameObject("TestScenery");
        readonly GameObject proxies = new GameObject("TestProxies");

        public RigHarness(Vector3 feet, float yaw = 0f, bool withFloor = true)
        {
            RigCalibration.PersistenceEnabled = false;
            TutorialController.PersistenceEnabled = false;

            Settings = ScriptableObject.CreateInstance<HandLocomotionSettings>();
            Settings.surfaceMask = GameLayers.LocomotionSurfaces;

            if (withFloor) Floor = AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));

            Root = new GameObject("TestRig");
            Root.SetActive(false);
            var offset = new GameObject("CameraOffset").transform;
            offset.SetParent(Root.transform, false);
            Head = Child(offset, "Head", new Vector3(0f, HeadHeight, 0f));
            Left = Child(offset, "LeftController", Vector3.zero);
            Right = Child(offset, "RightController", Vector3.zero);
            Transform leftHand = Child(Root.transform, "LeftHand", Vector3.zero);
            Transform rightHand = Child(Root.transform, "RightHand", Vector3.zero);

            var bodyObject = new GameObject("BodyProxy") { layer = GameLayers.PlayerBody };
            bodyObject.transform.SetParent(proxies.transform, false);
            bodyObject.AddComponent<Rigidbody>().isKinematic = true;
            var capsule = bodyObject.AddComponent<CapsuleCollider>();
            var handObject = new GameObject("HandProxy") { layer = GameLayers.PlayerHand };
            handObject.transform.SetParent(proxies.transform, false);
            handObject.AddComponent<Rigidbody>().isKinematic = true;
            var sphere = handObject.AddComponent<SphereCollider>();
            // Keep the proxies far from the action; the solver only uses their shapes.
            proxies.transform.position = new Vector3(0f, -500f, 0f);

            Motor = Root.AddComponent<HandLocomotionMotor>();
            Motor.Configure(Settings, Root.transform, capsule, sphere);
            Input = Root.AddComponent<ManualRigInputProvider>();
            Rig = Root.AddComponent<PlayerRig>();
            Rig.autoSimulate = false;
            Rig.Configure(Head, Left, Right, leftHand, rightHand, Motor, null, null, null);

            var bagObject = new GameObject("Bag");
            bagObject.transform.SetParent(Root.transform, false);
            Bag = bagObject.AddComponent<StorageBag>();
            Bag.Configure(Rig, bagObject.transform, null, null);

            LeftGrabber = leftHand.gameObject.AddComponent<HandGrabber>();
            LeftGrabber.Configure(Rig, Hand.Left, Bag);
            RightGrabber = rightHand.gameObject.AddComponent<HandGrabber>();
            RightGrabber.Configure(Rig, Hand.Right, Bag);

            Root.SetActive(true);
            Rig.SetInputOverride(Input);
            RestHands();
            Rig.TeleportFeet(feet, yaw);
        }

        static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }

        public GameObject AddBox(Vector3 centre, Vector3 size, Quaternion? rotation = null, int layer = GameLayers.World)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(scenery.transform, false);
            box.transform.SetPositionAndRotation(centre, rotation ?? Quaternion.identity);
            box.transform.localScale = size;
            box.layer = layer;
            Physics.SyncTransforms();
            return box;
        }

        public GrabbableItem AddItem(Vector3 position, float size = 0.12f, float mass = 1f, bool heavy = false, bool storable = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(scenery.transform, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * size;
            go.layer = GameLayers.Item;
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            var item = go.AddComponent<GrabbableItem>();
            item.heavy = heavy;
            item.storable = storable;
            Physics.SyncTransforms();
            return item;
        }

        /// <summary>Head position above the tracking floor, in rig space.</summary>
        public void SetHeadLocal(Vector3 local) => Head.localPosition = local;

        /// <summary>Places a controller relative to the head, in rig space.</summary>
        public void SetHand(Hand hand, Vector3 headRelative)
        {
            (hand == Hand.Left ? Left : Right).localPosition = Head.localPosition + headRelative;
        }

        public void RestHands()
        {
            SetHand(Hand.Left, RestLeft);
            SetHand(Hand.Right, RestRight);
        }

        public void Step(float dt) => Rig.Simulate(dt);

        public void Run(float seconds, float dt, Action<float> beforeEachStep = null)
        {
            int frames = Mathf.RoundToInt(seconds / dt);
            for (int i = 1; i <= frames; i++)
            {
                beforeEachStep?.Invoke(i * dt);
                Step(dt);
            }
        }

        /// <summary>Moves one controller in a straight line over the given time, stepping every frame.</summary>
        public void Stroke(Hand hand, Vector3 from, Vector3 to, float seconds, float dt)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
            for (int i = 1; i <= frames; i++)
            {
                SetHand(hand, Vector3.Lerp(from, to, (float)i / frames));
                Step(dt);
            }
        }

        public void StrokeBoth(Vector3 fromRight, Vector3 toRight, float seconds, float dt)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
            for (int i = 1; i <= frames; i++)
            {
                Vector3 right = Vector3.Lerp(fromRight, toRight, (float)i / frames);
                SetHand(Hand.Right, right);
                SetHand(Hand.Left, new Vector3(-right.x, right.y, right.z));
                Step(dt);
            }
        }

        /// <summary>Time-parameterised alternating arm strokes, identical at any frame rate.</summary>
        public void ApplyStride(float time, float cycleSeconds = 0.6f)
        {
            for (int i = 0; i < 2; i++)
            {
                float phase = Mathf.Repeat(time / cycleSeconds + i * 0.5f, 1f);
                float side = i == 0 ? -0.2f : 0.2f;
                Vector3 pose;
                float down = PressDepth - 0.02f;
                if (phase < 0.45f) pose = new Vector3(side, down, Mathf.Lerp(0.4f, -0.2f, phase / 0.45f));
                else if (phase < 0.6f) pose = new Vector3(side, Mathf.Lerp(down, -0.55f, (phase - 0.45f) / 0.15f), -0.2f);
                else if (phase < 0.85f) pose = new Vector3(side, -0.55f, Mathf.Lerp(-0.2f, 0.4f, (phase - 0.6f) / 0.25f));
                else pose = new Vector3(side, Mathf.Lerp(-0.55f, down, (phase - 0.85f) / 0.15f), 0.4f);
                SetHand((Hand)i, pose);
            }
        }

        /// <summary>Depth, relative to the head, at which a hand presses lightly (about 2 cm) on the ground the body stands on.</summary>
        public float PressDepth => -(Settings.standingBodyLength + Settings.skinWidth - Settings.handRadius - Settings.skinWidth) - 0.02f;

        /// <summary>
        /// Reaches out through the air, then lowers the hand gently onto the ground.
        /// A slow plant matters: slapping the ground is a real push and makes the body hop.
        /// </summary>
        public Vector3 Plant(Hand hand, float forward, float dt)
        {
            float side = hand == Hand.Left ? -0.2f : 0.2f;
            Vector3 rest = hand == Hand.Left ? RestLeft : RestRight;
            Vector3 above = new Vector3(side, -0.55f, forward);
            Vector3 planted = new Vector3(side, PressDepth, forward);
            Stroke(hand, rest, above, 0.1f, dt);
            Stroke(hand, above, planted, 0.25f, dt);
            Run(0.1f, dt);
            return planted;
        }

        public void Settle(float seconds = 0.5f) => Run(seconds, 1f / 72f);

        public void Dispose()
        {
            Object.DestroyImmediate(Root);
            Object.DestroyImmediate(scenery);
            Object.DestroyImmediate(proxies);
            Object.DestroyImmediate(Settings);
        }
    }
}
