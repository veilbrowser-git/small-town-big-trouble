using System.Collections;
using System.Linq;
using NUnit.Framework;
using SmallTown.Crime;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using SmallTown.Jobs;
using SmallTown.Locomotion;
using SmallTown.Npc;
using UnityEngine;
using UnityEngine.TestTools;

namespace SmallTown.Tests
{
    /// <summary>Milestone B rules exercised with real components: jobs, robbery, pawn sales, police and arrest.</summary>
    public class GameplayTests
    {
        GameHarness g;
        int previousCaptureRate;

        [SetUp]
        public void SetUp()
        {
            previousCaptureRate = Time.captureFramerate;
            Time.captureFramerate = 60;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = previousCaptureRate;
            g?.Dispose();
            g = null;
        }

        // ------------------------------------------------------------------ Pizza job

        sealed class PizzaSetup
        {
            public PizzaDeliveryJob job;
            public BoxZone oven;
            public BoxZone boxing;
            public DeliveryCustomer customer;
        }

        PizzaSetup BuildPizzaJob()
        {
            // An active template far overhead; the job clones it for each order.
            GrabbableItem templateItem = g.Item(new Vector3(0f, 60f, 0f));
            var template = templateItem.gameObject.AddComponent<Pizza>();

            var setup = new PizzaSetup
            {
                oven = g.Zone(new Vector3(3f, 0.6f, 0f), Vector3.one * 0.6f, "Oven"),
                boxing = g.Zone(new Vector3(5f, 0.6f, 0f), Vector3.one * 0.6f, "Boxing"),
            };
            var customerObject = new GameObject("Customer");
            customerObject.transform.SetParent(g.Root.transform, false);
            customerObject.transform.position = new Vector3(20f, 0f, 20f);
            setup.customer = customerObject.AddComponent<DeliveryCustomer>();
            setup.customer.placeName = "the test house";
            setup.customer.Configure(g.Zone(new Vector3(20f, 0.7f, 20f), Vector3.one * 2f, "Drop"), null, null);

            var jobObject = new GameObject("PizzaJob");
            jobObject.transform.SetParent(g.Root.transform, false);
            setup.job = jobObject.AddComponent<PizzaDeliveryJob>();
            setup.job.jobId = "job.pizza";
            setup.job.displayName = "Pizza delivery";
            setup.job.Configure(template, g.Point(new Vector3(1f, 0.6f, 0f)), setup.oven, setup.boxing, new[] { setup.customer }, null);
            return setup;
        }

        Topping MakeTopping(string id)
        {
            GrabbableItem item = g.Item(new Vector3(9f, 1f, 9f), false);
            var topping = item.gameObject.AddComponent<Topping>();
            topping.toppingId = id;
            return topping;
        }

        void CookBoxAndCarry(PizzaSetup s)
        {
            Pizza pizza = s.job.ActivePizza;
            foreach (string needed in pizza.Required.ToArray())
            {
                if (!pizza.Toppings.Contains(needed)) Assert.That(pizza.TryAdd(MakeTopping(needed)), Is.True, needed);
            }
            s.job.Tick(0.1f);
            Assert.That(s.job.Current, Is.EqualTo(PizzaDeliveryJob.Stage.Cook));

            pizza.transform.position = s.oven.transform.position;
            for (int i = 0; i < 100 && s.job.Current == PizzaDeliveryJob.Stage.Cook; i++) s.job.Tick(0.1f);
            Assert.That(pizza.Cooked, Is.True);
            Assert.That(s.job.Current, Is.EqualTo(PizzaDeliveryJob.Stage.Box));

            pizza.transform.position = s.boxing.transform.position;
            s.job.Tick(0.1f);
            Assert.That(pizza.Boxed, Is.True);
            Assert.That(s.job.Current, Is.EqualTo(PizzaDeliveryJob.Stage.Deliver));
        }

