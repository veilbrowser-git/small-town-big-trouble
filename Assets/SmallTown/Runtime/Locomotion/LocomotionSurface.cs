using System.Collections.Generic;
using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>
    /// Optional per-object overrides for how hands grip a surface. Applies to
    /// every collider on this object and its children.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocomotionSurface : MonoBehaviour
    {
        [Tooltip("Fully grippable at any angle. Pair with the climbable visual cue (yellow hazard stripes).")]
        public bool climbable;

        public bool overrideFriction;
        [Range(0f, 1f)] public float friction = 1f;

        static readonly Dictionary<Collider, LocomotionSurface> Cache = new Dictionary<Collider, LocomotionSurface>();

        public static LocomotionSurface Find(Collider collider)
        {
            if (collider == null) return null;
            if (Cache.TryGetValue(collider, out var cached))
            {
                // Destroyed components compare equal to null; fall through to refresh.
                if (cached != null || ReferenceEquals(cached, null)) return cached;
            }
            var found = collider.GetComponentInParent<LocomotionSurface>();
            Cache[collider] = found;
            return found;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => Cache.Clear();

        void OnEnable() => Cache.Clear();
        void OnDisable() => Cache.Clear();
    }
}
