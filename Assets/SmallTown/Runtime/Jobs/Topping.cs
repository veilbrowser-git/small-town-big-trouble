using System.Collections.Generic;
using SmallTown.Interaction;
using UnityEngine;

namespace SmallTown.Jobs
{
    /// <summary>A topping the player can press onto a pizza base.</summary>
    [RequireComponent(typeof(GrabbableItem))]
    public sealed class Topping : MonoBehaviour
    {
        public string toppingId = "cheese";
        public string displayName = "Cheese";
        public Color colour = Color.yellow;
    }
}
