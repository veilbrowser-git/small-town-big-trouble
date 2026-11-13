using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>Marks where the player stands when a scene starts or they respawn. Place it on the floor.</summary>
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.4f, 0.2f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.4f, transform.forward * 0.5f);
        }
    }
}
