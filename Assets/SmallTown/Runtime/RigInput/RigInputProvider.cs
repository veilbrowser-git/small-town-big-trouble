using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown.RigInput
{
    /// <summary>
    /// Source of controller state for a <see cref="PlayerRig"/>. Poses are read
    /// from the rig's head and controller transforms; a provider either lets a
    /// TrackedPoseDriver write them (XR) or writes them itself (simulated).
    /// </summary>
    public abstract class RigInputProvider : MonoBehaviour
    {
        /// <summary>True when this provider has a real source and should be used.</summary>
        public abstract bool IsAvailable { get; }

        /// <summary>True when this provider writes the head and controller transforms itself.</summary>
        public virtual bool DrivesPoses => false;

        public abstract bool HeadTracked { get; }
        public abstract bool IsTracked(Hand hand);
        public abstract float Grip(Hand hand);
        public abstract float Trigger(Hand hand);
        public abstract Vector2 Thumbstick(Hand hand);
        public abstract bool PrimaryButton(Hand hand);
        public abstract bool SecondaryButton(Hand hand);
        public abstract bool MenuButton { get; }

        public virtual void Haptic(Hand hand, float amplitude, float duration) { }

        /// <summary>Called once per frame before locomotion, for providers that write poses.</summary>
        public virtual void UpdatePoses(PlayerRig rig, float deltaTime) { }

        public virtual void OnSelected(PlayerRig rig) { }
        public virtual void OnDeselected(PlayerRig rig) { }
    }
}
