using UnityEngine;

namespace SmallTown.Avatar
{
    /// <summary>
    /// Two-bone arm posing shared by the player avatar and NPCs. Segment meshes
    /// are unit length along +Z and get stretched between the joints.
    /// </summary>
    public static class ArmSolver
    {
        public static void Solve(Vector3 shoulder, Vector3 hand, float segment, Quaternion facing, float side,
            Transform upper, Transform lower, float thickness)
        {
            Vector3 toHand = hand - shoulder;
            float distance = Mathf.Max(0.02f, toHand.magnitude);
            Vector3 direction = toHand / distance;

            // Two equal bones: the elbow sits off the shoulder-hand line by whatever
            // slack the arm has, pushed outward and back so elbows flare like a cartoon.
            float half = Mathf.Min(distance * 0.5f, segment);
            float bend = Mathf.Sqrt(Mathf.Max(0f, segment * segment - half * half));
            Vector3 pole = facing * new Vector3(side * 0.8f, -0.25f, -0.6f);
            Vector3 bendDirection = Vector3.ProjectOnPlane(pole, direction);
            bendDirection = bendDirection.sqrMagnitude < 1e-5f ? Vector3.up : bendDirection.normalized;
            Vector3 elbow = shoulder + direction * half + bendDirection * bend;

            Place(upper, shoulder, elbow, thickness, bendDirection);
            Place(lower, elbow, hand, thickness, bendDirection);
        }

        static void Place(Transform segment, Vector3 from, Vector3 to, float thickness, Vector3 up)
        {
            if (segment == null) return;
            Vector3 span = to - from;
            float length = span.magnitude;
            if (length < 1e-4f) return;
            Vector3 forward = span / length;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.98f) up = Vector3.up;
            segment.SetPositionAndRotation((from + to) * 0.5f, Quaternion.LookRotation(forward, up));
            Vector3 parentScale = segment.parent != null ? segment.parent.lossyScale : Vector3.one;
            segment.localScale = new Vector3(thickness / parentScale.x, thickness / parentScale.y, length / parentScale.z);
        }
    }
}
