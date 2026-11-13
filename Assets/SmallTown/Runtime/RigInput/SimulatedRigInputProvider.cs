using SmallTown.Locomotion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SmallTown.RigInput
{
    /// <summary>
    /// Development-only keyboard and mouse stand-in for a headset. It does not
    /// move the player directly: keys play scripted arm strokes, and the real
    /// contact solver turns those strokes into motion. That keeps desktop
    /// testing honest about collision, friction and momentum.
    ///
    /// This is a development aid. It does not replace testing on a headset.
    /// </summary>
    public sealed class SimulatedRigInputProvider : ManualRigInputProvider
    {
        [Header("Simulated body")]
        public float standingHeadHeight = 1.6f;
        public float crouchDrop = 0.45f;
        public float mouseSensitivity = 0.12f;

        [Header("Scripted strokes (head space, metres)")]
        public float strideForward = 0.42f;
        public float strideBack = -0.2f;
        public float strideDepth = 0.84f;
        public float strideLift = 0.5f;
        public float strideSide = 0.2f;
        public float strideSeconds = 0.5f;
        public float jumpSeconds = 0.14f;
        public float manualSpeed = 0.9f;

        public override bool IsAvailable =>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            isActiveAndEnabled && Keyboard.current != null;
#else
            false;
#endif

        float yaw;
        float pitch;
        float stridePhase;
        float jumpTimer = -1f;
        float crouch;
        bool lookLocked;
        int manualHand = -1;
        readonly Vector3[] manualOffset = new Vector3[2];
        readonly Vector3[] handLocal = new Vector3[2];

        /// <summary>Latched one-frame requests that PlayerRig consumes.</summary>
        public bool UnstuckHeld { get; private set; }
        public bool CalibrateHeld { get; private set; }
        public bool RespawnPressed { get; private set; }

        public override void OnSelected(PlayerRig rig)
        {
            yaw = rig.Head != null ? rig.Head.localEulerAngles.y : 0f;
            pitch = 0f;
            handLocal[0] = RestPose(0);
            handLocal[1] = RestPose(1);
        }

        Vector3 RestPose(int hand) => new Vector3(hand == 0 ? -strideSide : strideSide, -0.52f, 0.2f);

        public override void UpdatePoses(PlayerRig rig, float deltaTime)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || rig.Head == null) return;

            // --- Look.
            if (keyboard.tabKey.wasPressedThisFrame) lookLocked = !lookLocked;
            bool looking = lookLocked || (mouse != null && mouse.rightButton.isPressed);
            if (looking && mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -80f, 80f);
            }

            // --- Buttons.
            leftGrip = keyboard.gKey.isPressed ? 1f : 0f;
            rightGrip = keyboard.hKey.isPressed ? 1f : 0f;
            leftTrigger = keyboard.tKey.isPressed ? 1f : 0f;
            rightTrigger = (mouse != null && mouse.leftButton.isPressed) || keyboard.yKey.isPressed ? 1f : 0f;
            menu = keyboard.mKey.isPressed;
            UnstuckHeld = keyboard.uKey.isPressed;
            CalibrateHeld = keyboard.cKey.isPressed;
            RespawnPressed = keyboard.rKey.wasPressedThisFrame;

            float turn = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
            rightStick = new Vector2(turn, 0f);

            // --- Head.
            float crouchTarget = keyboard.leftCtrlKey.isPressed ? crouchDrop : 0f;
            crouch = Mathf.MoveTowards(crouch, crouchTarget, deltaTime * 1.5f);
            rig.Head.localPosition = new Vector3(0f, standingHeadHeight - crouch, 0f);
            rig.Head.localRotation = Quaternion.Euler(pitch, yaw, 0f);

            // --- Hands.
            if (keyboard.digit1Key.wasPressedThisFrame) manualHand = manualHand == 0 ? -1 : 0;
            if (keyboard.digit2Key.wasPressedThisFrame) manualHand = manualHand == 1 ? -1 : 1;
            if (keyboard.digit3Key.wasPressedThisFrame)
            {
                manualHand = -1;
                manualOffset[0] = manualOffset[1] = Vector3.zero;
            }

            if (manualHand >= 0)
            {
                Vector3 move = new Vector3(
                    (keyboard.lKey.isPressed ? 1f : 0f) - (keyboard.jKey.isPressed ? 1f : 0f),
                    (keyboard.oKey.isPressed ? 1f : 0f) - (keyboard.periodKey.isPressed ? 1f : 0f),
                    (keyboard.iKey.isPressed ? 1f : 0f) - (keyboard.kKey.isPressed ? 1f : 0f));
                manualOffset[manualHand] = Vector3.ClampMagnitude(manualOffset[manualHand] + move * (manualSpeed * deltaTime), 0.9f);
            }

            float forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            float strafe = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            if (keyboard.spaceKey.wasPressedThisFrame && jumpTimer < 0f) jumpTimer = 0f;

            Vector3 strokeDirection = new Vector3(strafe, 0f, forward);
            bool striding = strokeDirection.sqrMagnitude > 0.01f;
            if (striding) stridePhase = Mathf.Repeat(stridePhase + deltaTime / strideSeconds, 1f);

            for (int hand = 0; hand < 2; hand++)
            {
                Vector3 target = RestPose(hand);
                if (jumpTimer >= 0f)
                {
                    target = JumpPose(hand, jumpTimer / jumpSeconds);
                }
                else if (striding)
                {
                    target = StridePose(hand, Mathf.Repeat(stridePhase + hand * 0.5f, 1f), strokeDirection.normalized);
                }
                // F raises the right hand to eye level and points it where you look (aiming).
                bool aiming = hand == 1 && keyboard.fKey.isPressed;
                if (aiming) target = new Vector3(0.16f, -0.1f, 0.42f);
                target += manualOffset[hand];

                // Ease toward rest so releasing a key does not snap the hand.
                float speed = striding || jumpTimer >= 0f ? 12f : 4f;
                handLocal[hand] = Vector3.MoveTowards(handLocal[hand], target, speed * deltaTime);

                Transform controller = hand == 0 ? rig.LeftController : rig.RightController;
                Quaternion bodyYaw = Quaternion.Euler(0f, yaw, 0f);
                controller.localPosition = rig.Head.localPosition + bodyYaw * handLocal[hand];
                controller.localRotation = aiming ? rig.Head.localRotation : bodyYaw * Quaternion.Euler(20f, 0f, 0f);
            }

            if (jumpTimer >= 0f)
            {
                jumpTimer += deltaTime;
                if (jumpTimer > jumpSeconds + 0.25f) jumpTimer = -1f;
            }
        }

        /// <summary>
        /// One arm cycle: plant ahead, drag back along the ground (this is the
        /// push), lift, return through the air.
        /// </summary>
        Vector3 StridePose(int hand, float phase, Vector3 direction)
        {
            float side = hand == 0 ? -strideSide : strideSide;
            float along;
            float height;
            if (phase < 0.5f)
            {
                float t = phase / 0.5f;
                along = Mathf.Lerp(strideForward, strideBack, t);
                height = -strideDepth;
            }
            else
            {
                float t = (phase - 0.5f) / 0.5f;
                along = Mathf.Lerp(strideBack, strideForward, t);
                height = -Mathf.Lerp(strideLift, strideDepth, Mathf.Abs(t * 2f - 1f) * Mathf.Abs(t * 2f - 1f));
            }
            Vector3 right = new Vector3(direction.z, 0f, -direction.x);
            return direction * along + right * side + Vector3.up * height;
        }

        Vector3 JumpPose(int hand, float t)
        {
            float side = hand == 0 ? -strideSide : strideSide;
            float depth = Mathf.Lerp(0.5f, 1.0f, Mathf.Clamp01(t));
            if (t > 1f) depth = Mathf.Lerp(1.0f, 0.45f, Mathf.Clamp01((t - 1f) * 4f));
            return new Vector3(side, -depth, 0.15f);
        }
    }
}
