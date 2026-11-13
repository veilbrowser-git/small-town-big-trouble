using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown.Avatar
{
    /// <summary>
    /// Drives the legless red panda body: a torso that hangs under the head and
    /// two long arms solved from shoulder to hand. All the expressive motion is
    /// in the arms; the camera itself is never moved by the avatar.
    /// </summary>
    public sealed class PandaAvatar : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [SerializeField] Transform torso;
        [SerializeField] Transform headVisual;
        [SerializeField] Transform[] upperArms = new Transform[2];
        [SerializeField] Transform[] forearms = new Transform[2];
        public Vector3 shoulderOffset = new Vector3(0.21f, -0.26f, 0f);
        public float torsoDrop = 0.19f;
        [Range(0f, 90f)] public float torsoFollowAngle = 30f;
        [Min(0.01f)] public float armThickness = 0.16f;

        float yaw;
        bool yawSet;

        public void Configure(PlayerRig owner, Transform torsoVisual, Transform head, Transform[] upper, Transform[] lower)
        {
            rig = owner;
            torso = torsoVisual;
            headVisual = head;
            upperArms = upper;
            forearms = lower;
        }

        void OnEnable()
        {
            if (rig != null) rig.Simulated += Pose;
        }

        void OnDisable()
        {
            if (rig != null) rig.Simulated -= Pose;
        }

        void Pose(float deltaTime)
        {
            Transform head = rig.Head;
            if (head == null) return;

            float headYaw = rig.BodyYaw.eulerAngles.y;
            if (!yawSet)
            {
                yaw = headYaw;
                yawSet = true;
            }
            float difference = Mathf.DeltaAngle(yaw, headYaw);
            if (Mathf.Abs(difference) > torsoFollowAngle) yaw += difference - Mathf.Sign(difference) * torsoFollowAngle;
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);

            if (torso != null) torso.SetPositionAndRotation(head.position + Vector3.down * torsoDrop, facing);
            if (headVisual != null) headVisual.SetPositionAndRotation(head.position, head.rotation);

            float reach = rig.Motor != null && rig.Motor.Settings != null ? rig.Motor.ArmReach : 0.9f;
            for (int i = 0; i < 2; i++)
            {
                Transform hand = rig.HandTransform((Hand)i);
                if (hand == null || upperArms[i] == null || forearms[i] == null) continue;

                Vector3 offset = shoulderOffset;
                if (i == 0) offset.x = -offset.x;
                Vector3 shoulder = head.position + facing * offset;
                ArmSolver.Solve(shoulder, hand.position, reach * 0.52f, facing, i == 0 ? -1f : 1f, upperArms[i], forearms[i], armThickness);
            }
        }
    }
}
