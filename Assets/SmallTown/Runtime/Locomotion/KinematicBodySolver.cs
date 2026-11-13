using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>
    /// Collide-and-slide movement for the player's body: a vertical capsule that
    /// hangs from the headset. Pure physics queries, no rigidbody forces, so the
    /// result is deterministic for a given displacement.
    /// </summary>
    public sealed class KinematicBodySolver
    {
        public struct GroundInfo
        {
            public bool grounded;
            public bool walkable;
            public Vector3 normal;
            public Vector3 point;
            public Collider collider;
        }

        public struct MoveResult
        {
            public Vector3 head;
            public int contactCount;
            public Vector3 contactNormal0;
            public Vector3 contactNormal1;
            public bool stepped;
        }

        const int MaxSlideIterations = 4;
        const int MaxDepenetrationIterations = 4;

        readonly Collider[] overlapBuffer = new Collider[24];

        public LayerMask Mask;
        public float Radius = 0.17f;
        public float Skin = 0.008f;
        public float StepHeight = 0.22f;
        public float MaxGroundAngle = 50f;

        /// <summary>Capsule used only as the shape argument of ComputePenetration.</summary>
        public CapsuleCollider Proxy;

        // The head is the centre of the capsule's top sphere; the bottom tip is `length` below it.
        Vector3 Top(Vector3 head) => head;
        Vector3 Bottom(Vector3 head, float length) => head + Vector3.down * Mathf.Max(0f, length - Radius);

        public MoveResult Move(Vector3 head, float length, Vector3 displacement, bool allowStep)
        {
            var result = Slide(head, length, displacement);

            if (allowStep && StepHeight > 0f && result.contactCount > 0)
            {
                Vector3 horizontal = new Vector3(displacement.x, 0f, displacement.z);
                if (horizontal.sqrMagnitude > 1e-8f && BlockedBySteepSurface(result))
                {
                    var stepped = TryStep(head, length, displacement, horizontal);
                    Vector3 plain = result.head - head;
                    Vector3 withStep = stepped.head - head;
                    plain.y = 0f;
                    withStep.y = 0f;
                    if (stepped.stepped && withStep.sqrMagnitude > plain.sqrMagnitude + 1e-6f) result = stepped;
                }
            }
            return result;
        }

        bool BlockedBySteepSurface(MoveResult result)
        {
            float limit = Mathf.Cos(MaxGroundAngle * Mathf.Deg2Rad);
            if (result.contactNormal0.y < limit && result.contactNormal0.y > -0.1f) return true;
            return result.contactCount > 1 && result.contactNormal1.y < limit && result.contactNormal1.y > -0.1f;
        }

        MoveResult TryStep(Vector3 head, float length, Vector3 displacement, Vector3 horizontal)
        {
            // Up, across, then back down. Only accepted if we land on walkable ground.
            var up = Slide(head, length, Vector3.up * StepHeight);
            float raised = up.head.y - head.y;
            if (raised < 0.01f) return default;

            var across = Slide(up.head, length, horizontal);
            var down = Slide(across.head, length, Vector3.down * (raised + Skin));
            var ground = ProbeGround(down.head, length, Skin * 4f);

            var result = down;
            result.stepped = ground.grounded && ground.walkable;
            result.contactCount = 0;
            // Preserve any vertical intent (for example a jump that began at the kerb).
            if (displacement.y > 0f)
            {
                var lift = Slide(result.head, length, Vector3.up * displacement.y);
                result.head = lift.head;
            }
            return result;
        }

        MoveResult Slide(Vector3 head, float length, Vector3 displacement)
        {
            var result = new MoveResult { head = head };
            Vector3 remaining = displacement;
            Vector3 firstNormal = Vector3.zero;

            for (int i = 0; i < MaxSlideIterations; i++)
            {
                float distance = remaining.magnitude;
                if (distance < 1e-5f) break;
                Vector3 direction = remaining / distance;

                if (!Physics.CapsuleCast(Top(result.head), Bottom(result.head, length), Radius, direction,
                        out RaycastHit hit, distance + Skin, Mask, QueryTriggerInteraction.Ignore))
                {
                    result.head += remaining;
                    break;
                }

                // Keep a skin's width of clearance along the surface normal (see HandSweeper).
                float approach = -Vector3.Dot(direction, hit.normal);
                float backOff = approach > 1e-3f ? Skin / approach : hit.distance;
                float travel = Mathf.Max(0f, hit.distance - backOff);
                result.head += direction * travel;
                Vector3 leftover = direction * (distance - travel);

                if (result.contactCount == 0)
                {
                    firstNormal = hit.normal;
                    result.contactNormal0 = hit.normal;
                    remaining = Vector3.ProjectOnPlane(leftover, hit.normal);
                }
                else
                {
                    result.contactNormal1 = hit.normal;
                    // Second surface: only movement along the crease satisfies both.
                    Vector3 crease = Vector3.Cross(firstNormal, hit.normal);
                    if (crease.sqrMagnitude < 1e-6f)
                    {
                        remaining = Vector3.ProjectOnPlane(leftover, hit.normal);
                    }
                    else
                    {
                        crease.Normalize();
                        remaining = crease * Vector3.Dot(leftover, crease);
                    }
                }
                result.contactCount++;

                // Never slide back against the direction we were asked to go.
                if (Vector3.Dot(remaining, displacement) <= 0f) break;
            }
            return result;
        }

        /// <summary>Pushes the capsule out of anything it overlaps, such as a platform that moved into it.</summary>
        public Vector3 Depenetrate(Vector3 head, float length, out bool wasOverlapping)
        {
            wasOverlapping = false;
            if (Proxy == null) return head;

            float capsuleHeight = length + Radius;
            Proxy.radius = Radius;
            Proxy.height = capsuleHeight;
            Proxy.direction = 1;
            Proxy.center = Vector3.zero;

            for (int iteration = 0; iteration < MaxDepenetrationIterations; iteration++)
            {
                int hits = Physics.OverlapCapsuleNonAlloc(Top(head), Bottom(head, length), Radius,
                    overlapBuffer, Mask, QueryTriggerInteraction.Ignore);
                if (hits == 0) break;

                // Capsule spans head + Radius (top) to head - length (bottom).
                Vector3 centre = head + Vector3.up * (Radius - capsuleHeight * 0.5f);
                bool moved = false;
                for (int i = 0; i < hits; i++)
                {
                    Collider other = overlapBuffer[i];
                    if (other == Proxy) continue;
                    if (Physics.ComputePenetration(Proxy, centre, Quaternion.identity,
                            other, other.transform.position, other.transform.rotation,
                            out Vector3 direction, out float distance))
                    {
                        Vector3 push = direction * (distance + Skin * 0.5f);
                        head += push;
                        centre += push;
                        moved = true;
                        wasOverlapping = true;
                    }
                }
                if (!moved) break;
            }
            return head;
        }

        public GroundInfo ProbeGround(Vector3 head, float length, float probeDistance)
        {
            var info = new GroundInfo();
            Vector3 origin = Bottom(head, length) + Vector3.up * 0.02f;
            float probeRadius = Radius * 0.95f;
            if (Physics.SphereCast(origin, probeRadius, Vector3.down, out RaycastHit hit,
                    probeDistance + 0.02f + (Radius - probeRadius) + Skin, Mask, QueryTriggerInteraction.Ignore))
            {
                info.grounded = true;
                info.normal = hit.normal;
                info.point = hit.point;
                info.collider = hit.collider;
                info.walkable = Vector3.Angle(hit.normal, Vector3.up) <= MaxGroundAngle;
            }
            return info;
        }

        /// <summary>True if a body of the given length fits at this head position.</summary>
        public bool Fits(Vector3 head, float length)
        {
            return !Physics.CheckCapsule(Top(head), Bottom(head, length), Radius - Skin, Mask, QueryTriggerInteraction.Ignore);
        }
    }
}
