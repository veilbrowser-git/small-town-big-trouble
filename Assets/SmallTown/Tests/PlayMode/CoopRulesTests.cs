using System.Collections;
using System.Linq;
using NUnit.Framework;
using SmallTown.Crime;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using SmallTown.Jobs;
using SmallTown.Networking;
using SmallTown.Npc;
using UnityEngine;
using UnityEngine.TestTools;

namespace SmallTown.Tests
{
    /// <summary>
    /// The host-side co-op rules, tested with a second player added directly to
    /// the session (no networking involved): who gets paid, who gets blamed, who
    /// gets chased, and who loses what. Real connections are covered separately
    /// by Tools/coop_check.py.
    /// </summary>
    public class CoopRulesTests
    {
        const ulong Friend = 7;
        GameHarness g;
        PlayerState friend;
        int friendMessages;
        int previousCaptureRate;

        [SetUp]
        public void SetUp()
        {
            previousCaptureRate = Time.captureFramerate;
            Time.captureFramerate = 60;
            friendMessages = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = previousCaptureRate;
            g?.Dispose();
            g = null;
        }

        void AddFriend(Vector3 position)
        {
            friend = g.Session.AddPlayer(Friend, _ => friendMessages++);
            friend.Head = position + Vector3.up * 0.8f;
            friend.Feet = position;
        }

        [Test]
        public void MotionValidator_AcceptsRunning_RejectsTeleporting_UnlessAnnounced()
        {
            var validator = new MotionValidator(14f, 0.5f);
            Vector3 position = Vector3.zero;
            // Sprinting flat out for five seconds is fine.
            for (int i = 0; i < 300; i++)
            {
                position += Vector3.forward * (14f / 60f);
                Assert.That(validator.Check(position, 1f / 60f, 0), Is.True, "frame " + i);
            }
            Assert.That(validator.Violations, Is.EqualTo(0));

            // Jumping 60 m in one frame is not.
            Vector3 honest = position;
            position += Vector3.right * 60f;
            bool accepted = true;
            for (int i = 0; i < 40; i++) accepted &= validator.Check(position, 1f / 60f, 0);
            Assert.That(accepted, Is.False, "An impossible move is refused.");
            Assert.That(Vector3.Distance(validator.LastValid, honest), Is.LessThan(8f), "The player is sent back to where they last made sense.");

            // The same jump is fine when the player's own game announced a teleport (arrest, respawn, unstuck).
            var second = new MotionValidator(14f, 0.5f);
            second.Check(Vector3.zero, 1f / 60f, 0);
            bool ok = true;
            for (int i = 0; i < 40; i++) ok &= second.Check(new Vector3(60f, 0f, 0f), 1f / 60f, 1);
            Assert.That(ok, Is.True);
        }

