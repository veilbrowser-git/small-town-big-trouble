using System.Collections.Generic;
using SmallTown.Jobs;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SmallTown.Networking
{
    /// <summary>
    /// Keeps a pizza looking the same for everyone. Toppings are decided on the
    /// host: a client that presses a topping onto the base asks, and the host
    /// checks the topping really is next to the pizza before adding it.
    /// </summary>
    [RequireComponent(typeof(Pizza))]
    public sealed class NetPizza : NetworkBehaviour
    {
        readonly NetworkVariable<FixedString128Bytes> toppings = new NetworkVariable<FixedString128Bytes>();
        readonly NetworkVariable<bool> cooked = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> boxed = new NetworkVariable<bool>();
        readonly List<Color> colours = new List<Color>();
        Pizza pizza;

        void Awake() => pizza = GetComponent<Pizza>();

        public override void OnNetworkSpawn()
        {
            pizza.ToppingRelay = RelayTopping;
            if (IsServer)
            {
                pizza.Changed += Push;
                Push(pizza);
                return;
            }
            toppings.OnValueChanged += (_, __) => Apply();
            cooked.OnValueChanged += (_, __) => Apply();
            boxed.OnValueChanged += (_, __) => Apply();
            Apply();
        }

        public override void OnNetworkDespawn()
        {
            if (pizza != null)
            {
                pizza.Changed -= Push;
                pizza.ToppingRelay = null;
            }
        }

        bool RelayTopping(Topping topping)
        {
            if (!IsSpawned || IsServer) return false;
            var networkObject = topping.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned) AddToppingRpc(networkObject);
            return true;
        }

        [Rpc(SendTo.Server)]
        void AddToppingRpc(NetworkObjectReference toppingReference)
        {
            if (!toppingReference.TryGet(out NetworkObject networkObject)) return;
            var topping = networkObject.GetComponent<Topping>();
            if (topping == null) return;
            // Believe the request only if the topping is actually at the pizza.
            if ((topping.transform.position - transform.position).sqrMagnitude > 0.7f * 0.7f) return;
            pizza.TryAdd(topping);
        }

        void Push(Pizza source)
        {
            var text = new FixedString128Bytes();
            foreach (Color colour in source.ToppingColours)
            {
                if (text.Length > 0) text.Append(',');
                text.Append(ColorUtility.ToHtmlStringRGB(colour));
            }
            if (toppings.Value != text) toppings.Value = text;
            if (cooked.Value != source.Cooked) cooked.Value = source.Cooked;
            if (boxed.Value != source.Boxed) boxed.Value = source.Boxed;
        }

        void Apply()
        {
            colours.Clear();
            string text = toppings.Value.ToString();
            if (text.Length > 0)
            {
                foreach (string hex in text.Split(','))
                {
                    if (ColorUtility.TryParseHtmlString("#" + hex, out Color colour)) colours.Add(colour);
                }
            }
            pizza.ApplyRemote(colours, cooked.Value, boxed.Value);
        }
    }
}
