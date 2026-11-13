using UnityEngine;

namespace SmallTown.Locomotion
{
    public enum Hand
    {
        Left = 0,
        Right = 1,
    }

    /// <summary>
    /// The collision-constrained state of one virtual hand. The controller pose
    /// is where the player's real hand is; <see cref="position"/> is where the
    /// virtual hand is allowed to be.
    /// </summary>
    public sealed class HandContact
    {
        public Vector3 position;
        public bool seeded;
        public bool touching;
        public Collider surface;
        public Vector3 normal;
        public Vector3 surfaceLocalPoint;
        public bool wasTracked;

        /// <summary>True while the hand is gripping a climbable surface: it holds in every direction, not just when pushed.</summary>
        public bool latched;

        /// <summary>True while this hand is allowed to support and propel the body.</summary>
        public bool canPush = true;

        public void ClearContact()
        {
            latched = false;
            touching = false;
            surface = null;
            normal = Vector3.zero;
        }

        public void RememberSurface()
        {
            if (touching && surface != null) surfaceLocalPoint = surface.transform.InverseTransformPoint(position);
        }

        /// <summary>How far the surface under this hand moved since the last tick.</summary>
        public Vector3 SurfaceDelta()
        {
            if (!touching || surface == null) return Vector3.zero;
            return surface.transform.TransformPoint(surfaceLocalPoint) - position;
        }
    }

    /// <summary>Sphere sweeps for virtual hands, with per-surface friction.</summary>
    public sealed class HandSweeper
    {
        public struct Result
        {
            public Vector3 position;
            public bool touching;
            public Vector3 normal;
            public Collider collider;
        }

        const int MaxIterations = 3;

        readonly Collider[] overlapBuffer = new Collider[12];

        public LayerMask Mask;
        public float Radius = 0.06f;
        public float Skin = 0.008f;
        public HandLocomotionSettings Settings;

        /// <summary>Sphere used only as the shape argument of ComputePenetration.</summary>
        public SphereCollider Proxy;

        float Friction(Vector3 normal, Collider collider, float frictionScale)
        {
            float friction = Settings != null
                ? Settings.FrictionFor(normal, LocomotionSurface.Find(collider)) * frictionScale
                : frictionScale;
            return Mathf.Clamp01(friction);
        }

        /// <summary>
        /// Looks for a surface within <paramref name="reach"/> of the hand sphere.
        /// <paramref name="clearance"/> is the gap between the sphere and that surface.
        /// </summary>
        public bool Probe(Vector3 position, float reach, bool climbableOnly, out Vector3 normal, out Collider collider, out float clearance)
        {
            normal = Vector3.zero;
            collider = null;
            clearance = reach;
            if (Proxy == null) return false;

            float probeRadius = Radius + reach;
            int hits = Physics.OverlapSphereNonAlloc(position, probeRadius, overlapBuffer, Mask, QueryTriggerInteraction.Ignore);
            if (hits == 0) return false;

            Proxy.center = Vector3.zero;
            Proxy.radius = probeRadius;
            float deepest = 0f;
            for (int i = 0; i < hits; i++)
            {
                Collider other = overlapBuffer[i];
                if (other == Proxy) continue;
                if (climbableOnly)
                {
                    LocomotionSurface surface = LocomotionSurface.Find(other);
                    if (surface == null || !surface.climbable) continue;
                }
                if (Physics.ComputePenetration(Proxy, position, Quaternion.identity,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float distance) && distance > deepest)
                {
                    deepest = distance;
                    normal = direction;
                    collider = other;
                }
            }
            Proxy.radius = Radius;
            if (collider == null) return false;
            clearance = reach - deepest;
            return true;
        }

        /// <param name="frictionScale">0 lets the hand glide along surfaces, 1 uses the surface's own friction.</param>
        public Result Sweep(Vector3 from, Vector3 to, float frictionScale)
        {
            var result = new Result { position = from };
            Vector3 remaining = to - from;

            // Resting contact. A hand sits a skin's width off the surface it is on, so a
            // mostly sideways drag would never "hit" that surface again. If the hand is
            // within the skin band and the motion leans into the surface at all, the
            // contact holds and friction applies to the sideways part.
            if (remaining.sqrMagnitude > 1e-12f &&
                Probe(result.position, Skin * 1.5f, false, out Vector3 contactNormal, out Collider contactCollider, out float clearance))
            {
                float into = -Vector3.Dot(remaining, contactNormal);
                if (into > 1e-4f)
                {
                    result.touching = true;
                    result.normal = contactNormal;
                    result.collider = contactCollider;
                    result.position += contactNormal * (Skin - clearance);
                    Vector3 tangential = remaining + contactNormal * into;
                    remaining = tangential * (1f - Friction(contactNormal, contactCollider, frictionScale));
                }
            }

            for (int i = 0; i < MaxIterations; i++)
            {
                float distance = remaining.magnitude;
                if (distance < 1e-6f) break;
                Vector3 direction = remaining / distance;

                if (!Physics.SphereCast(result.position, Radius, direction, out RaycastHit hit,
                        distance + Skin, Mask, QueryTriggerInteraction.Ignore))
                {
                    result.position += remaining;
                    break;
                }

                // Stop a skin's width from the surface measured along its normal. Backing
                // off along the motion instead lets shallow drags creep into the surface.
                float approach = -Vector3.Dot(direction, hit.normal);
                float backOff = approach > 1e-3f ? Skin / approach : hit.distance;
                float travel = Mathf.Max(0f, hit.distance - backOff);
                result.position += direction * travel;
                result.touching = true;
                result.normal = hit.normal;
                result.collider = hit.collider;

                Vector3 leftover = direction * (distance - travel);
                Vector3 tangential = Vector3.ProjectOnPlane(leftover, hit.normal);
                remaining = tangential * (1f - Friction(hit.normal, hit.collider, frictionScale));
            }
            return result;
        }

        /// <summary>Pushes an overlapping hand out of geometry, for example when a platform moves into it.</summary>
        public bool Depenetrate(ref Vector3 position, out Collider collider, out Vector3 normal)
        {
            collider = null;
            normal = Vector3.zero;
            if (Proxy == null) return false;

            int hits = Physics.OverlapSphereNonAlloc(position, Radius, overlapBuffer, Mask, QueryTriggerInteraction.Ignore);
            bool moved = false;
            Proxy.radius = Radius;
            Proxy.center = Vector3.zero;
            for (int i = 0; i < hits; i++)
            {
                Collider other = overlapBuffer[i];
                if (other == Proxy) continue;
                if (Physics.ComputePenetration(Proxy, position, Quaternion.identity,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float distance))
                {
                    position += direction * (distance + Skin);
                    collider = other;
                    normal = direction;
                    moved = true;
                }
            }
            return moved;
        }

        public bool Overlaps(Vector3 position)
        {
            return Physics.CheckSphere(position, Radius - Skin, Mask, QueryTriggerInteraction.Ignore);
        }
    }
}