        [UnityTest]
        public IEnumerator Job_PaysEveryPlayerOnce()
        {
            g = new GameHarness(Vector3.zero);
            AddFriend(new Vector3(2f, 0f, 0f));
            int mine = g.Wallet.Cash;
            int theirs = friend.Wallet.Cash;

            GrabbableItem templateItem = g.Item(new Vector3(0f, 60f, 0f));
            var template = templateItem.gameObject.AddComponent<Pizza>();
            var customerObject = new GameObject("Customer");
            customerObject.transform.SetParent(g.Root.transform, false);
            var customer = customerObject.AddComponent<DeliveryCustomer>();
            customer.Configure(g.Zone(new Vector3(20f, 0.7f, 20f), Vector3.one * 2f), null, null);
            var jobObject = new GameObject("Job");
            jobObject.transform.SetParent(g.Root.transform, false);
            var job = jobObject.AddComponent<PizzaDeliveryJob>();
            BoxZone oven = g.Zone(new Vector3(3f, 0.6f, 0f), Vector3.one * 0.6f);
            BoxZone boxing = g.Zone(new Vector3(5f, 0.6f, 0f), Vector3.one * 0.6f);
            job.Configure(template, g.Point(new Vector3(1f, 0.6f, 0f)), oven, boxing, new[] { customer }, null);

            // The friend takes the order; anyone may.
            g.Session.RunAs(friend, job.TakeOrder);
            Assert.That(job.State, Is.EqualTo(JobState.Active));
            Pizza pizza = job.ActivePizza;
            foreach (string needed in pizza.Required.ToArray())
            {
                GrabbableItem item = g.Item(new Vector3(9f, 1f, 9f), false);
                var topping = item.gameObject.AddComponent<Topping>();
                topping.toppingId = needed;
                pizza.TryAdd(topping);
            }
            job.Tick(0.1f);
            pizza.transform.position = oven.transform.position;
            for (int i = 0; i < 100 && job.Current == PizzaDeliveryJob.Stage.Cook; i++) job.Tick(0.1f);
            pizza.transform.position = boxing.transform.position;
            job.Tick(0.1f);
            // The friend is carrying it when it reaches the customer.
            pizza.Item.SetRemoteState(ItemState.Held, Friend);
            pizza.transform.position = customer.Zone.transform.position;
            job.Tick(0.1f);
            job.Tick(0.1f);

            int pay = g.Balance.pizzaPayout + g.Balance.pizzaSpeedTip;
            Assert.That(g.Wallet.Cash, Is.EqualTo(mine + pay), "The host is paid.");
            Assert.That(friend.Wallet.Cash, Is.EqualTo(theirs + pay), "The friend is paid the same.");
            Assert.That(friend.Wallet.Profile.jobsCompleted, Is.EqualTo(1));
            Assert.That(friendMessages, Is.GreaterThan(0), "The friend is told.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Robbery_OnlyTheRobberIsWanted_UntilSomeoneElseTakesTheCash()
        {
            g = new GameHarness(Vector3.zero, b =>
            {
                b.registerBundles = 3;
                b.alarmDelay = 1f;
                b.robberyCooldown = 60f;
            });
            g.Player.Settle();
            Vector3 centre = g.Player.Head.position;
            AddFriend(new Vector3(centre.x + 1f, 0f, centre.z));

            BoxZone zone = g.Zone(new Vector3(centre.x, 1f, centre.z), new Vector3(8f, 4f, 8f));
            NpcBody cashier = g.Npc(new Vector3(centre.x, 0f, centre.z + 2f), 180f);
            LootItem cashTemplate = g.Loot(new Vector3(0f, 70f, 0f), 1, true, true, "Cash");
            var siteObject = new GameObject("Site");
            siteObject.transform.SetParent(g.Root.transform, false);
            siteObject.transform.position = zone.transform.position;
            var site = siteObject.AddComponent<RobberySite>();
            site.Configure(zone, cashier, g.Point(new Vector3(centre.x, 0.7f, centre.z + 1f)), g.Point(zone.transform.position),
                cashTemplate, new Transform[0], new LootItem[0], null);
            yield return null;

            // The friend holds the place up. The local player just happens to be standing there.
            Assert.That(site.StartHoldUp(friend), Is.True);
            for (int i = 0; i < 30; i++) site.Tick(0.1f);
            Assert.That(friend.Wanted.Level, Is.EqualTo(2), "The robber is wanted.");
            Assert.That(g.Session.Wanted.Level, Is.EqualTo(0), "Standing next to a robbery is not a crime.");
            Assert.That(site.Robber, Is.EqualTo(friend));

            // The local player now pockets a bundle.
            LootItem bundle = LootItem.All.First(l => l.isCash && l != cashTemplate);
            Assert.That(g.Player.Bag.TryStore(bundle.Item), Is.True);
            site.Tick(0.1f);
            Assert.That(g.Session.Wanted.Level, Is.EqualTo(2), "Taking the stolen cash makes you an accomplice.");
            Assert.That(friend.Wallet.Profile.robberiesCompleted, Is.EqualTo(0), "Still in progress.");
        }

        [UnityTest]
        public IEnumerator Police_ChaseTheWantedPlayer_NotTheInnocentOneBesideThem()
        {
            g = new GameHarness(Vector3.zero);
            g.Player.Settle();
            g.BakeNavMesh();
            var arrest = g.Root.AddComponent<ArrestController>();
            arrest.Configure(g.Point(new Vector3(-40f, 0f, -40f)));
            PlayerState arrested = null;
            arrest.RemotePresentation = (player, position, yaw, result) => arrested = player;
            var dispatcher = g.Root.AddComponent<PoliceDispatcher>();
            Vector3 station = new Vector3(0f, 0f, 10f);
            dispatcher.Configure(g.OfficerTemplate(), new[] { g.Point(station), g.Point(station + Vector3.right * 1.5f) },
                new[] { g.Point(station + Vector3.forward * 2f), g.Point(station + Vector3.back * 2f) }, null, new GameObject[0], arrest);
            yield return null;

            // The innocent local player stands right in the officers' path; the wanted friend is further on.
            AddFriend(new Vector3(0f, 0f, -6f));
            LootItem friendsLoot = g.Loot(new Vector3(0f, 1f, -6f), 38, false, true, "Friend's radio");
            friendsLoot.Item.SetRemoteState(ItemState.Stored, Friend);
            LootItem myLoot = g.Loot(new Vector3(1f, 1f, 1f), 38, false, true, "My radio");
            g.Player.Bag.TryStore(myLoot.Item);
            friend.Wallet.Apply("seed", 200);
            int myCash = g.Wallet.Cash;
            friend.Wanted.ReportCrime(2, friend.Feet);

            float deadline = Time.time + 30f;
            while (arrested == null && Time.time < deadline)
            {
                g.Player.Step(Time.deltaTime);
                Assert.That(g.Session.Wanted.Level, Is.EqualTo(0), "The innocent player never becomes wanted.");
                Assert.That(arrest.InProgress, Is.False, "The innocent player is never arrested.");
                yield return null;
            }

            Assert.That(arrested, Is.EqualTo(friend), "The wanted player is the one arrested.");
            Assert.That(friend.Wanted.IsWanted, Is.False);
            Assert.That(friend.Wallet.Cash, Is.EqualTo(g.Balance.startingMoney + 200 - g.Balance.FineFor(2)), "Only the arrested player is fined.");
            Assert.That(g.Wallet.Cash, Is.EqualTo(myCash));
            yield return null;
            Assert.That(friendsLoot == null, Is.True, "The arrested player's stolen goods are confiscated.");
            Assert.That(myLoot != null && g.Player.Bag.Items.Count == 1, Is.True, "Another player's bag is left alone, whatever is in it.");
        }

        [UnityTest]
        public IEnumerator EachPlayerEscapesOnTheirOwnClock()
        {
            g = new GameHarness(Vector3.zero, b => b.escapeSeconds = 3f);
            // The local player is boxed in and cannot be seen; the friend stands in the open.
            g.Player.AddBox(new Vector3(0f, 1.5f, 1.5f), new Vector3(3.3f, 3f, 0.3f));
            g.Player.AddBox(new Vector3(0f, 1.5f, -1.5f), new Vector3(3.3f, 3f, 0.3f));
            g.Player.AddBox(new Vector3(1.5f, 1.5f, 0f), new Vector3(0.3f, 3f, 3.3f));
            g.Player.AddBox(new Vector3(-1.5f, 1.5f, 0f), new Vector3(0.3f, 3f, 3.3f));
            g.Player.Settle();
            g.BakeNavMesh();
            var arrest = g.Root.AddComponent<ArrestController>();
            arrest.RemotePresentation = (player, position, yaw, result) => { };
            var dispatcher = g.Root.AddComponent<PoliceDispatcher>();
            Vector3 station = new Vector3(0f, 0f, 14f);
            dispatcher.Configure(g.OfficerTemplate(), new[] { g.Point(station) }, new[] { g.Point(station + Vector3.forward), g.Point(station + Vector3.back) },
                null, new GameObject[0], arrest);
            yield return null;
            AddFriend(new Vector3(6f, 0f, 14f));

            // Both are wanted for something reported far away from the hidden player.
            g.Session.Wanted.ReportCrime(1, new Vector3(6f, 0f, 14f));
            friend.Wanted.ReportCrime(1, new Vector3(6f, 0f, 14f));

            float deadline = Time.time + 12f;
            while (g.Session.Wanted.IsWanted && Time.time < deadline)
            {
                g.Player.Step(Time.deltaTime);
                yield return null;
            }
            Assert.That(g.Session.Wanted.IsWanted, Is.False, "The hidden player gets away.");
            Assert.That(friend.Wanted.IsWanted || friend.Wallet.Profile.timesArrested > 0, Is.True,
                "The player in plain sight does not get away just because their friend did.");
        }

        [UnityTest]
        public IEnumerator PawnShop_PaysWhoeverBroughtTheItemIn_AndPurchasesBelongToTheBuyer()
        {
            g = new GameHarness(Vector3.zero);
            yield return null;
            AddFriend(new Vector3(2f, 0f, 0f));
            var shopObject = new GameObject("Pawn");
            shopObject.transform.SetParent(g.Root.transform, false);
            var shop = shopObject.AddComponent<PawnShop>();
            shop.Configure(g.Zone(new Vector3(4f, 0.6f, 4f), Vector3.one * 0.8f), null);

            int mine = g.Wallet.Cash;
            int theirs = friend.Wallet.Cash;
            LootItem radio = g.Loot(new Vector3(8f, 1f, 8f), 38);
            radio.Item.SetRemoteState(ItemState.Held, Friend);   // the friend carried it in...
            radio.Item.SetRemoteState(ItemState.Free, GrabbableItem.NoHolder); // ...and put it down
            Assert.That(shop.Sell(radio), Is.EqualTo(g.Balance.PawnPrice(38)));
            Assert.That(friend.Wallet.Cash, Is.EqualTo(theirs + g.Balance.PawnPrice(38)), "The seller is paid.");
            Assert.That(g.Wallet.Cash, Is.EqualTo(mine), "Nobody else is.");

            // The friend buys the satchel: their money, their unlock, and the host's own bag is untouched.
            friend.Wallet.Apply("seed", 500);
            var offerObject = new GameObject("Offer");
            offerObject.transform.SetParent(g.Root.transform, false);
            var offer = offerObject.AddComponent<ShopOffer>();
            offer.unlockId = GameBalance.SatchelUnlock;
            TransactionResult result = TransactionResult.Invalid;
            g.Session.RunAs(friend, () => result = offer.Buy());
            Assert.That(result, Is.EqualTo(TransactionResult.Applied));
            Assert.That(friend.Wallet.Owns(GameBalance.SatchelUnlock), Is.True);
            Assert.That(g.Wallet.Owns(GameBalance.SatchelUnlock), Is.False);
            Assert.That(g.Player.Bag.capacity, Is.EqualTo(g.Balance.baseSatchelCapacity));
            Assert.That(g.Wallet.Cash, Is.EqualTo(mine));
        }

        [UnityTest]
        public IEnumerator PlayerLeaving_ClearsTheirWantedLevel_AndStandsThePoliceDown()
        {
            g = new GameHarness(Vector3.zero);
            g.Player.Settle();
            g.BakeNavMesh();
            var arrest = g.Root.AddComponent<ArrestController>();
            var dispatcher = g.Root.AddComponent<PoliceDispatcher>();
            Vector3 station = new Vector3(0f, 0f, 20f);
            dispatcher.Configure(g.OfficerTemplate(), new[] { g.Point(station), g.Point(station + Vector3.right) },
                new[] { g.Point(station + Vector3.forward), g.Point(station + Vector3.back) }, null, new GameObject[0], arrest);
            yield return null;
            AddFriend(new Vector3(30f, 0f, 30f));
            yield return null;
            friend.Wanted.ReportCrime(3, friend.Feet);
            Assert.That(dispatcher.Strength, Is.EqualTo(3));

            g.Session.RemovePlayer(Friend);
            yield return null;
            Assert.That(g.Session.Players.Count, Is.EqualTo(1));
            Assert.That(dispatcher.Strength, Is.EqualTo(0), "With the only wanted player gone, the response is called off.");
            Assert.That(g.Session.Wanted.IsWanted, Is.False);
        }
    }
}
