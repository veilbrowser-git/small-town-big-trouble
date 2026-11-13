using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>
    /// Every tuning value for hand-driven locomotion lives here so designers can
    /// iterate on feel without touching solver code.
    /// </summary>
    [CreateAssetMenu(menuName = "Small Town/Hand Locomotion Settings", fileName = "HandLocomotionSettings")]
    public sealed class HandLocomotionSettings : ScriptableObject
    {
        [Header("Hands")]
        [Tooltip("Radius of the collision sphere that represents each virtual hand.")]
        [Min(0.01f)] public float handRadius = 0.06f;

        [Tooltip("Maximum distance from the shoulder pivot to a hand. Calibration overrides this per player.")]
        [Min(0.2f)] public float maxArmReach = 0.9f;

        [Tooltip("The shoulder pivot sits this far below the headset.")]
        [Min(0f)] public float shoulderDrop = 0.14f;

        [Tooltip("Gap kept between a hand or the body and any surface so sweeps never start overlapped.")]
        [Min(0.001f)] public float skinWidth = 0.008f;

        [Tooltip("A hand that separates this far from its controller gives up its contact and re-seeds.")]
        [Min(0.1f)] public float handUnstickDistance = 0.45f;

        [Tooltip("Controller speed above this is treated as a tracking glitch, not a push (m/s).")]
        [Min(1f)] public float handGlitchSpeed = 25f;

        [Tooltip("When false, a hand that is holding an item never propels the player.")]
        public bool occupiedHandsCanPush;

        [Header("Surface friction (0 = ice, 1 = fully sticky)")]
        [Range(0f, 1f)] public float groundFriction = 1f;

        [Tooltip("Friction on steep surfaces. Below 1 the player cannot hang on ordinary walls.")]
        [Range(0f, 1f)] public float wallFriction = 0.45f;

        [Tooltip("Surfaces steeper than this (degrees from horizontal) use wall friction.")]
        [Range(10f, 89f)] public float wallAngle = 55f;

        [Header("Propulsion")]
        [Tooltip("Scale applied to the pushed velocity at the moment the last hand lets go.")]
        [Min(0f)] public float propulsionMultiplier = 1.1f;

        [Tooltip("Extra scale on the upward part of a release, i.e. jump strength.")]
        [Min(0f)] public float jumpMultiplier = 1.15f;

        [Tooltip("Releases slower than this fade out instead of launching (m/s).")]
        [Min(0f)] public float minLaunchSpeed = 0.35f;

        [Tooltip("Fastest speed a single release can produce (m/s).")]
        [Min(0.5f)] public float maxLaunchSpeed = 6.5f;

        [Tooltip("Absolute body speed limit (m/s).")]
        [Min(1f)] public float maxSpeed = 14f;

        [Tooltip("Seconds of recent motion averaged to decide release velocity. Time based so it behaves the same at 72, 90 and 120 Hz.")]
        [Range(0.03f, 0.3f)] public float velocityWindow = 0.1f;

        [Header("Gravity and momentum decay")]
        [Min(0f)] public float gravity = 9.81f;

        [Tooltip("Constant deceleration while the body slides on walkable ground (m/s^2).")]
        [Min(0f)] public float groundDeceleration = 5f;

        [Tooltip("Proportional speed loss on the ground (1/s).")]
        [Min(0f)] public float groundDamping = 1.5f;

        [Tooltip("Proportional speed loss in the air (1/s).")]
        [Min(0f)] public float airDamping = 0.05f;

        [Tooltip("Whether leaving a moving platform keeps the platform's velocity.")]
        public bool inheritPlatformVelocity = true;

        [Header("Body")]
        [Tooltip("Radius of the body capsule and the head sphere on top of it.")]
        [Min(0.05f)] public float bodyRadius = 0.17f;

        [Tooltip("Distance from the headset down to the bottom of the body when upright. Calibration overrides this per player.")]
        [Min(0.3f)] public float standingBodyLength = 0.78f;

        [Tooltip("Shortest body length when the player physically crouches.")]
        [Min(0.2f)] public float crouchBodyLength = 0.36f;

        [Tooltip("Speed at which a crouched body stretches back out when it has room (m/s).")]
        [Min(0.1f)] public float standUpSpeed = 1.5f;

        [Tooltip("Ground steeper than this is not walkable and the body slides off (degrees).")]
        [Range(10f, 80f)] public float maxGroundAngle = 50f;

        [Tooltip("Ledges up to this height are stepped over while moving along the ground.")]
        [Min(0f)] public float stepHeight = 0.22f;

        [Tooltip("Head movement larger than this in one frame is treated as a recentre, not as motion (m).")]
        [Min(0.1f)] public float trackingJumpDistance = 0.6f;

        [Header("Turning")]
        [Range(5f, 90f)] public float snapTurnDegrees = 45f;
        [Range(0.1f, 0.95f)] public float snapTurnDeadzone = 0.7f;

        [Header("Safety")]
        [Tooltip("Falling below this height respawns the player.")]
        public float killPlaneY = -30f;

        [Tooltip("Seconds between recorded safe positions used by the unstuck action.")]
        [Min(0.1f)] public float safePositionInterval = 0.75f;

        [Tooltip("The simulation never advances by more than this in one step; longer frames are split.")]
        [Range(0.005f, 0.05f)] public float maxStep = 1f / 60f;

        [Header("Collision layers")]
        [Tooltip("Layers hands can push against and the body collides with.")]
        public LayerMask surfaceMask = 1;

        public float FrictionFor(Vector3 normal, LocomotionSurface surface)
        {
            if (surface != null)
            {
                if (surface.climbable) return 1f;
                if (surface.overrideFriction) return surface.friction;
            }
            float angle = Vector3.Angle(normal, Vector3.up);
            return angle > wallAngle ? wallFriction : groundFriction;
        }
    }
}
