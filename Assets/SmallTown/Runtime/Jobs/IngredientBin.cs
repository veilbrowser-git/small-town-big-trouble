using System.Collections.Generic;
using SmallTown.Interaction;
using UnityEngine;

namespace SmallTown.Jobs
{
    /// <summary>Keeps one topping available at an ingredient bin and tidies up extras left lying around.</summary>
    public sealed class IngredientBin : MonoBehaviour
    {
        [SerializeField] Topping prefab;
        [Min(0.1f)] public float respawnSeconds = 1f;
        [Min(1)] public int maxLoose = 4;

        readonly List<Topping> spawned = new List<Topping>();
        Topping current;
        float respawnAt;

        public Topping Current => current;

        public void Configure(Topping toppingPrefab) => prefab = toppingPrefab;

        void Start() => Spawn();

        void Update()
        {
            if (prefab == null) return;
            bool taken = current == null || (current.transform.position - transform.position).sqrMagnitude > 0.2f * 0.2f;
            if (!taken)
            {
                respawnAt = Time.time + respawnSeconds;
                return;
            }
            if (Time.time >= respawnAt) Spawn();
        }

        public Topping Spawn()
        {
            if (prefab == null) return null;
            spawned.RemoveAll(t => t == null);
            while (spawned.Count >= maxLoose)
            {
                Topping oldest = spawned[0];
                spawned.RemoveAt(0);
                var item = oldest.GetComponent<GrabbableItem>();
                if (item.State == ItemState.Free) Destroy(oldest.gameObject);
            }
            current = Instantiate(prefab, transform.position, transform.rotation);
            spawned.Add(current);
            respawnAt = Time.time + respawnSeconds;
            return current;
        }
    }
}
