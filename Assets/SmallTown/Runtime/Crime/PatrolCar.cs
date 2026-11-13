using UnityEngine;

namespace SmallTown.Crime
{
    /// <summary>
    /// The town's patrol car. It drives along the main street to wherever the
    /// suspect was last reported, lights flashing, then parks there as an
    /// obstacle. Kinematic and on rails: it never needs to path-find.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PatrolCar : MonoBehaviour
    {
        [SerializeField] Renderer[] lights = new Renderer[0];
        [Min(0.5f)] public float speed = 7f;
        [Tooltip("The car stays on the street between these X positions.")]
        public Vector2 streetRange = new Vector2(-19f, 19f);

        Rigidbody body;
        Vector3 home;
        Quaternion homeRotation;
        float targetX;
        bool responding;

        public bool Responding => responding;

        public void Configure(Renderer[] lightRenderers) => lights = lightRenderers;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            home = transform.position;
            homeRotation = transform.rotation;
            targetX = home.x;
            SetLights(false);
        }

        public void Dispatch(Vector3 lastKnownPosition)
        {
            responding = true;
            targetX = Mathf.Clamp(lastKnownPosition.x, streetRange.x, streetRange.y);
        }

        public void Return()
        {
            responding = false;
            targetX = home.x;
        }

        void FixedUpdate()
        {
            if (!Net.IsHost) return;
            Vector3 position = body.position;
            float delta = targetX - position.x;
            if (Mathf.Abs(delta) > 0.02f)
            {
                position.x += Mathf.Clamp(delta, -speed * Time.fixedDeltaTime, speed * Time.fixedDeltaTime);
                body.MovePosition(position);
                body.MoveRotation(Quaternion.Euler(0f, delta > 0f ? 90f : 270f, 0f));
            }
            else if (!responding)
            {
                body.MoveRotation(homeRotation);
            }
        }

        void Update()
        {
            // The light bar is switched on and off as an object so its state can be
            // copied to other players; the flashing itself runs on every machine.
            bool on = lights.Length > 0 && lights[0] != null && lights[0].gameObject.activeSelf;
            if (Net.IsHost && on != responding) SetLights(responding);
            if (!on) return;
            bool phase = Mathf.Repeat(Time.time * 3f, 1f) < 0.5f;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null) lights[i].enabled = (i % 2 == 0) == phase;
            }
        }

        void SetLights(bool on)
        {
            foreach (Renderer light in lights)
            {
                if (light != null) light.gameObject.SetActive(on);
            }
        }
    }
}
