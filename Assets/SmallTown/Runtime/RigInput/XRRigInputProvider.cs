using System;
using System.Collections.Generic;
using SmallTown.Locomotion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using Hand = SmallTown.Locomotion.Hand;

namespace SmallTown.RigInput
{
    /// <summary>
    /// Reads tracked controllers through the Input System. Bindings use control
    /// usages, so they resolve for any OpenXR controller profile (Touch, Touch
    /// Plus, Index, ...). Poses themselves are written to the rig transforms by
    /// TrackedPoseDriver components.
    /// </summary>
    public sealed class XRRigInputProvider : RigInputProvider
    {
        sealed class HandActions
        {
            public InputAction tracked;
            public InputAction grip;
            public InputAction trigger;
            public InputAction stick;
            public InputAction primary;
            public InputAction secondary;
        }

        static readonly List<XRInputSubsystem> Subsystems = new List<XRInputSubsystem>();

        readonly HandActions[] handActions = new HandActions[2];
        InputAction headTracked;
        InputAction menu;
        readonly List<XRInputSubsystem> hooked = new List<XRInputSubsystem>();

        /// <summary>Raised when the runtime recentres or changes its tracking origin.</summary>
        public event Action TrackingOriginChanged;

        public override bool IsAvailable
        {
            get
            {
                if (UnityEngine.InputSystem.InputSystem.GetDevice<UnityEngine.InputSystem.XR.XRHMD>() != null) return true;
                return XRSettings.isDeviceActive;
            }
        }

        public override bool HeadTracked => headTracked != null && headTracked.ReadValue<float>() > 0.5f;
        public override bool IsTracked(Hand hand) => handActions[(int)hand]?.tracked.ReadValue<float>() > 0.5f;
        public override float Grip(Hand hand) => handActions[(int)hand]?.grip.ReadValue<float>() ?? 0f;
        public override float Trigger(Hand hand) => handActions[(int)hand]?.trigger.ReadValue<float>() ?? 0f;
        public override Vector2 Thumbstick(Hand hand) => handActions[(int)hand]?.stick.ReadValue<Vector2>() ?? Vector2.zero;
        public override bool PrimaryButton(Hand hand) => handActions[(int)hand]?.primary.IsPressed() ?? false;
        public override bool SecondaryButton(Hand hand) => handActions[(int)hand]?.secondary.IsPressed() ?? false;
        public override bool MenuButton => menu != null && menu.IsPressed();

        void Awake()
        {
            headTracked = Button("HeadTracked", "<XRHMD>/isTracked");
            menu = Button("Menu", "<XRController>{LeftHand}/{MenuButton}");
            handActions[0] = Build("{LeftHand}", "Left");
            handActions[1] = Build("{RightHand}", "Right");
        }

        static HandActions Build(string usage, string label)
        {
            string device = "<XRController>" + usage + "/";
            return new HandActions
            {
                tracked = Button(label + "Tracked", device + "isTracked"),
                grip = Axis(label + "Grip", device + "{Grip}"),
                trigger = Axis(label + "Trigger", device + "{Trigger}"),
                stick = new InputAction(label + "Stick", InputActionType.Value, device + "{Primary2DAxis}", expectedControlType: "Vector2"),
                primary = Button(label + "Primary", device + "{PrimaryButton}"),
                secondary = Button(label + "Secondary", device + "{SecondaryButton}"),
            };
        }

        static InputAction Button(string name, string binding) =>
            new InputAction(name, InputActionType.Button, binding);

        static InputAction Axis(string name, string binding) =>
            new InputAction(name, InputActionType.Value, binding, expectedControlType: "Axis");

        void OnEnable()
        {
            headTracked.Enable();
            menu.Enable();
            foreach (var hand in handActions) SetEnabled(hand, true);
            HookSubsystems();
        }

        void OnDisable()
        {
            headTracked.Disable();
            menu.Disable();
            foreach (var hand in handActions) SetEnabled(hand, false);
            foreach (var subsystem in hooked) subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            hooked.Clear();
        }

        void OnDestroy()
        {
            headTracked?.Dispose();
            menu?.Dispose();
            foreach (var hand in handActions)
            {
                if (hand == null) continue;
                hand.tracked.Dispose();
                hand.grip.Dispose();
                hand.trigger.Dispose();
                hand.stick.Dispose();
                hand.primary.Dispose();
                hand.secondary.Dispose();
            }
        }

        static void SetEnabled(HandActions hand, bool enabled)
        {
            if (hand == null) return;
            if (enabled)
            {
                hand.tracked.Enable();
                hand.grip.Enable();
                hand.trigger.Enable();
                hand.stick.Enable();
                hand.primary.Enable();
                hand.secondary.Enable();
            }
            else
            {
                hand.tracked.Disable();
                hand.grip.Disable();
                hand.trigger.Disable();
                hand.stick.Disable();
                hand.primary.Disable();
                hand.secondary.Disable();
            }
        }

        public override void UpdatePoses(PlayerRig rig, float deltaTime)
        {
            // The XR subsystem can start after this component; keep the hook current.
            if (hooked.Count == 0) HookSubsystems();
        }

        void HookSubsystems()
        {
            SubsystemManager.GetSubsystems(Subsystems);
            foreach (var subsystem in Subsystems)
            {
                if (hooked.Contains(subsystem)) continue;
                subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                hooked.Add(subsystem);
            }
        }

        void OnTrackingOriginUpdated(XRInputSubsystem subsystem) => TrackingOriginChanged?.Invoke();

        public override void Haptic(Hand hand, float amplitude, float duration)
        {
            var device = InputDevices.GetDeviceAtXRNode(hand == Hand.Left ? XRNode.LeftHand : XRNode.RightHand);
            if (device.isValid) device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
        }
    }
}
