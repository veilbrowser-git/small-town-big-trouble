using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown
{
    /// <summary>Falling in the sea drops the player back on the dock instead of the kill plane.</summary>
    [RequireComponent(typeof(BoxZone))]
    public sealed class WaterZone : MonoBehaviour
    {
        [SerializeField] Transform respawn;
        BoxZone zone;

        public void Configure(Transform respawnPoint) => respawn = respawnPoint;

        void Awake() => zone = GetComponent<BoxZone>();

        void Update()
        {
            GameSession session = GameSession.Instance;
            if (session == null || respawn == null) return;
            PlayerRig rig = session.Rig;
            if (rig.Mode != MovementMode.HandLocomotion || !zone.Contains(rig.Motor.BodyBottom)) return;
            GameFeedback.Play(FeedbackSound.Whoosh, rig.Head.position);
            rig.TeleportFeet(respawn.position, respawn.eulerAngles.y);
            HudToast.Show("Splash! Someone fished you out at the dock.");
        }
    }
}
