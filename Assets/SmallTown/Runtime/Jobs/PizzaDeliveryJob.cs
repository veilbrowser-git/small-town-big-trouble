using System.Collections.Generic;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Npc;
using TMPro;
using UnityEngine;

namespace SmallTown.Jobs
{
    /// <summary>
    /// Take an order, build the pizza from physical toppings, bake it, box it,
    /// carry it to the customer. Losing the pizza restarts the cooking steps
    /// with a fresh base; cancelling removes everything.
    /// </summary>
    public sealed class PizzaDeliveryJob : Job
    {
        public enum Stage
        {
            None,
            Assemble,
            Cook,
            Box,
            Deliver,
        }

        static readonly string[][] Recipes =
        {
            new[] { "cheese" },
            new[] { "cheese", "pepperoni" },
            new[] { "cheese", "mushroom" },
            new[] { "pepperoni", "mushroom" },
        };

        [SerializeField] Pizza pizzaPrefab;
        [SerializeField] Transform baseSpawn;
        [SerializeField] BoxZone oven;
        [SerializeField] BoxZone boxing;
        [SerializeField] DeliveryCustomer[] customers = new DeliveryCustomer[0];
        [SerializeField] TMP_Text orderLabel;
        [Tooltip("A pizza below this height is treated as lost (fell in the sea or out of the world).")]
        public float lostBelowY = -0.9f;

        Pizza pizza;
        DeliveryCustomer target;
        float cookTimer;
        float acceptedAt;
        int nextRecipe;
        int nextCustomer;

        public Stage Current { get; private set; }
        public Pizza ActivePizza => pizza;
        public DeliveryCustomer Target => target;
        public float CookProgress => GameSession.Instance != null ? Mathf.Clamp01(cookTimer / GameSession.Instance.Balance.pizzaCookSeconds) : 0f;

        public void Configure(Pizza prefab, Transform spawn, BoxZone ovenZone, BoxZone boxingZone, DeliveryCustomer[] dropOffs, TMP_Text label)
        {
            pizzaPrefab = prefab;
            baseSpawn = spawn;
            oven = ovenZone;
            boxing = boxingZone;
            customers = dropOffs;
            orderLabel = label;
        }

        void Start() => RefreshLabel();

        /// <summary>Wired to the TAKE ORDER button.</summary>
        public void TakeOrder()
        {
            if (!Accept() && State == JobState.Available) HudToast.Show("Finish or cancel your current job first.");
        }

        /// <summary>Wired to the CANCEL button.</summary>
        public void CancelOrder() => Cancel();

        protected override void OnAccepted()
        {
            target = customers.Length > 0 ? customers[nextCustomer++ % customers.Length] : null;
            acceptedAt = Time.time;
            StartAssembly(Recipes[nextRecipe++ % Recipes.Length]);
            HudToast.Show("Order up! " + RecipeText() + " for " + (target != null ? target.placeName : "a customer") + ".");
        }

        void StartAssembly(string[] recipe)
        {
            if (pizza != null) Destroy(pizza.gameObject);
            pizza = Instantiate(pizzaPrefab, baseSpawn.position, baseSpawn.rotation);
            pizza.Required.Clear();
            pizza.Required.AddRange(recipe);
            cookTimer = 0f;
            if (target != null) target.SetWaiting(false);
            SetStage(Stage.Assemble);
        }

        protected override void OnCleanup()
        {
            if (pizza != null)
            {
                pizza.Item.ForceReleaseAll();
                if (GameSession.Instance != null && GameSession.Instance.Bag != null) GameSession.Instance.Bag.Remove(pizza.Item);
                Destroy(pizza.gameObject);
            }
            pizza = null;
            if (target != null) target.SetWaiting(false);
            Current = Stage.None;
            RefreshLabel();
        }

        void Update()
        {
            if (State != JobState.Active) return;
            Tick(Time.deltaTime);
        }

        /// <summary>Advances the job. Public so tests can step it without waiting for frames.</summary>
        public void Tick(float deltaTime)
        {
            if (State != JobState.Active) return;
            GameBalance balance = GameSession.Instance.Balance;

            if (pizza == null || pizza.transform.position.y < lostBelowY)
            {
                // Lost item recovery: no penalty beyond redoing the cooking.
                var recipe = pizza != null ? new List<string>(pizza.Required).ToArray() : Recipes[0];
                HudToast.Show("The pizza is gone. A fresh base is on the counter.");
                StartAssembly(recipe);
                return;
            }

            Vector3 position = pizza.transform.position;
            bool resting = pizza.Item.State == ItemState.Free;
            switch (Current)
            {
                case Stage.Assemble:
                    if (pizza.IsFullyTopped) SetStage(Stage.Cook);
                    break;
                case Stage.Cook:
                    if (resting && oven.Contains(position))
                    {
                        cookTimer += deltaTime;
                        if (cookTimer >= balance.pizzaCookSeconds)
                        {
                            pizza.SetCooked();
                            GameFeedback.Play(FeedbackSound.Ding, oven.transform.position);
                            SetStage(Stage.Box);
                        }
                        else
                        {
                            RefreshLabel();
                        }
                    }
                    break;
                case Stage.Box:
                    if (resting && boxing.Contains(position))
                    {
                        pizza.SetBoxed();
                        GameFeedback.Play(FeedbackSound.Store, boxing.transform.position);
                        if (target != null) target.SetWaiting(true);
                        SetStage(Stage.Deliver);
                    }
                    break;
                case Stage.Deliver:
                    if (target != null && pizza.Item.State != ItemState.Stored && target.Zone.Contains(position))
                    {
                        int payout = balance.pizzaPayout;
                        bool fast = Time.time - acceptedAt <= balance.pizzaTipSeconds;
                        if (fast) payout += balance.pizzaSpeedTip;
                        DeliveryCustomer served = target;
                        Complete(payout, fast ? "Hot and fast! Tip included." : "Pizza delivered.");
                        served.Celebrate();
                    }
                    break;
            }
        }

        void SetStage(Stage stage)
        {
            Current = stage;
            switch (stage)
            {
                case Stage.Assemble:
                    SetObjective("Pizza: put " + RecipeText() + " on the base");
                    break;
                case Stage.Cook:
                    SetObjective("Pizza: slide it into the oven");
                    break;
                case Stage.Box:
                    SetObjective("Pizza: put it on the boxing mat");
                    break;
                case Stage.Deliver:
                    SetObjective("Deliver to " + (target != null ? target.placeName : "the customer"));
                    break;
            }
            RefreshLabel();
        }

        string RecipeText() => pizza != null ? string.Join(" + ", pizza.Required) : "";

        void RefreshLabel()
        {
            if (orderLabel == null) return;
            switch (Current)
            {
                case Stage.None:
                    orderLabel.text = "<b>ORDERS</b>\nSlap TAKE ORDER to start a delivery.";
                    break;
                case Stage.Assemble:
                    orderLabel.text = "<b>1. TOPPINGS</b>\n" + RecipeText() + "\nPress each one onto the base.";
                    break;
                case Stage.Cook:
                    orderLabel.text = "<b>2. BAKE</b>\nSlide it into the oven and let go.\n" + Mathf.RoundToInt(CookProgress * 100f) + "%";
                    break;
                case Stage.Box:
                    orderLabel.text = "<b>3. BOX</b>\nPut it on the boxing mat.";
                    break;
                case Stage.Deliver:
                    orderLabel.text = "<b>4. DELIVER</b>\n" + (target != null ? target.placeName : "") + "\nFollow the beacon.";
                    break;
            }
        }
    }
}
