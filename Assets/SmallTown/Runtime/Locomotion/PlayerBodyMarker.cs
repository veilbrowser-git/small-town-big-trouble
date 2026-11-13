using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>Put on the body proxy so triggers can find the rig that walked into them.</summary>
    public sealed class PlayerBodyMarker : MonoBehaviour
    {
        public PlayerRig rig;
        public Hand hand;
        public bool isHand;

        public static PlayerRig RigFrom(Collider other)
        {
            if (other == null) return null;
            var marker = other.GetComponent<PlayerBodyMarker>();
            return marker != null ? marker.rig : null;
        }
    }
}
