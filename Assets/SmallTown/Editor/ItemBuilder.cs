using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using SmallTown.Jobs;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SmallTown.EditorTools
{
    /// <summary>Builds the gameplay item prefabs: cash, shop goods, pizza parts and the pistol.</summary>
    public static class ItemBuilder
    {
        public const string Folder = ContentBuilder.PrefabFolder + "/Items";
        public const string BalancePath = ProjectSetup.SettingsFolder + "/GameBalance.asset";

        public static GameBalance EnsureBalance()
        {
            var balance = AssetDatabase.LoadAssetAtPath<GameBalance>(BalancePath);
            if (balance == null)
            {
                ProjectSetup.EnsureFolder(ProjectSetup.SettingsFolder);
                balance = ScriptableObject.CreateInstance<GameBalance>();
                AssetDatabase.CreateAsset(balance, BalancePath);
            }
            return balance;
        }

        static GameObject Save(GameObject root, string name)
        {
            ProjectSetup.EnsureFolder(Folder);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GrabbableItem MakeItem(GameObject go, string id, string displayName, float mass, int storageSize = 1)
        {
            GameLayers.SetLayerRecursively(go, GameLayers.Item);
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.angularDamping = 0.6f;
            var item = go.AddComponent<GrabbableItem>();
            item.itemId = id;
            item.displayName = displayName;
            item.storageSize = storageSize;
            return item;
        }

        public static LootItem BuildCash()
        {
            GameObject root = ContentBuilder.Box("Cash", null, Vector3.zero, new Vector3(0.14f, 0.05f, 0.08f),
                ContentBuilder.Mat("CashGreen", ContentBuilder.Hex("6FBF73")), true, GameLayers.Item);
            ContentBuilder.Box("Band", root.transform, Vector3.zero, new Vector3(0.25f, 1.06f, 1.06f),
                ContentBuilder.Mat("CashBand", ContentBuilder.Hex("F2E3C6")), false, GameLayers.Item);
            MakeItem(root, "cash", "Cash", 0.3f);
            var loot = root.AddComponent<LootItem>();
            loot.definitionId = "cash";
            loot.displayName = "Cash bundle";
            loot.isCash = true;
            loot.stolen = true;
            return Save(root, "Cash").GetComponent<LootItem>();
        }

        public static LootItem[] BuildMerchandise(GameBalance balance)
        {
            var result = new LootItem[balance.merchandise.Length];
            for (int i = 0; i < balance.merchandise.Length; i++)
            {
                MerchandiseType type = balance.merchandise[i];
                GameObject root = ContentBuilder.Box("Merch_" + type.id, null, Vector3.zero, type.size,
                    ContentBuilder.Mat("Merch_" + type.id, type.colour), true, GameLayers.Item);
                ContentBuilder.Box("Label", root.transform, new Vector3(0f, 0f, -0.5f), new Vector3(0.7f, 0.45f, 0.06f),
                    ContentBuilder.Mat("MerchLabel", ContentBuilder.Hex("FFFFFF")), false, GameLayers.Item);
                MakeItem(root, "merch." + type.id, type.displayName, 0.25f);
                var loot = root.AddComponent<LootItem>();
                loot.definitionId = "merch." + type.id;
                loot.displayName = type.displayName;
                loot.value = type.value;
                result[i] = Save(root, "Merch_" + type.id).GetComponent<LootItem>();
            }
            return result;
        }

        public static Topping BuildTopping(string id, string displayName, Color colour)
        {
            GameObject root = ContentBuilder.Box("Topping_" + id, null, Vector3.zero, new Vector3(0.09f, 0.05f, 0.09f),
                ContentBuilder.Mat("Topping_" + id, colour), true, GameLayers.Item);
            var item = MakeItem(root, "topping." + id, displayName, 0.15f);
            item.storable = false;
            var topping = root.AddComponent<Topping>();
            topping.toppingId = id;
            topping.displayName = displayName;
            topping.colour = colour;
            return Save(root, "Topping_" + id).GetComponent<Topping>();
        }

        public static Pizza BuildPizza()
        {
            var root = new GameObject("Pizza") { layer = GameLayers.Item };
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.3f, 0.035f, 0.3f);
            collider.center = new Vector3(0f, 0.0175f, 0f);

            var open = new GameObject("Open");
            open.transform.SetParent(root.transform, false);
            Material raw = ContentBuilder.Mat("PizzaDoughRaw", ContentBuilder.Hex("F1DEB4"));
            Material cooked = ContentBuilder.Mat("PizzaDoughCooked", ContentBuilder.Hex("D9963F"));
            GameObject dough = ContentBuilder.Box("Dough", open.transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.3f, 0.02f, 0.3f), raw, false, GameLayers.Item);
            ContentBuilder.Box("Sauce", open.transform, new Vector3(0f, 0.022f, 0f), new Vector3(0.25f, 0.006f, 0.25f),
                ContentBuilder.Mat("PizzaSauce", ContentBuilder.Hex("C0392B")), false, GameLayers.Item);
            var chips = new GameObject("Toppings");
            chips.transform.SetParent(open.transform, false);
            chips.transform.localPosition = new Vector3(0f, 0.028f, 0f);

            var box = new GameObject("Box");
            box.transform.SetParent(root.transform, false);
            Material card = ContentBuilder.Mat("PizzaBoxCard", ContentBuilder.Hex("E7D3A8"));
            ContentBuilder.Box("Card", box.transform, new Vector3(0f, 0.025f, 0f), new Vector3(0.34f, 0.05f, 0.34f), card, false, GameLayers.Item);
            ContentBuilder.Box("Stripe", box.transform, new Vector3(0f, 0.051f, 0f), new Vector3(0.2f, 0.004f, 0.2f),
                ContentBuilder.Mat("ButtonRed", ContentBuilder.Hex("E0453A")), false, GameLayers.Item);

            // The chip is a tiny shared cube that the pizza tints per topping.
            GameObject chip = ContentBuilder.Box("ToppingChip", null, Vector3.zero, new Vector3(0.035f, 0.01f, 0.035f),
                ContentBuilder.Mat("ToppingChip", Color.white), false, GameLayers.Item);
            GameObject chipPrefab = Save(chip, "ToppingChip");

            var item = MakeItem(root, "pizza", "Pizza", 0.5f, 2);
            item.storable = true;
            var pizza = root.AddComponent<Pizza>();
            pizza.Configure(dough.GetComponent<Renderer>(), raw, cooked, open, box, chips.transform, chipPrefab);
            return Save(root, "Pizza").GetComponent<Pizza>();
        }

        public static Pistol BuildPistol()
        {
            var root = new GameObject("Pistol") { layer = GameLayers.Item };
            Material body = ContentBuilder.Mat("PistolBody", ContentBuilder.Hex("3A3D46"));
            Material accent = ContentBuilder.Mat("PistolAccent", ContentBuilder.Hex("F28C28"));
            // Barrel along +Z, grip below and behind it. Toy colours on purpose.
            GameObject slide = ContentBuilder.Box("Slide", root.transform, new Vector3(0f, 0.04f, 0.06f), new Vector3(0.045f, 0.055f, 0.2f), body, true, GameLayers.Item);
            ContentBuilder.Box("Grip", root.transform, new Vector3(0f, -0.03f, -0.02f), new Vector3(0.04f, 0.11f, 0.055f), accent, true, GameLayers.Item)
                .transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            ContentBuilder.Box("Tip", root.transform, new Vector3(0f, 0.04f, 0.165f), new Vector3(0.05f, 0.06f, 0.02f), accent, false, GameLayers.Item);
            ContentBuilder.Box("Guard", root.transform, new Vector3(0f, -0.005f, 0.03f), new Vector3(0.012f, 0.012f, 0.07f), body, false, GameLayers.Item);

            var grip = new GameObject("GripPose");
            grip.transform.SetParent(root.transform, false);
            grip.transform.localPosition = new Vector3(0f, -0.02f, -0.02f);

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(root.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.04f, 0.18f);

            GameObject flash = ContentBuilder.Box("Flash", muzzle.transform, new Vector3(0f, 0f, 0.04f), new Vector3(0.07f, 0.07f, 0.07f),
                ContentBuilder.Mat("MuzzleFlash", ContentBuilder.Hex("FFE066")), false, GameLayers.Item);
            flash.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);

            var tracerObject = new GameObject("Tracer");
            tracerObject.transform.SetParent(root.transform, false);
            var tracer = tracerObject.AddComponent<LineRenderer>();
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.startWidth = 0.012f;
            tracer.endWidth = 0.006f;
            tracer.sharedMaterial = ContentBuilder.UnlitMaterial("TracerYellow", ContentBuilder.Hex("FFE066"), false);
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.enabled = false;

            TextMeshPro ammo = ContentBuilder.Label("6", root.transform, new Vector3(0f, 0.04f, -0.045f), new Vector2(0.06f, 0.05f), 0.4f, Color.white);

            GrabbableItem item = MakeItem(root, "pistol", "Pistol", 0.6f);
            item.snapPose = grip.transform;
            var pistol = root.AddComponent<Pistol>();
            pistol.Configure(muzzle.transform, tracer, flash.transform, ammo);
            return Save(root, "Pistol").GetComponent<Pistol>();
        }
    }
}
