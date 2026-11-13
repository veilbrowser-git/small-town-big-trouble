using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown.RigInput
{
    /// <summary>
    /// Input whose state is set directly by code. Automated tests use this to
    /// script exact controller motion; it is also the base of the keyboard and
    /// mouse simulator.
    /// </summary>
    public class ManualRigInputProvider : RigInputProvider
    {
        [Header("State (set from code)")]
        public bool headTracked = true;
        public bool leftTracked = true;
        public bool rightTracked = true;
        public float leftGrip;
        public float rightGrip;
        public float leftTrigger;
        public float rightTrigger;
        public Vector2 leftStick;
        public Vector2 rightStick;
        public bool leftPrimary;
        public bool rightPrimary;
        public bool leftSecondary;
        public bool rightSecondary;
        public bool menu;

        public int HapticCount { get; private set; }

        public override bool IsAvailable => isActiveAndEnabled;
        public override bool DrivesPoses => true;
        public override bool HeadTracked => headTracked;
        public override bool IsTracked(Hand hand) => hand == Hand.Left ? leftTracked : rightTracked;
        public override float Grip(Hand hand) => hand == Hand.Left ? leftGrip : rightGrip;
        public override float Trigger(Hand hand) => hand == Hand.Left ? leftTrigger : rightTrigger;
        public override Vector2 Thumbstick(Hand hand) => hand == Hand.Left ? leftStick : rightStick;
        public override bool PrimaryButton(Hand hand) => hand == Hand.Left ? leftPrimary : rightPrimary;
        public override bool SecondaryButton(Hand hand) => hand == Hand.Left ? leftSecondary : rightSecondary;
        public override bool MenuButton => menu;

        public override void Haptic(Hand hand, float amplitude, float duration) => HapticCount++;
    }
}
