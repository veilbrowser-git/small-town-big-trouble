using System.Collections.Generic;
using SmallTown.Economy;
using SmallTown.Interaction;
using UnityEngine;

namespace SmallTown.Items
{
    /// <summary>
    /// Makes purchased equipment exist in the world: a player's pistol appears
    /// at the shop when bought and at home on later sessions, and comes back
    /// home if it is ever lost. Each player who owns one gets their own.
    /// Runs on the host.
    /// </summary>
    public sealed class OwnedEquipment : MonoBehaviour
    {
        [SerializeField] Pistol pistolPrefab;
        [SerializeField] Transform homePoint;
        [SerializeField] Transform shopPoint;
        public float lostBelowY = -0.9f;

        readonly Dictionary<PlayerState, Pistol> pistols = new Dictionary<PlayerState, Pistol>();
        readonly List<PlayerState> scratch = new List<PlayerState>();
        float checkAt;

        /// <summary>The local player's pistol, if they own one and it exists.</summary>
        public Pistol PistolInstance
        {
            get
            {
                GameSession session = GameSession.Instance;
                if (session == null) return null;
                pistols.TryGetValue(session.Local, out Pistol pistol);
                return pistol;
            }
        }

        public void Configure(Pistol prefab, Transform home, Transform shop)
        {
            pistolPrefab = prefab;
            homePoint = home;
            shopPoint = shop;
        }

        /// <summary>Wired to the pistol offer's purchased event. The buyer is the session's current actor.</summary>
        public void OnPistolPurchased()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;
            PlayerState buyer = session.Actor;
            if (!pistols.TryGetValue(buyer, out Pistol existing) || existing == null) SpawnFor(buyer, shopPoint != null ? shopPoint : homePoint);
        }

        void SpawnFor(PlayerState player, Transform point)
        {
            if (pistolPrefab == null || point == null) return;
            // Side by side so four players' pistols do not spawn inside each other.
            Vector3 position = point.position + Vector3.up * 0.06f + point.right * (player.Index * 0.22f);
            pistols[player] = Net.Spawn(pistolPrefab, position, point.rotation);
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            if (session == null || !Net.IsHost || Time.time < checkAt) return;
            checkAt = Time.time + 1f;

            // Players who left take their pistol with them.
            scratch.Clear();
            foreach (PlayerState owner in pistols.Keys)
            {
                bool present = false;
                foreach (PlayerState player in session.Players) present |= player == owner;
                if (!present) scratch.Add(owner);
            }
            foreach (PlayerState gone in scratch)
            {
                if (pistols[gone] != null) Net.Despawn(pistols[gone].gameObject);
                pistols.Remove(gone);
            }

            foreach (PlayerState player in session.Players)
            {
                if (!player.Wallet.Owns(GameBalance.PistolUnlock)) continue;
                pistols.TryGetValue(player, out Pistol pistol);
                if (pistol != null && pistol.transform.position.y >= lostBelowY) continue;

                bool lost = pistol != null;
                if (lost)
                {
                    pistol.GetComponent<GrabbableItem>().ForceReleaseAll();
                    Net.Despawn(pistol.gameObject);
                }
                SpawnFor(player, homePoint);
                if (lost) player.Tell("Your pistol was lost. It is back on the table in your apartment.");
            }
        }
    }
}
