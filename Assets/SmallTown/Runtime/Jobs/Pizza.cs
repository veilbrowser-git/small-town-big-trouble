using System.Collections.Generic;
using SmallTown.Interaction;
using UnityEngine;

namespace SmallTown.Jobs
{
    /// <summary>
    /// The pizza as it moves through the job: raw base, topped, cooked, boxed.
    /// One object the whole way, so there is exactly one thing to carry, lose
    /// and recover.
    /// </summary>
    [RequireComponent(typeof(GrabbableItem))]
    public sealed class Pizza : MonoBehaviour
    {
        [SerializeField] Renderer doughRenderer;
        [SerializeField] Material rawMaterial;
        [SerializeField] Material cookedMaterial;
        [SerializeField] GameObject openVisual;
        [SerializeField] GameObject boxVisual;
        [SerializeField] Transform toppingRoot;
        [SerializeField] GameObject toppingChipPrefab;

        readonly List<string> toppings = new List<string>();

        /// <summary>Toppings this order needs. Anything else is refused.</summary>
        public readonly List<string> Required = new List<string>();

        public bool Cooked { get; private set; }
        public bool Boxed { get; private set; }
        public GrabbableItem Item { get; private set; }
        public IReadOnlyList<string> Toppings => toppings;

        public bool IsFullyTopped
        {
            get
            {
                foreach (string needed in Required)
                {
                    if (!toppings.Contains(needed)) return false;
                }
                return Required.Count > 0;
            }
        }

        public void Configure(Renderer dough, Material raw, Material cooked, GameObject open, GameObject box, Transform chips, GameObject chipPrefab)
        {
            doughRenderer = dough;
            rawMaterial = raw;
            cookedMaterial = cooked;
            openVisual = open;
            boxVisual = box;
            toppingRoot = chips;
            toppingChipPrefab = chipPrefab;
        }

        void Awake()
        {
            Item = GetComponent<GrabbableItem>();
            if (boxVisual != null) boxVisual.SetActive(false);
        }

        void OnCollisionEnter(Collision collision)
        {
            var topping = collision.rigidbody != null ? collision.rigidbody.GetComponent<Topping>() : null;
            if (topping != null) TryAdd(topping);
        }

        /// <summary>Adds a topping if the order wants it and it is not on already. The topping item is consumed.</summary>
        public bool TryAdd(Topping topping)
        {
            if (topping == null || Cooked || Boxed) return false;
            if (!Required.Contains(topping.toppingId) || toppings.Contains(topping.toppingId)) return false;

            toppings.Add(topping.toppingId);
            AddChips(topping.colour, toppings.Count);
            GameFeedback.Play(FeedbackSound.Store, transform.position, 0.7f, 1.2f);

            var item = topping.GetComponent<GrabbableItem>();
            item.ForceReleaseAll();
            Destroy(topping.gameObject);
            return true;
        }

        void AddChips(Color colour, int ring)
        {
            if (toppingRoot == null || toppingChipPrefab == null) return;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", colour);
            int count = 5;
            for (int i = 0; i < count; i++)
            {
                float angle = (i + ring * 0.37f) * Mathf.PI * 2f / count;
                float radius = 0.045f + 0.03f * (ring % 2);
                GameObject chip = Instantiate(toppingChipPrefab, toppingRoot);
                chip.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, 0.004f * ring, Mathf.Sin(angle) * radius);
                chip.transform.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
                var chipRenderer = chip.GetComponent<Renderer>();
                if (chipRenderer != null) chipRenderer.SetPropertyBlock(block);
            }
        }

        public void SetCooked()
        {
            Cooked = true;
            if (doughRenderer != null && cookedMaterial != null) doughRenderer.sharedMaterial = cookedMaterial;
        }

        public void SetBoxed()
        {
            Boxed = true;
            if (openVisual != null) openVisual.SetActive(false);
            if (boxVisual != null) boxVisual.SetActive(true);
            Item.storageSize = 2;
        }
    }
}
