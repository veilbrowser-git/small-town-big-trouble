using SmallTown.Avatar;
using UnityEngine;

namespace SmallTown.Npc
{
    public enum NpcPose
    {
        Idle,
        Walk,
        HandsUp,
        Reach,
        Cower,
        Wave,
        Dizzy,
    }

    /// <summary>
    /// Visual body for townsfolk and police: the same legless shape as the
    /// player, posed procedurally. NPCs "walk" by pawing the ground, so all the
    /// character is in the arms. The transform this sits on is the NPC's ground
    /// position and facing.
    /// </summary>
    public sealed class NpcBody : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] Transform torso;
        [SerializeField] Transform[] upperArms = new Transform[2];
        [SerializeField] Transform[] forearms = new Transform[2];
        [SerializeField] Transform[] hands = new Transform[2];
        public float eyeHeight = 0.8f;
        public float armThickness = 0.16f;
        public float armSegment = 0.46f;
        public NpcPose pose = NpcPose.Idle;
        [Tooltip("World point the hands reach for in the Reach pose.")]
        public Vector3 reachTarget;

        Vector3 lastPosition;
        float stride;
        float clock;
        readonly Vector3[] handLocal = new Vector3[2];
        bool poseSet;

        Renderer[] renderers;
        Collider[] bodyColliders;

        /// <summary>False while the character is off duty: invisible and untouchable, but still in the scene.</summary>
        public bool Present { get; private set; } = true;

        public void SetPresent(bool present)
        {
            if (Present == present && renderers != null) return;
            Present = present;
            if (renderers == null)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
                bodyColliders = GetComponentsInChildren<Collider>(true);
            }
            foreach (Renderer r in renderers)
            {
                if (r != null) r.enabled = present;
            }
            foreach (Collider c in bodyColliders)
            {
                if (c != null) c.enabled = present;
            }
        }

        public Vector3 EyePosition => transform.position + Vector3.up * eyeHeight;
        public Transform Head => head;

        public void Configure(Transform headPart, Transform torsoPart, Transform[] upper, Transform[] lower, Transform[] handParts)
        {
            head = headPart;
            torso = torsoPart;
            upperArms = upper;
            forearms = lower;
            hands = handParts;
        }

        void OnEnable()
        {
            lastPosition = transform.position;
            poseSet = false;
        }

        void LateUpdate() => Pose(Time.deltaTime);

        /// <summary>Positions every body part for the current pose. Public so edit-time previews can call it.</summary>
        public void Pose(float deltaTime)
        {
            clock += deltaTime;
            Vector3 moved = transform.position - lastPosition;
            lastPosition = transform.position;
            moved.y = 0f;
            stride += moved.magnitude * 2.6f;

            NpcPose active = pose;
            if (active == NpcPose.Idle && deltaTime > 0f && moved.magnitude / deltaTime > 0.15f) active = NpcPose.Walk;

            Quaternion facing = transform.rotation;
            float bob = active == NpcPose.Walk ? Mathf.Abs(Mathf.Sin(stride)) * 0.04f : Mathf.Sin(clock * 2f) * 0.008f;
            float wobble = active == NpcPose.Dizzy ? Mathf.Sin(clock * 9f) * 14f : 0f;
            Vector3 headPosition = transform.position + Vector3.up * (eyeHeight + bob);
            Quaternion lean = facing * Quaternion.Euler(active == NpcPose.Cower ? 22f : 0f, 0f, wobble);
            if (head != null) head.SetPositionAndRotation(headPosition, lean);
            if (torso != null) torso.SetPositionAndRotation(headPosition + lean * Vector3.down * 0.19f, lean);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Vector3 target = HandTarget(active, side, i);
                handLocal[i] = poseSet ? Vector3.Lerp(handLocal[i], target, 1f - Mathf.Exp(-14f * deltaTime)) : target;

                Vector3 shoulder = headPosition + lean * new Vector3(side * 0.21f, -0.26f, 0f);
                Vector3 handWorld = active == NpcPose.Reach
                    ? Vector3.Lerp(transform.TransformPoint(handLocal[i]), reachTarget + facing * new Vector3(side * 0.1f, 0f, 0f), 0.85f)
                    : transform.TransformPoint(handLocal[i]);
                Vector3 offset = handWorld - shoulder;
                float reach = armSegment * 1.95f;
                if (offset.magnitude > reach) handWorld = shoulder + offset.normalized * reach;

                ArmSolver.Solve(shoulder, handWorld, armSegment, facing, side, upperArms[i], forearms[i], armThickness);
                if (hands[i] != null)
                {
                    bool raised = active == NpcPose.HandsUp || active == NpcPose.Wave && i == 1 || active == NpcPose.Cower;
                    Quaternion handRotation = raised
                        ? facing * Quaternion.Euler(-80f, 0f, 0f)
                        : facing * Quaternion.Euler(10f, 0f, 0f);
                    hands[i].SetPositionAndRotation(handWorld, handRotation);
                }
            }
            poseSet = true;
        }

        Vector3 HandTarget(NpcPose active, float side, int index)
        {
            switch (active)
            {
                case NpcPose.Walk:
                {
                    float phase = stride + index * Mathf.PI;
                    return new Vector3(side * 0.27f, 0.07f + Mathf.Max(0f, Mathf.Cos(phase)) * 0.16f, Mathf.Sin(phase) * 0.26f + 0.08f);
                }
                case NpcPose.HandsUp:
                    return new Vector3(side * 0.34f, eyeHeight + 0.42f + Mathf.Sin(clock * 11f + index) * 0.015f, 0.08f);
                case NpcPose.Cower:
                    return new Vector3(side * 0.14f, eyeHeight + 0.12f, 0.2f);
                case NpcPose.Wave:
                    return index == 1
                        ? new Vector3(0.36f + Mathf.Sin(clock * 7f) * 0.12f, eyeHeight + 0.42f, 0.1f)
                        : new Vector3(-0.27f, 0.2f, 0.1f);
                case NpcPose.Reach:
                    return new Vector3(side * 0.16f, eyeHeight - 0.2f, 0.6f);
                case NpcPose.Dizzy:
                    return new Vector3(side * 0.4f, 0.4f + Mathf.Sin(clock * 9f + index * 2f) * 0.1f, 0.05f);
                default:
                    return new Vector3(side * 0.27f, 0.12f + Mathf.Sin(clock * 2f + index) * 0.01f, 0.1f);
            }
        }
    }
}
