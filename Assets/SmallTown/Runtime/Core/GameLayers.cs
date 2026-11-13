using UnityEngine;

namespace SmallTown
{
    /// <summary>
    /// Physics layers used by the game. Indices are fixed here and written into
    /// the project's TagManager by the editor setup, so code and project
    /// settings cannot drift apart.
    /// </summary>
    public static class GameLayers
    {
        public const int World = 0;
        public const int PlayerBody = 6;
        public const int PlayerHand = 7;
        public const int Item = 8;
        public const int HeldItem = 9;
        public const int Vehicle = 10;
        public const int Npc = 11;
        public const int Interactable = 12;
        public const int RemotePlayer = 13;

        public static readonly (int index, string name)[] Names =
        {
            (PlayerBody, "PlayerBody"),
            (PlayerHand, "PlayerHand"),
            (Item, "Item"),
            (HeldItem, "HeldItem"),
            (Vehicle, "Vehicle"),
            (Npc, "Npc"),
            (Interactable, "Interactable"),
            (RemotePlayer, "RemotePlayer"),
        };

        /// <summary>
        /// What hands can push against and the body collides with. Loose and held
        /// items are deliberately absent: nobody flies by pushing on something
        /// they, or a friend, are carrying.
        /// </summary>
        public const int LocomotionSurfaces = (1 << World) | (1 << Vehicle);

        /// <summary>What a hand can grab.</summary>
        public const int Grabbable = (1 << Item) | (1 << HeldItem) | (1 << Interactable);

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
