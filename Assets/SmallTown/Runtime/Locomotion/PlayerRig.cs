using System;
using SmallTown.RigInput;
using UnityEngine;

namespace SmallTown.Locomotion
{
    public enum MovementMode
    {
        /// <summary>Hands push against the world.</summary>
        HandLocomotion,
        /// <summary>Attached to a seat; the seat moves the rig.</summary>
        Seated,
        /// <summary>Nothing moves the rig (menus, arrest transition, loading).</summary>
        Frozen,
    }

    /// <summary>
    /// The one place that is allowed to move the XR rig. Hand locomotion, snap
    /// turning, teleports, seats and respawns all go through here, so two
    /// systems can never apply conflicting movement in the same frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerRig : MonoBehaviour
    {
        [Header("Rig transforms")]
        [SerializeField] Transform head;
        [SerializeField] Transform leftController;
        [SerializeField] Transform rightController;
        [SerializeField] Transform leftHand;
        [SerializeField] Transform rightHand;

        [Header("Systems")]
        [SerializeField] HandLocomotionMotor motor;
        [SerializeField] RigInputProvider xrInput;
        [SerializeField] RigInputProvider simulatedInput;
        [SerializeField] Behaviour[] trackedPoseDrivers = Array.Empty<Behaviour>();

        [Header("Behaviour")]
        [Tooltip("When false nothing runs in Update; tests call Simulate themselves.")]
        public bool autoSimulate = true;
        public bool snapTurnEnabled = true;
        [Min(0.2f)] public float holdSeconds = 1.2f;

        RigInputProvider activeInput;
        RigInputProvider overrideInput;
        MovementMode mode = MovementMode.HandLocomotion;
        Transform seat;
        Vector3 seatLocalAnchor;
        Vector3 rigLocalAnchor;
        float seatYawOffset;
        bool turnLatched;
        float unstuckTimer;
        float calibrateTimer;
        Transform spawnPoint;
        readonly Vector3[] lateOffset = new Vector3[2];
        readonly bool[] lateFollow = new bool[2];
        readonly bool[] wasTouching = new bool[2];
        readonly Vector3[] lastRawHand = new Vector3[2];

        public event Action<float> SnapTurned;
        public event Action Teleported;
        public event Action Unstuck;
        public event Action Calibrated;
        public event Action<MovementMode> ModeChanged;
        /// <summary>Raised at the end of every simulated frame, after hands are placed. Interaction runs from this.</summary>
        public event Action<float> Simulated;
        /// <summary>A hand just made contact with a surface: hand, contact position, approach speed.</summary>
        public event Action<Hand, Vector3, float> HandTouched;

        public Transform Head => head;
        public Transform LeftController => leftController;
        public Transform RightController => rightController;
        public HandLocomotionMotor Motor => motor;
        public MovementMode Mode => mode;
        public RigInputProvider Input => overrideInput != null ? overrideInput : activeInput;
        public RigCalibration Calibration { get; private set; } = new RigCalibration();

        public Transform Controller(Hand hand) => hand == Hand.Left ? leftController : rightController;
        public Transform HandTransform(Hand hand) => hand == Hand.Left ? leftHand : rightHand;

        /// <summary>Body-facing direction: the head's yaw without pitch or roll.</summary>
        public Quaternion BodyYaw
        {
            get
            {
                Vector3 forward = head.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-4f) forward = head.up * -Mathf.Sign(head.forward.y);
                forward.y = 0f;
                return forward.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        public void Configure(Transform headTransform, Transform leftRaw, Transform rightRaw, Transform leftVirtual,
            Transform rightVirtual, HandLocomotionMotor locomotion, RigInputProvider xr, RigInputProvider simulated,
            Behaviour[] poseDrivers)
        {
            head = headTransform;
            leftController = leftRaw;
            rightController = rightRaw;
            leftHand = leftVirtual;
            rightHand = rightVirtual;
            motor = locomotion;
            xrInput = xr;
            simulatedInput = simulated;
            trackedPoseDrivers = poseDrivers ?? Array.Empty<Behaviour>();
        }

        /// <summary>Force a specific input source (tests, replays). Pass null to return to automatic selection.</summary>
        public void SetInputOverride(RigInputProvider provider)
        {
            overrideInput = provider;
            ApplyPoseDriverState();
        }

        void Awake()
        {
            Calibration = RigCalibration.Load();
            ApplyCalibration();
        }

        void OnEnable()
        {
            if (motor != null) motor.FellOutOfWorld += Respawn;
            if (xrInput is XRRigInputProvider xr) xr.TrackingOriginChanged += OnTrackingOriginChanged;
            Application.onBeforeRender += OnBeforeRender;
        }

        void OnDisable()
        {
            if (motor != null) motor.FellOutOfWorld -= Respawn;
            if (xrInput is XRRigInputProvider xr) xr.TrackingOriginChanged -= OnTrackingOriginChanged;
            Application.onBeforeRender -= OnBeforeRender;
        }

        void Start()
        {
            SelectInput();
            if (spawnPoint == null)
            {
                var marker = FindAnyObjectByType<PlayerSpawnPoint>();
                if (marker != null) spawnPoint = marker.transform;
            }
            if (autoSimulate && spawnPoint != null) Respawn();
        }

        void Update()
        {
            if (autoSimulate) Simulate(Time.deltaTime);
        }

        /// <summary>Runs one frame of the whole rig pipeline, in a fixed order.</summary>
        public void Simulate(float deltaTime)
        {
            SelectInput();
            RigInputProvider input = Input;
            if (input == null || head == null) return;

            input.UpdatePoses(this, deltaTime);
            HandleActions(input, deltaTime);

            switch (mode)
            {
                case MovementMode.HandLocomotion:
                    motor.Simulate(deltaTime, new HandLocomotionMotor.RigSample
                    {
                        headLocal = transform.InverseTransformPoint(head.position),
                        leftLocal = transform.InverseTransformPoint(leftController.position),
                        rightLocal = transform.InverseTransformPoint(rightController.position),
                        leftTracked = input.IsTracked(Hand.Left),
                        rightTracked = input.IsTracked(Hand.Right),
                    });
                    break;
                case MovementMode.Seated:
                    FollowSeat();
                    break;
            }

            PlaceHand(Hand.Left, input);
            PlaceHand(Hand.Right, input);
            ReportContacts(deltaTime);
            Simulated?.Invoke(deltaTime);
        }

        void ReportContacts(float deltaTime)
        {
            for (int i = 0; i < 2; i++)
            {
                Transform controller = Controller((Hand)i);
                Vector3 raw = controller != null ? controller.position : Vector3.zero;
                bool touching = mode == MovementMode.HandLocomotion && motor.IsHandTouching((Hand)i);
                if (touching && !wasTouching[i] && deltaTime > 0f)
                {
                    float speed = (raw - lastRawHand[i]).magnitude / deltaTime;
                    HandTouched?.Invoke((Hand)i, motor.HandPosition((Hand)i), speed);
                }
                wasTouching[i] = touching;
                lastRawHand[i] = raw;
            }
        }

        void SelectInput()
        {
            RigInputProvider best = null;
            if (xrInput != null && xrInput.IsAvailable) best = xrInput;
            else if (simulatedInput != null && simulatedInput.IsAvailable) best = simulatedInput;
            if (best == activeInput) return;

            if (activeInput != null) activeInput.OnDeselected(this);
            activeInput = best;
            if (activeInput != null) activeInput.OnSelected(this);
            ApplyPoseDriverState();
            // The poses are about to jump to a different source; never treat that as motion.
            if (motor != null) motor.ResetState();
        }

        void ApplyPoseDriverState()
        {
            RigInputProvider input = Input;
            bool driversOn = input == null || !input.DrivesPoses;
            foreach (var driver in trackedPoseDrivers)
            {
                if (driver != null) driver.enabled = driversOn;
            }
        }

        void HandleActions(RigInputProvider input, float deltaTime)
        {
            // Snap turn on the right stick; one turn per flick.
            float x = input.Thumbstick(Hand.Right).x;
            float deadzone = motor.Settings != null ? motor.Settings.snapTurnDeadzone : 0.7f;
            if (Mathf.Abs(x) < deadzone * 0.6f)
            {
                turnLatched = false;
            }
            else if (!turnLatched && Mathf.Abs(x) >= deadzone && snapTurnEnabled && mode != MovementMode.Frozen)
            {
                turnLatched = true;
                float degrees = motor.Settings != null ? motor.Settings.snapTurnDegrees : 45f;
                SnapTurn(Mathf.Sign(x) * degrees);
            }

            var simulated = input as SimulatedRigInputProvider;

            // Hold both primary buttons (A + X): unstuck.
            bool unstuckHeld = (input.PrimaryButton(Hand.Left) && input.PrimaryButton(Hand.Right)) ||
                               (simulated != null && simulated.UnstuckHeld);
            unstuckTimer = unstuckHeld ? unstuckTimer + deltaTime : 0f;
            if (unstuckTimer >= holdSeconds)
            {
                unstuckTimer = float.NegativeInfinity;
                UnstuckNow();
            }

            // Hold both secondary buttons (B + Y): calibrate height and reach.
            bool calibrateHeld = (input.SecondaryButton(Hand.Left) && input.SecondaryButton(Hand.Right)) ||
                                 (simulated != null && simulated.CalibrateHeld);
            calibrateTimer = calibrateHeld ? calibrateTimer + deltaTime : 0f;
            if (calibrateTimer >= holdSeconds)
            {
                calibrateTimer = float.NegativeInfinity;
                CalibrateFromCurrentPose();
            }

            if (simulated != null && simulated.RespawnPressed) Respawn();
        }

        void PlaceHand(Hand hand, RigInputProvider input)
        {
            Transform target = HandTransform(hand);
            Transform controller = Controller(hand);
            if (target == null || controller == null) return;

            bool solved = mode == MovementMode.HandLocomotion && input.IsTracked(hand) && motor.GetHand(hand).seeded;
            Vector3 position = solved ? motor.HandPosition(hand) : controller.position;
            target.SetPositionAndRotation(position, controller.rotation);

            lateOffset[(int)hand] = position - controller.position;
            // A planted hand must stay planted; only free hands get the late pose update.
            lateFollow[(int)hand] = !solved || !motor.IsHandTouching(hand);
        }

        [BeforeRenderOrder(1000)]
        void OnBeforeRender()
        {
            RigInputProvider input = Input;
            if (input == null || input.DrivesPoses) return;
            for (int i = 0; i < 2; i++)
            {
                if (!lateFollow[i]) continue;
                Transform target = HandTransform((Hand)i);
                Transform controller = Controller((Hand)i);
                if (target == null || controller == null) continue;
                target.SetPositionAndRotation(controller.position + lateOffset[i], controller.rotation);
            }
        }

        // ------------------------------------------------------------------ Movement requests

        public void SetMode(MovementMode newMode)
        {
            if (mode == newMode) return;
            mode = newMode;
            if (mode != MovementMode.Seated) seat = null;
            motor.ResetState();
            ModeChanged?.Invoke(mode);
        }

        public void SnapTurn(float degrees)
        {
            transform.RotateAround(head.position, Vector3.up, degrees);
            if (mode == MovementMode.Seated && seat != null)
            {
                seatYawOffset += degrees;
                rigLocalAnchor = transform.InverseTransformPoint(seat.TransformPoint(seatLocalAnchor));
            }
            // Hands swing to new world positions; re-seed them so the swing is not a push.
            motor.ReseedHands();
            SnapTurned?.Invoke(degrees);
        }

        /// <summary>Moves the rig so the headset is at <paramref name="headPosition"/>, optionally facing a yaw.</summary>
        public void TeleportHead(Vector3 headPosition, float? yawDegrees = null)
        {
            if (yawDegrees.HasValue)
            {
                float current = BodyYaw.eulerAngles.y;
                transform.RotateAround(head.position, Vector3.up, Mathf.DeltaAngle(current, yawDegrees.Value));
            }
            transform.position += headPosition - head.position;
            motor.ResetState();
            Teleported?.Invoke();
        }

        /// <summary>Stands the player on a floor point.</summary>
        public void TeleportFeet(Vector3 floorPoint, float? yawDegrees = null)
        {
            float skin = motor.Settings != null ? motor.Settings.skinWidth : 0.01f;
            TeleportHead(floorPoint + Vector3.up * (motor.StandingLength + skin * 2f), yawDegrees);
        }

        public void SetSpawnPoint(Transform point) => spawnPoint = point;

        public void Respawn()
        {
            if (mode == MovementMode.Seated) SetMode(MovementMode.HandLocomotion);
            if (spawnPoint != null) TeleportFeet(spawnPoint.position, spawnPoint.eulerAngles.y);
            else TeleportFeet(Vector3.zero);
            motor.ClearSafePositions();
        }

        /// <summary>Returns the player to the last safe place they stood, or the spawn point.</summary>
        public void UnstuckNow()
        {
            if (mode == MovementMode.Seated) SetMode(MovementMode.HandLocomotion);
            if (motor.TryGetSafePosition(1f, out Vector3 safeHead))
            {
                TeleportHead(safeHead + Vector3.up * 0.03f);
            }
            else
            {
                Respawn();
            }
            Unstuck?.Invoke();
        }

        // ------------------------------------------------------------------ Seats

        /// <summary>
        /// Attaches the rig to a seat. The headset keeps its natural tracked
        /// motion relative to the seat; hand propulsion is off until ExitSeat.
        /// </summary>
        public void EnterSeat(Transform seatAnchor, Vector3 headAnchorWorld)
        {
            mode = MovementMode.Seated;
            seat = seatAnchor;
            transform.position += headAnchorWorld - head.position;
            // Glue one point of tracking space (where the head is now) to one point of the seat.
            seatLocalAnchor = seat.InverseTransformPoint(headAnchorWorld);
            rigLocalAnchor = transform.InverseTransformPoint(head.position);
            seatYawOffset = Mathf.DeltaAngle(seat.eulerAngles.y, transform.eulerAngles.y);
            motor.ResetState();
            ModeChanged?.Invoke(mode);
        }

        public void ExitSeat(Vector3 exitFloorPoint)
        {
            seat = null;
            mode = MovementMode.HandLocomotion;
            // Stay upright even if the vehicle was tilted.
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            TeleportFeet(exitFloorPoint);
            ModeChanged?.Invoke(mode);
        }

        void FollowSeat()
        {
            if (seat == null)
            {
                SetMode(MovementMode.HandLocomotion);
                return;
            }
            // Follow position and yaw only. Pitch and roll of the vehicle are never
            // applied to the view, which keeps the horizon stable.
            Quaternion yaw = Quaternion.Euler(0f, seat.eulerAngles.y + seatYawOffset, 0f);
            transform.SetPositionAndRotation(seat.TransformPoint(seatLocalAnchor) - yaw * rigLocalAnchor, yaw);
        }

        // ------------------------------------------------------------------ Tracking and calibration

        void OnTrackingOriginChanged()
        {
            // The runtime recentred. Hold the body where it is and re-seed the hands.
            if (motor != null) motor.ReseedHands();
        }

        /// <summary>
        /// Stand tall with arms relaxed at your sides, then call this. Body length
        /// is set so resting hands hover just above the ground, and reach is set
        /// from the measured arm length.
        /// </summary>
        public void CalibrateFromCurrentPose()
        {
            float headY = head.position.y;
            float handY = (leftController.position.y + rightController.position.y) * 0.5f;
            Vector3 shoulder = head.position + Vector3.down * motor.Settings.shoulderDrop;
            float arm = (Vector3.Distance(shoulder, leftController.position) + Vector3.Distance(shoulder, rightController.position)) * 0.5f;

            Calibration.standingLength = Mathf.Clamp(headY - handY + motor.Settings.handRadius + 0.06f, 0.5f, 1.2f);
            Calibration.armReach = Mathf.Clamp(arm * 1.12f, 0.5f, 1.15f);
            ApplyCalibration();
            Calibration.Save();
            Calibrated?.Invoke();
        }

        public void AdjustCalibration(float standingLengthDelta, float armReachDelta)
        {
            Calibration.standingLength = Mathf.Clamp(motor.StandingLength + standingLengthDelta, 0.5f, 1.2f);
            Calibration.armReach = Mathf.Clamp(motor.ArmReach + armReachDelta, 0.5f, 1.15f);
            ApplyCalibration();
            Calibration.Save();
            Calibrated?.Invoke();
        }

        public void ResetCalibration()
        {
            Calibration = new RigCalibration();
            ApplyCalibration();
            Calibration.Save();
            Calibrated?.Invoke();
        }

        void ApplyCalibration()
        {
            if (motor == null) return;
            motor.StandingLengthOverride = Calibration.standingLength;
            motor.ArmReachOverride = Calibration.armReach;
        }
    }
}
