using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>
    /// Kinematic physics stand-ins for the player's body and hands. They let the
    /// player shove props and enter triggers, and double as the shapes the
    /// solver uses for penetration tests. They are detached from the rig at
    /// runtime and follow it in FixedUpdate so pushes look physical.
    /// </summary>
    public sealed class RigPhysicsProxies : MonoBehaviour
    {
        [SerializeField] PlayerRig rig;
        [SerializeField] Rigidbody body;
        [SerializeField] Rigidbody leftHand;
        [SerializeField] Rigidbody rightHand;

        public PlayerRig Rig => rig;
        public Rigidbody Body => body;
        public Rigidbody HandBody(Hand hand) => hand == Hand.Left ? leftHand : rightHand;

        public void Configure(PlayerRig owner, Rigidbody bodyProxy, Rigidbody left, Rigidbody right)
        {
            rig = owner;
            body = bodyProxy;
            leftHand = left;
            rightHand = right;
        }

        void Awake()
        {
            Detach(body);
            Detach(leftHand);
            Detach(rightHand);
        }

        static void Detach(Rigidbody target)
        {
            if (target == null) return;
            target.isKinematic = true;
            target.useGravity = false;
            target.transform.SetParent(null, true);
            target.transform.rotation = Quaternion.identity;
        }

        void OnDestroy()
        {
            if (body != null) Destroy(body.gameObject);
            if (leftHand != null) Destroy(leftHand.gameObject);
            if (rightHand != null) Destroy(rightHand.gameObject);
        }

        void FixedUpdate()
        {
            if (rig == null || rig.Motor == null || rig.Motor.Settings == null) return;
            if (body != null) body.MovePosition(rig.Motor.BodyCentre);
            Follow(leftHand, rig.HandTransform(Hand.Left));
            Follow(rightHand, rig.HandTransform(Hand.Right));
        }

        static void Follow(Rigidbody proxy, Transform target)
        {
            if (proxy == null || target == null) return;
            proxy.MovePosition(target.position);
            proxy.MoveRotation(target.rotation);
        }
    }
}
