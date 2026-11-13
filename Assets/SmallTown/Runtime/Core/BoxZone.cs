using UnityEngine;

namespace SmallTown
{
    /// <summary>
    /// A box-shaped region tested by position. Gameplay zones poll with this
    /// instead of relying on trigger callbacks, so an item carried inside a bag
    /// (colliders off) is still found, and tests need no physics stepping.
    /// </summary>
    public sealed class BoxZone : MonoBehaviour
    {
        public Vector3 size = Vector3.one;

        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= size.x * 0.5f &&
                   Mathf.Abs(local.y) <= size.y * 0.5f &&
                   Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