        [UnityTest]
        public IEnumerator PizzaDelivery_FullRun_PaysExactlyOnce_AndCanBeRepeated()
        {
            g = new GameHarness(Vector3.zero);
            PizzaSetup s = BuildPizzaJob();
            int start = g.Wallet.Cash;

            Assert.That(s.job.Accept(), Is.True);
            Assert.That(s.job.Accept(), Is.False, "One run at a time.");
            Assert.That(s.job.Current, Is.EqualTo(PizzaDeliveryJob.Stage.Assemble));
            Pizza pizza = s.job.ActivePizza;
            Assert.That(pizza, Is.Not.Null);
            Assert.That(JobManager.Instance.Active, Is.EqualTo(s.job));
            Assert.That(s.job.Objective, Does.Contain(pizza.Required[0]));

            // Wrong and duplicate toppings are refused.
            Assert.That(pizza.TryAdd(MakeTopping("pineapple")), Is.False);
            Topping first = MakeTopping(pizza.Required[0]);
            Assert.That(pizza.TryAdd(first), Is.True);
            Assert.That(pizza.TryAdd(MakeTopping(pizza.Required[0])), Is.False);
            // An uncooked pizza cannot skip the oven.
            pizza.transform.position = s.boxing.transform.position;
            s.job.Tick(0.1f);
            Assert.That(pizza.Boxed, Is.False);

            CookBoxAndCarry(s);

            // Carried to the customer, in hand.
            pizza.transform.position = s.customer.Zone.transform.position;
            s.job.Tick(0.1f);
            int expected = g.Balance.pizzaPayout + g.Balance.pizzaSpeedTip;
            Assert.That(g.Wallet.Cash, Is.EqualTo(start + expected));
            Assert.That(s.job.State, Is.EqualTo(JobState.Available));
            Assert.That(JobManager.Instance.Active, Is.Null);
            Assert.That(g.Wallet.Profile.jobsCompleted, Is.EqualTo(1));

            s.job.Tick(0.1f);
            s.job.Tick(0.1f);
            Assert.That(g.Wallet.Cash, Is.EqualTo(start + expected), "Finishing never pays twice.");
            yield return null;
            Assert.That(pizza == null, Is.True, "The delivered pizza is gone.");

            // A second run is a new order and pays again.
            Assert.That(s.job.Accept(), Is.True);
            CookBoxAndCarry(s);
            s.job.ActivePizza.transform.position = s.customer.Zone.transform.position;
            s.job.Tick(0.1f);
            Assert.That(g.Wallet.Cash, Is.EqualTo(start + expected * 2));
            Assert.That(s.job.CompletedRuns, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator PizzaDelivery_LostPizza_IsReplaced_AndCancelCleansUp()
        {
            g = new GameHarness(Vector3.zero);
            PizzaSetup s = BuildPizzaJob();
            int start = g.Wallet.Cash;
            s.job.Accept();
            CookBoxAndCarry(s);
            Pizza lost = s.job.ActivePizza;
            string[] recipe = lost.Required.ToArray();

            // It falls in the sea on the way.
            lost.transform.position = new Vector3(10f, -5f, 10f);
            s.job.Tick(0.1f);
            Assert.That(s.job.State, Is.EqualTo(JobState.Active), "Losing the pizza does not fail the job.");
            Assert.That(s.job.Current, Is.EqualTo(PizzaDeliveryJob.Stage.Assemble));
            Assert.That(s.job.ActivePizza, Is.Not.EqualTo(lost));
            Assert.That(s.job.ActivePizza.Required, Is.EquivalentTo(recipe), "Same order, fresh base.");
            Assert.That(g.Wallet.Cash, Is.EqualTo(start));
            yield return null;
            Assert.That(lost == null, Is.True);

            // Abandon the job with the pizza in the satchel.
            Pizza second = s.job.ActivePizza;
            Assert.That(g.Player.Bag.TryStore(second.Item), Is.True);
            s.job.Cancel();
            Assert.That(s.job.State, Is.EqualTo(JobState.Available));
            Assert.That(g.Player.Bag.Items, Is.Empty, "Cancelling removes the job item from the bag too.");
            Assert.That(g.Wallet.Cash, Is.EqualTo(start), "No pay for an abandoned job.");
            yield return null;
            Assert.That(second == null, Is.True);
            Assert.That(s.job.Accept(), Is.True, "The job can be taken again afterwards.");
        }

        // ------------------------------------------------------------------ Robbery

        RobberySite BuildStore(out LootItem cashTemplate, out NpcBody cashier)
        {
            Vector3 centre = g.Player.Head.position;
            BoxZone zone = g.Zone(new Vector3(centre.x, 1f, centre.z), new Vector3(8f, 4f, 8f), "Store");
            cashier = g.Npc(new Vector3(centre.x, 0f, centre.z + 2f), 180f);
            cashTemplate = g.Loot(new Vector3(0f, 70f, 0f), 1, true, true, "Cash");
            LootItem merchTemplate = g.Loot(new Vector3(0f, 72f, 0f), 12, false, false, "Snack");

            var spots = new Transform[4];
            for (int i = 0; i < spots.Length; i++) spots[i] = g.Point(new Vector3(centre.x - 2f + i, 0.5f, centre.z - 2f));

            var siteObject = new GameObject("Site");
            siteObject.transform.SetParent(g.Root.transform, false);
            siteObject.transform.position = zone.transform.position;
            var site = siteObject.AddComponent<RobberySite>();
            site.Configure(zone, cashier, g.Point(new Vector3(centre.x, 0.7f, centre.z + 1f)), g.Point(zone.transform.position),
                cashTemplate, spots, new[] { merchTemplate }, null);
            return site;
        }

        [UnityTest]
        public IEnumerator Robbery_PaysOutTheRegisterOnce_RaisesAlarm_ThenRestocks()
        {
            g = new GameHarness(Vector3.zero, b =>
            {
                b.registerBundles = 4;
                b.bundleValue = 45;
                b.robberyCooldown = 6f;
                b.alarmDelay = 3f;
            });
            g.Player.Settle();
            RobberySite site = BuildStore(out LootItem cashTemplate, out NpcBody cashier);
            yield return null; // Start

            Assert.That(site.Stock.Count(l => l != null), Is.EqualTo(4), "Shelves are stocked on opening.");
            Assert.That(site.StartHoldUp(), Is.True);
            Assert.That(cashier.pose, Is.EqualTo(NpcPose.HandsUp));
            Assert.That(site.StartHoldUp(), Is.False);

            for (int i = 0; i < 150; i++) site.Tick(0.1f);

            LootItem[] cash = LootItem.All.Where(l => l.isCash && l != cashTemplate).ToArray();
            Assert.That(cash.Length, Is.EqualTo(4), "Exactly the register's contents, no more.");
            Assert.That(cash.All(c => c.stolen && c.value == 45), Is.True);
            Assert.That(cash.Select(c => c.InstanceId).Distinct().Count(), Is.EqualTo(4), "Every bundle is individually identifiable.");
            Assert.That(g.Session.Wanted.Level, Is.EqualTo(2), "The alarm makes the robber wanted.");
            Assert.That(g.Wallet.Cash, Is.EqualTo(g.Balance.startingMoney), "Stolen cash is not money until you get away.");
            Assert.That(g.Wallet.Profile.robberiesCompleted, Is.EqualTo(1));

            // By now the cooldown has also run out and the shop has restocked.
            Assert.That(site.Machine.State, Is.EqualTo(RobberyState.Idle));
            Assert.That(site.Machine.BundlesRemaining, Is.EqualTo(4));
            Assert.That(site.StartHoldUp(), Is.True, "It can be robbed again only after the restock.");
            for (int i = 0; i < 30; i++) site.Tick(0.1f);
            Assert.That(site.Machine.State, Is.Not.EqualTo(RobberyState.Idle));
            Assert.That(site.StartHoldUp(), Is.False);
        }

        [UnityTest]
        public IEnumerator Shoplifting_MarksGoodsStolen_AndIsALesserCrime()
        {
            g = new GameHarness(Vector3.zero);
            g.Player.Settle();
            RobberySite site = BuildStore(out _, out _);
            yield return null;

            LootItem snack = site.Stock[0];
            Assert.That(snack.stolen, Is.False);
            Assert.That(snack.owner, Is.EqualTo(site.siteId));
            site.Tick(0.1f);
            Assert.That(g.Session.Wanted.Level, Is.EqualTo(0), "Handling goods inside the shop is fine.");

            // Carried out in the satchel: colliders are off, but the position check still catches it.
            Assert.That(g.Player.Bag.TryStore(snack.Item), Is.True);
            snack.transform.position = new Vector3(50f, 0.5f, 50f);
            site.Tick(0.1f);
            Assert.That(snack.stolen, Is.True);
            Assert.That(g.Session.Wanted.Level, Is.EqualTo(1));

            int level = g.Session.Wanted.Level;
            site.Tick(0.1f);
            Assert.That(g.Session.Wanted.Level, Is.EqualTo(level), "One theft is one report.");
        }

        // ------------------------------------------------------------------ Pawn shop and loot

        [UnityTest]
        public IEnumerator PawnShop_SellsEachItemOnce()
        {
            g = new GameHarness(Vector3.zero);
            var shopObject = new GameObject("Pawn");
            shopObject.transform.SetParent(g.Root.transform, false);
            BoxZone tray = g.Zone(new Vector3(4f, 0.6f, 4f), Vector3.one * 0.8f, "Tray");
            var shop = shopObject.AddComponent<PawnShop>();
            shop.Configure(tray, null);
            int start = g.Wallet.Cash;

            LootItem radio = g.Loot(new Vector3(8f, 1f, 8f), 38, false, true, "Pocket Radio");
            int price = g.Balance.PawnPrice(38);
            Assert.That(shop.Sell(radio), Is.EqualTo(price));
            Assert.That(shop.Sell(radio), Is.EqualTo(0), "The same item cannot be sold twice.");
            Assert.That(g.Wallet.Cash, Is.EqualTo(start + price));
            Assert.That(g.Wallet.WasProcessed("sale:" + radio.InstanceId), Is.True);

            LootItem cash = g.Loot(new Vector3(8f, 1f, 9f), 45, true);
            Assert.That(shop.Sell(cash), Is.EqualTo(0), "Cash is not pawned.");

            // Dropped on the tray: sold automatically, once, however long it sits there.
            LootItem soda = g.Loot(tray.transform.position, 10, false, true, "Soda");
            yield return null;
            yield return null;
            yield return null;
            Assert.That(g.Wallet.Cash, Is.EqualTo(start + price + g.Balance.PawnPrice(10)));
            Assert.That(soda == null, Is.True);
            Assert.That(radio == null, Is.True);

            // Held over the tray is not a sale.
            LootItem held = g.Loot(tray.transform.position, 20, false, true, "Held");
            g.Player.SetHand(Hand.Right, g.Player.Head.InverseTransformPoint(tray.transform.position));
            g.Player.Step(1f / 72f);
            Assert.That(g.Player.RightGrabber.TryGrabItem(held.Item), Is.True);
            int before = g.Wallet.Cash;
            yield return null;
            yield return null;
            Assert.That(g.Wallet.Cash, Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator StolenCash_OnlyBecomesMoney_AfterEscaping()
        {
            g = new GameHarness(Vector3.zero, b => b.escapeSeconds = 4f);
            g.Root.AddComponent<LootBanker>();
            yield return null; // Start
            int start = g.Wallet.Cash;

            g.Session.Wanted.ReportCrime(2, Vector3.zero);
            LootItem bundle = g.Loot(new Vector3(1f, 1f, 1f), 45, true);
            Assert.That(g.Player.Bag.TryStore(bundle.Item), Is.True);
            Assert.That(g.Wallet.Cash, Is.EqualTo(start), "While wanted, cash in the bag is still just loot.");

            for (int i = 0; i < 30; i++) g.Session.Wanted.Tick(0.1f, false, Vector3.zero);
            Assert.That(g.Session.Wanted.IsWanted, Is.True);
            for (int i = 0; i < 15; i++) g.Session.Wanted.Tick(0.1f, false, Vector3.zero);
            Assert.That(g.Session.Wanted.IsWanted, Is.False);
            Assert.That(g.Wallet.Cash, Is.EqualTo(start + 45), "Getting away pays out the cash.");
            Assert.That(g.Player.Bag.Items, Is.Empty);

            // With nobody looking for you, bagging cash pockets it straight away, once.
            LootItem found = g.Loot(new Vector3(1f, 1f, 2f), 45, true);
            g.Player.Bag.TryStore(found.Item);
            Assert.That(g.Wallet.Cash, Is.EqualTo(start + 90));
            yield return null;
            Assert.That(found == null, Is.True);
        }

        [UnityTest]
        public IEnumerator Arrest_FinesAndConfiscatesStolenGoods_ButKeepsProperty()
        {
            g = new GameHarness(Vector3.zero);
            g.Player.Settle();
            g.Wallet.Apply("test:seed", 180);                    // wallet 200
            g.Wallet.Purchase(GameBalance.PistolUnlock, 50);     // wallet 150
            g.Wallet.Deposit(60);                                // wallet 90, bank 60
            var arrest = g.Root.AddComponent<ArrestController>();
            arrest.Configure(g.Point(new Vector3(30f, 0f, 30f), "Release"));

            LootItem stolenRadio = g.Loot(new Vector3(1f, 1f, 1f), 38, false, true);
            LootItem stolenCash = g.Loot(new Vector3(1f, 1f, 2f), 45, true, true);
            GrabbableItem ownCrate = g.Item(new Vector3(1f, 1f, 3f));
            LootItem heldLoot = g.Loot(new Vector3(1f, 1f, 4f), 20, false, true);
            g.Player.Bag.TryStore(stolenRadio.Item);
            g.Player.Bag.TryStore(stolenCash.Item);
            g.Player.Bag.TryStore(ownCrate);
            g.Player.Step(1f / 72f);
            Assert.That(g.Player.LeftGrabber.TryGrabItem(heldLoot.Item), Is.True);

            g.Session.Wanted.ReportCrime(2, Vector3.zero);
            ArrestResult result = arrest.ApplyConsequences();

            Assert.That(result.wantedLevel, Is.EqualTo(2));
            Assert.That(result.fine, Is.EqualTo(g.Balance.FineFor(2)));
            Assert.That(result.confiscatedItems, Is.EqualTo(3), "Bagged and held stolen goods are taken.");
            Assert.That(g.Wallet.Cash, Is.EqualTo(90 - g.Balance.FineFor(2)));
            Assert.That(g.Wallet.Bank, Is.EqualTo(60), "Savings are safe.");
            Assert.That(g.Wallet.Owns(GameBalance.PistolUnlock), Is.True, "Purchases are kept.");
            Assert.That(g.Session.Wanted.IsWanted, Is.False);
            Assert.That(g.Wallet.Profile.timesArrested, Is.EqualTo(1));
            Assert.That(g.Player.Bag.Items.Count, Is.EqualTo(1), "Legitimate belongings stay in the bag.");
            Assert.That(g.Player.Bag.Items[0], Is.EqualTo(ownCrate));
            Assert.That(g.Player.LeftGrabber.Held, Is.Null);

            g.Player.Run(0.5f, 1f / 72f);
            Vector3 head = g.Player.Head.position;
            Assert.That(new Vector2(head.x - 30f, head.z - 30f).magnitude, Is.LessThan(0.1f), "Released outside the station.");
            Assert.That(g.Player.Motor.IsGrounded, Is.True);
            yield return null;
            Assert.That(stolenRadio == null && stolenCash == null && heldLoot == null, Is.True);
        }

        // ------------------------------------------------------------------ Police

        [UnityTest]
        public IEnumerator Officer_SeesOnlyWhatIsInViewAndUnobstructed()
        {
            g = new GameHarness(new Vector3(0f, 0f, -30f));
            g.BakeNavMesh();
            PoliceOfficer officer = Object.Instantiate(g.OfficerTemplate(), Vector3.zero, Quaternion.identity);
            officer.Activate(Vector3.zero);
            officer.transform.rotation = Quaternion.identity; // facing +Z
            yield return null;
            GameBalance b = g.Balance;

            Assert.That(officer.CanSee(new Vector3(0f, 0.8f, 8f), b), Is.True, "Straight ahead.");
            Assert.That(officer.CanSee(new Vector3(0f, 0.8f, b.sightRange + 5f), b), Is.False, "Too far.");
            Assert.That(officer.CanSee(new Vector3(0f, 0.8f, -8f), b), Is.False, "Behind its back.");
            Assert.That(officer.CanSee(new Vector3(0f, 0.8f, -2f), b), Is.True, "Right behind: it notices.");

            g.Player.AddBox(new Vector3(0f, 1.5f, 4f), new Vector3(6f, 3f, 0.3f));
            Assert.That(officer.CanSee(new Vector3(0f, 0.8f, 8f), b), Is.False, "Not through a wall.");
            Assert.That(officer.CanSee(new Vector3(5f, 0.8f, 3f), b), Is.True, "Around the end of the wall.");
            Assert.That(officer.CanSee(new Vector3(0f, 5f, 6f), b), Is.True, "Over the wall, such as on a rooftop.");
            Object.Destroy(officer.gameObject);
        }

        PoliceDispatcher BuildPolice(Vector3 station, out ArrestController arrest)
        {
            arrest = g.Root.AddComponent<ArrestController>();
            arrest.Configure(g.Point(new Vector3(-40f, 0f, -40f), "Release"));
            var dispatcher = g.Root.AddComponent<PoliceDispatcher>();
            var spawns = new[] { g.Point(station), g.Point(station + Vector3.right * 1.5f), g.Point(station + Vector3.left * 1.5f) };
            var patrol = new[] { g.Point(station + Vector3.forward * 3f), g.Point(station + Vector3.back * 3f) };
            dispatcher.Configure(g.OfficerTemplate(), spawns, patrol, null, new GameObject[0], arrest);
            return dispatcher;
        }

        [UnityTest]
        public IEnumerator Police_ChaseAVisibleSuspect_AndArrest()
        {
            g = new GameHarness(Vector3.zero);
            g.Player.Settle();
            g.BakeNavMesh();
            PoliceDispatcher dispatcher = BuildPolice(new Vector3(0f, 0f, 12f), out ArrestController arrest);
            yield return null; // Start

            Assert.That(dispatcher.ActiveOfficers, Is.EqualTo(1), "One beat cop when all is quiet.");
            g.Session.Wanted.ReportCrime(2, g.Player.Head.position);
            Assert.That(dispatcher.ActiveOfficers, Is.EqualTo(g.Balance.OfficersFor(2)), "Level 2 brings a squad.");

            float deadline = Time.time + 25f;
            while (g.Wallet.Profile.timesArrested == 0 && Time.time < deadline)
            {
                g.Player.Step(Time.deltaTime);
                yield return null;
            }
            Assert.That(g.Wallet.Profile.timesArrested, Is.EqualTo(1), "A suspect standing in plain sight gets caught.");
            while (arrest.InProgress)
            {
                g.Player.Step(Time.deltaTime);
                yield return null;
            }
            Assert.That(g.Session.Wanted.IsWanted, Is.False);
            g.Player.Run(0.4f, 1f / 72f);
            Assert.That(Vector3.Distance(g.Player.Motor.BodyBottom, new Vector3(-40f, 0f, -40f)), Is.LessThan(0.5f));
            Assert.That(g.Player.Rig.Mode, Is.EqualTo(MovementMode.HandLocomotion), "Control returns after the arrest.");
        }

        [UnityTest]
        public IEnumerator Police_LoseAHiddenSuspect_SearchThenGiveUp()
        {
            g = new GameHarness(Vector3.zero, b =>
            {
                b.escapeSeconds = 6f;
                b.searchSeconds = 3f;
            });
            // The player hides inside a closed box of walls; the crime was reported elsewhere.
            g.Player.AddBox(new Vector3(0f, 1.5f, 1.5f), new Vector3(3.3f, 3f, 0.3f));
            g.Player.AddBox(new Vector3(0f, 1.5f, -1.5f), new Vector3(3.3f, 3f, 0.3f));
            g.Player.AddBox(new Vector3(1.5f, 1.5f, 0f), new Vector3(0.3f, 3f, 3.3f));
            g.Player.AddBox(new Vector3(-1.5f, 1.5f, 0f), new Vector3(0.3f, 3f, 3.3f));
            g.Player.Settle();
            g.BakeNavMesh();
            PoliceDispatcher dispatcher = BuildPolice(new Vector3(0f, 0f, 20f), out _);
            yield return null;

            int escapes = 0;
            g.Session.Wanted.Escaped += _ => escapes++;
            Vector3 reported = new Vector3(10f, 0f, 14f);
            g.Session.Wanted.ReportCrime(1, reported);

            bool investigated = false;
            float deadline = Time.time + 30f;
            while (g.Session.Wanted.IsWanted && Time.time < deadline)
            {
                foreach (PoliceOfficer officer in dispatcher.Officers)
                {
                    if (officer.State == OfficerState.Investigate || officer.State == OfficerState.Search) investigated = true;
                    Assert.That(officer.State, Is.Not.EqualTo(OfficerState.Pursue), "Nobody can see through the walls.");
                }
                g.Player.Step(Time.deltaTime);
                yield return null;
            }
            Assert.That(investigated, Is.True, "Officers go to where the crime was reported.");
            Assert.That(g.Session.Wanted.IsWanted, Is.False, "Staying hidden long enough clears the wanted level.");
            Assert.That(escapes, Is.EqualTo(1));
            Assert.That(g.Wallet.Profile.timesArrested, Is.EqualTo(0));
            Assert.That(g.Session.Wanted.LastKnownPosition, Is.EqualTo(reported), "Police never learned the real hiding place.");
        }

        // ------------------------------------------------------------------ Pistol, witnesses, saving

        sealed class ShotCounter : MonoBehaviour, IShootable
        {
            public int hits;
            public void OnShot(Vector3 point, Vector3 direction, PlayerState shooter) => hits++;
        }

        [UnityTest]
        public IEnumerator Pistol_HitsWhatItPointsAt_UsesAmmo_AndReloads()
        {
            g = new GameHarness(Vector3.zero);
            GameObject target = g.Player.AddBox(new Vector3(20f, 1f, 30f), Vector3.one, null, GameLayers.Npc);
            var counter = target.AddComponent<ShotCounter>();
            GameObject wallTarget = g.Player.AddBox(new Vector3(26f, 1f, 30f), Vector3.one, null, GameLayers.Npc);
            var hidden = wallTarget.AddComponent<ShotCounter>();

            Pistol pistol = g.MakePistol(new Vector3(20f, 1f, 20f));
            pistol.reloadSeconds = 0.2f;
            int crimes = 0;
            CrimeEvents.Committed += _ => crimes++;
            yield return null;

            Assert.That(pistol.Ammo, Is.EqualTo(pistol.magazineSize));
            for (int i = 0; i < pistol.magazineSize; i++) Assert.That(pistol.Fire(), Is.True);
            Assert.That(counter.hits, Is.EqualTo(pistol.magazineSize), "Shots land where the barrel points.");
            Assert.That(hidden.hits, Is.EqualTo(0));
            Assert.That(crimes, Is.EqualTo(pistol.magazineSize), "Every shot is a crime someone might witness.");
            Assert.That(pistol.Ammo, Is.EqualTo(0));
            Assert.That(pistol.Fire(), Is.False, "Empty: click.");

            int reserve = pistol.reserveAmmo;
            yield return new WaitForSeconds(0.5f);
            Assert.That(pistol.Ammo, Is.EqualTo(pistol.magazineSize), "It reloads itself.");
            Assert.That(pistol.reserveAmmo, Is.EqualTo(reserve - pistol.magazineSize));

            // Other players are never hit: their layers are not in the shot's mask.
            GameObject friend = g.Player.AddBox(new Vector3(20f, 1f, 24f), Vector3.one, null, GameLayers.RemotePlayer);
            var friendCounter = friend.AddComponent<ShotCounter>();
            pistol.Fire();
            Assert.That(friendCounter.hits, Is.EqualTo(0), "Friendly fire is off.");
            Assert.That(counter.hits, Is.EqualTo(pistol.magazineSize + 1), "The shot passes through a friend and hits what is behind.");
        }

        [UnityTest]
        public IEnumerator PointingAPistolAtTheCashier_StartsTheHoldUp()
        {
            g = new GameHarness(Vector3.zero);
            g.Player.Settle();
            RobberySite site = BuildStore(out _, out NpcBody cashier);
            yield return null;

            g.Player.Step(1f / 72f);
            Vector3 hand = g.Player.Rig.HandTransform(Hand.Right).position;
            Pistol pistol = g.MakePistol(hand);
            Assert.That(g.Player.RightGrabber.TryGrabItem(pistol.GetComponent<GrabbableItem>()), Is.True);
            Assert.That(pistol.IsHeld, Is.True);

            // Held but pointed at the floor: the cashier carries on.
            pistol.transform.rotation = Quaternion.LookRotation(Vector3.down);
            for (int i = 0; i < 30; i++) site.Tick(0.1f);
            Assert.That(site.Machine.State, Is.EqualTo(RobberyState.Idle));

            // Aimed at the cashier for a moment: hands up.
            pistol.transform.rotation = Quaternion.LookRotation(cashier.EyePosition - pistol.transform.position);
            site.Tick(0.1f);
            Assert.That(site.Machine.State, Is.EqualTo(RobberyState.Idle), "A glance is not a hold-up.");
            for (int i = 0; i < 12; i++) site.Tick(0.1f);
            Assert.That(site.Machine.State, Is.EqualTo(RobberyState.HoldUp));
            Assert.That(cashier.pose, Is.EqualTo(NpcPose.HandsUp));
        }

        [UnityTest]
        public IEnumerator Witness_ReportsOnlyWhatTheyCouldSee()
        {
            g = new GameHarness(Vector3.zero, b => b.witnessReportDelay = 0.5f);
            g.Player.AddBox(new Vector3(0f, 1.5f, 10f), new Vector3(20f, 3f, 0.3f)); // a long wall at z = 10
            g.BakeNavMesh();

            var go = new GameObject("Pedestrian");
            go.SetActive(false);
            go.transform.position = new Vector3(0f, 0f, 5f);
            var agent = go.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.radius = 0.22f;
            agent.height = 1f;
            var body = go.AddComponent<NpcBody>();
            var pedestrian = go.AddComponent<Pedestrian>();
            pedestrian.Configure(body, new Transform[0]);
            go.SetActive(true);
            yield return null;

            // A shot on the far side of the wall: nobody saw it.
            CrimeEvents.Raise(CrimeKind.Gunfire, new Vector3(0f, 0.5f, 15f), 2);
            yield return new WaitForSeconds(1.2f);
            Assert.That(g.Session.Wanted.IsWanted, Is.False, "No witness, no report.");
            Assert.That(pedestrian.WillReport, Is.False);

            // The same thing in plain view.
            CrimeEvents.Raise(CrimeKind.Gunfire, new Vector3(2f, 0.5f, 3f), 2);
            Assert.That(pedestrian.Fleeing, Is.True);
            Assert.That(g.Session.Wanted.IsWanted, Is.False, "It takes a moment to phone it in.");
            yield return new WaitForSeconds(1.2f);
            Assert.That(g.Session.Wanted.Level, Is.EqualTo(2));
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Restart_RestoresMoneyAndPurchases_ButNotWantedLevel()
        {
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smalltown-session-" + System.Guid.NewGuid().ToString("N"));
            string profileId;
            try
            {
                g = new GameHarness(Vector3.zero, null, folder);
                yield return null;
                Assert.That(g.Session.LoadStatus, Is.EqualTo(LoadStatus.NewProfile));
                g.Wallet.Apply("job:pizza:1", 300);
                Assert.That(g.Wallet.Purchase(GameBalance.SatchelUnlock, g.Balance.satchelPrice), Is.EqualTo(TransactionResult.Applied));
                g.Session.Wanted.ReportCrime(3, Vector3.zero);
                profileId = g.Session.LocalPlayerId;
                int cash = g.Wallet.Cash;
                Assert.That(g.Session.SaveNow(), Is.True);
                g.Dispose();

                // "Restart the game."
                g = new GameHarness(Vector3.zero, null, folder);
                yield return null;
                Assert.That(g.Session.LoadStatus, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(g.Session.LocalPlayerId, Is.EqualTo(profileId));
                Assert.That(g.Wallet.Cash, Is.EqualTo(cash));
                Assert.That(g.Wallet.Owns(GameBalance.SatchelUnlock), Is.True);
                Assert.That(g.Player.Bag.capacity, Is.EqualTo(g.Balance.bigSatchelCapacity), "The owned upgrade is applied on load.");
                Assert.That(g.Session.Wanted.IsWanted, Is.False, "Wanted status is session state and is not saved.");
                Assert.That(g.Wallet.Apply("job:pizza:1", 300), Is.EqualTo(TransactionResult.Duplicate), "An old payout cannot be replayed after a restart.");
            }
            finally
            {
                g?.Dispose();
                g = null;
                if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
            }
        }

        [UnityTest]
        public IEnumerator ShopOffer_BuysOnce_AndUpgradesTheSatchel()
        {
            g = new GameHarness(Vector3.zero);
            yield return null; // session Start applies unlocks
            Assert.That(g.Player.Bag.capacity, Is.EqualTo(g.Balance.baseSatchelCapacity));

            var offerObject = new GameObject("Offer");
            offerObject.transform.SetParent(g.Root.transform, false);
            var offer = offerObject.AddComponent<ShopOffer>();
            offer.unlockId = GameBalance.SatchelUnlock;
            offer.displayName = "Big Satchel";
            int purchases = 0;
            offer.purchased.AddListener(() => purchases++);

            Assert.That(offer.Buy(), Is.EqualTo(TransactionResult.InsufficientFunds));
            Assert.That(g.Player.Bag.capacity, Is.EqualTo(g.Balance.baseSatchelCapacity));

            g.Wallet.Apply("test:seed", 500);
            int before = g.Wallet.Cash;
            Assert.That(offer.Buy(), Is.EqualTo(TransactionResult.Applied));
            Assert.That(offer.Buy(), Is.EqualTo(TransactionResult.Duplicate));
            Assert.That(g.Wallet.Cash, Is.EqualTo(before - g.Balance.satchelPrice));
            Assert.That(purchases, Is.EqualTo(1));
            Assert.That(g.Player.Bag.capacity, Is.EqualTo(g.Balance.bigSatchelCapacity), "The upgrade applies immediately.");
        }
    }
}
