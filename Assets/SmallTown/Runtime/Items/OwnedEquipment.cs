using SmallTown.Economy;
using SmallTown.Interaction;
using UnityEngine;

namespace SmallTown.Items
{
    /// <summary>
    /// Makes purchased equipment exist in the world: the pistol appears at the
    /// shop when bought and at home on later sessions, and comes back home if
    /// it is ever lost.
    /// </summary>
    public sealed class OwnedEquipment : MonoBehaviour
    {
        [SerializeField] Pistol pistolPrefab;
        [SerializeField] Transform homePoint;
        [SerializeField] Transform shopPoint;
        public float lostBelowY = -0.9f;

        Pistol pistol;
        float checkAt;

        public Pistol PistolInstance => pistol;

        public void Configure(Pistol prefab, Transform home, Transform shop)
        {
            pistolPrefab = prefab;
            homePoint = home;
            shopPoint = shop;
        }

        void Start()
        {
            if (Owned) SpawnAt(homePoint);
        }

        bool Owned => GameSession.Instance != null && GameSession.Instance.Wallet.Owns(GameBalance.PistolUnlock);

        /// <summary>Wired to the pistol offer's purchased event.</summary>
        public void OnPistolPurchased()
        {
            if (pistol == null) SpawnAt(shopPoint != null ? shopPoint : homePoint);
        }

        void SpawnAt(Transform point)
        {
            if (pistolPrefab == null || point == null) return;
            pistol = Instantiate(pistolPrefab, point.position + Vector3.up * 0.06f, point.rotation);
        }

        void Update()
        {
            if (Time.time < checkAt) return;
            checkAt = Time.time + 1f;
            if (!Owned) return;
            if (pistol != null && pistol.transform.position.y >= lostBelowY) return;

            if (pistol != null)
            {
                pistol.GetComponent<GrabbableItem>().ForceReleaseAll();
                Destroy(pistol.gameObject);
            }
            SpawnAt(homePoint);
            HudToast.Show("Your pistol was lost. It is back on the table in your apartment.");
        }
    }
}
