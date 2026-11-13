using System.Collections;
using System.Linq;
using System.IO;
using NUnit.Framework;
using SmallTown.Locomotion;
using SmallTown.RigInput;
using SmallTown.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SmallTown.Tests
{
    /// <summary>
    /// Loads the real movement scene with the real player prefab and plays it
    /// through the keyboard simulator. Catches broken prefab wiring, missing
    /// references and scene setup mistakes that unit-level tests cannot see.
    /// </summary>
    public class SceneSmokeTests
    {
        readonly InputTestFixture input = new InputTestFixture();
        Keyboard keyboard;
        int previousCaptureRate;

        [SetUp]
        public void SetUp()
        {
            RigCalibration.PersistenceEnabled = false;
            TutorialController.PersistenceEnabled = false;
            input.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            previousCaptureRate = Time.captureFramerate;
            Time.captureFramerate = 72; // fixed 72 Hz steps regardless of how fast the test machine renders
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = previousCaptureRate;
            input.TearDown();
        }

        [UnityTest]
        public IEnumerator MovementTestScene_IsPlayableWithSimulatedInput()
        {
            yield return SceneManager.LoadSceneAsync("MovementTest", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            var rig = Object.FindAnyObjectByType<PlayerRig>();
            Assert.That(rig, Is.Not.Null, "The scene should contain the player rig.");
            Assert.That(rig.Input, Is.InstanceOf<SimulatedRigInputProvider>(), "Without a headset the simulator takes over.");
            Assert.That(rig.Motor.IsGrounded, Is.True, "The player should be standing at the spawn point.");
            Assert.That(rig.Motor.BodyBottom.y, Is.EqualTo(0f).Within(0.05f));

            var tutorial = Object.FindAnyObjectByType<TutorialController>();
            Assert.That(tutorial, Is.Not.Null);
            Assert.That(tutorial.Current, Is.EqualTo(TutorialController.Step.TouchGround));

            // Hold W: scripted strides push against the real ground.
            Vector3 start = rig.Head.position;
            input.Press(keyboard.wKey);
            yield return new WaitForSeconds(2f);
            input.Release(keyboard.wKey);
            Assert.That(rig.Head.position.z - start.z, Is.GreaterThan(1.2f), "Striding should move the player forward.");
            Assert.That(tutorial.Current, Is.GreaterThan(TutorialController.Step.TouchGround), "The tutorial should notice the first paw on the ground.");
            yield return new WaitForSeconds(1f);

            // Space: two-handed jump.
            float peak = 0f;
            input.Press(keyboard.spaceKey);
            yield return null;
            input.Release(keyboard.spaceKey);
            for (int i = 0; i < 90; i++)
            {
                peak = Mathf.Max(peak, rig.Motor.BodyBottom.y);
                yield return null;
            }
            Assert.That(peak, Is.GreaterThan(0.2f), "The jump key should lift the player off the ground.");

            // E: snap turn.
            float yaw = rig.transform.eulerAngles.y;
            input.Press(keyboard.eKey);
            yield return null;
            yield return null;
            input.Release(keyboard.eKey);
            yield return null;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, rig.transform.eulerAngles.y)), Is.EqualTo(45f).Within(0.5f));

            // Skip button ends the tutorial; replay restarts it.
            tutorial.Skip();
            Assert.That(tutorial.IsComplete, Is.True);
            tutorial.Restart();
            Assert.That(tutorial.Current, Is.EqualTo(TutorialController.Step.TouchGround));

            yield return new WaitForSeconds(0.5f);
            Capture(rig, "Logs/preview-third-person.png");
        }

        [UnityTest]
        public IEnumerator TownScene_LoadsAndTheCoreLoopWorksEndToEnd()
        {
            GameSession.PersistenceEnabled = false;
            yield return SceneManager.LoadSceneAsync("Town", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            GameSession session = GameSession.Instance;
            Assert.That(session, Is.Not.Null, "The town has a game session.");
            PlayerRig rig = session.Rig;
            Assert.That(rig.Input, Is.InstanceOf<SimulatedRigInputProvider>());
            Assert.That(rig.Motor.IsGrounded, Is.True, "The player starts standing in the apartment.");
            Assert.That(Vector3.Distance(rig.Motor.BodyBottom, new Vector3(-14.5f, 0.08f, -9.5f)), Is.LessThan(0.3f));
            Assert.That(session.Wallet.Cash, Is.EqualTo(session.Balance.startingMoney));
            Assert.That(UnityEngine.AI.NavMesh.SamplePosition(new Vector3(0f, 0f, 3.5f), out _, 1f, UnityEngine.AI.NavMesh.AllAreas), Is.True, "The street is walkable for NPCs.");

            var dispatcher = Object.FindAnyObjectByType<SmallTown.Crime.PoliceDispatcher>();
            Assert.That(dispatcher.Officers.Count, Is.EqualTo(5));
            Assert.That(dispatcher.ActiveOfficers, Is.EqualTo(1));
            Capture(rig, "Logs/town-apartment.png");

            // Walk out of the front door with simulated strides.
            Vector3 start = rig.Head.position;
            input.Press(keyboard.wKey);
            yield return new WaitForSeconds(3.5f);
            input.Release(keyboard.wKey);
            Assert.That(rig.Head.position.z - start.z, Is.GreaterThan(3f), "The player can walk out of the apartment.");
            yield return new WaitForSeconds(1f);
            Capture(rig, "Logs/town-street-play.png");

            // --- The pizza job, using the scene's real stations and prefabs.
            var job = Object.FindAnyObjectByType<SmallTown.Jobs.PizzaDeliveryJob>();
            int money = session.Wallet.Cash;
            job.TakeOrder();
            Assert.That(job.State, Is.EqualTo(SmallTown.Jobs.JobState.Active));
            SmallTown.Jobs.Pizza pizza = job.ActivePizza;
            Assert.That(pizza, Is.Not.Null);
            yield return new WaitForSeconds(0.3f);
            var bins = Object.FindObjectsByType<SmallTown.Jobs.IngredientBin>(FindObjectsSortMode.None);
            Assert.That(bins.Length, Is.EqualTo(3));
            foreach (string needed in pizza.Required.ToArray())
            {
                SmallTown.Jobs.Topping topping = null;
                foreach (var bin in bins)
                {
                    if (bin.Current != null && bin.Current.toppingId == needed) topping = bin.Current;
                }
                Assert.That(topping, Is.Not.Null, "A bin offers " + needed);
                Assert.That(pizza.TryAdd(topping), Is.True);
            }
            yield return null;
            yield return null;
            Assert.That(job.Current, Is.EqualTo(SmallTown.Jobs.PizzaDeliveryJob.Stage.Cook));

            pizza.Item.Body.isKinematic = true;
            pizza.transform.position = GameObject.Find("OvenZone").transform.position;
            yield return new WaitForSeconds(session.Balance.pizzaCookSeconds + 0.5f);
            Assert.That(job.Current, Is.EqualTo(SmallTown.Jobs.PizzaDeliveryJob.Stage.Box));
            pizza.transform.position = GameObject.Find("BoxingZone").transform.position;
            yield return null;
            yield return null;
            Assert.That(job.Current, Is.EqualTo(SmallTown.Jobs.PizzaDeliveryJob.Stage.Deliver));
            Vector3 drop = job.Target.Zone.transform.position;
            for (int i = 0; i < 60 && job.State == SmallTown.Jobs.JobState.Active; i++)
            {
                pizza.Item.Body.position = drop;
                pizza.transform.position = drop;
                yield return null;
            }
            Assert.That(session.Wallet.Cash, Is.EqualTo(money + session.Balance.pizzaPayout + session.Balance.pizzaSpeedTip),
                "Delivery pays the wallet. stage=" + job.Current + " state=" + job.State + " pizza=" + (pizza != null ? pizza.transform.position.ToString("F2") : "destroyed") +
                " zone=" + drop.ToString("F2") + " contains=" + (pizza != null && job.Target != null && job.Target.Zone.Contains(pizza.transform.position)));
            Assert.That(job.State, Is.EqualTo(SmallTown.Jobs.JobState.Available));

            // --- Buy the pistol at the pawn shop once there is enough money.
            session.Wallet.Apply("test:seed", 200);
            SmallTown.Crime.ShopOffer pistolOffer = null;
            foreach (var offer in Object.FindObjectsByType<SmallTown.Crime.ShopOffer>(FindObjectsSortMode.None))
            {
                if (offer.unlockId == SmallTown.Economy.GameBalance.PistolUnlock) pistolOffer = offer;
            }
            Assert.That(pistolOffer.Buy(), Is.EqualTo(SmallTown.Economy.TransactionResult.Applied));
            yield return null;
            Assert.That(SmallTown.Items.Pistol.All.Count, Is.EqualTo(1), "Buying the pistol puts one on the counter.");

            // --- Rob the store.
            var site = Object.FindAnyObjectByType<SmallTown.Crime.RobberySite>();
            Vector3 store = site.Zone.transform.position;
            rig.TeleportFeet(new Vector3(store.x, 0.08f, store.z), 90f);
            yield return new WaitForSeconds(0.5f);
            Assert.That(site.Stock.Count, Is.GreaterThan(10), "Shelves are stocked.");
            Assert.That(site.StartHoldUp(), Is.True);
            // Watch the next few seconds and keep the peaks: a nearby officer may well
            // arrest the robber before the end, which is the game working.
            var patrolCar = Object.FindAnyObjectByType<SmallTown.Crime.PatrolCar>();
            int peakBundles = 0, peakWanted = 0, peakOfficers = 0;
            bool carResponded = false;
            float until = Time.time + 9.5f;
            while (Time.time < until)
            {
                int bundles = 0;
                foreach (var loot in SmallTown.Items.LootItem.All)
                {
                    if (loot.isCash) bundles++;
                }
                peakBundles = Mathf.Max(peakBundles, bundles);
                peakWanted = Mathf.Max(peakWanted, session.Wanted.Level);
                peakOfficers = Mathf.Max(peakOfficers, dispatcher.ActiveOfficers);
                carResponded |= patrolCar.Responding;
                yield return null;
            }
            string history = string.Join(" / ", HudToast.History);
            Assert.That(peakBundles, Is.EqualTo(session.Balance.registerBundles), history);
            Assert.That(peakWanted, Is.GreaterThanOrEqualTo(2), "The alarm makes the robber wanted. Messages: " + history);
            Assert.That(peakOfficers, Is.GreaterThanOrEqualTo(3), "The alarm brings a squad.");
            Assert.That(carResponded, Is.True, "The patrol car rolls at level 2.");
            Debug.Log("[SmallTown] Town test messages: " + history);
            Capture(rig, "Logs/town-robbery.png");
        }

        /// <summary>Renders the running scene from behind the player. Not an assertion; a picture for humans.</summary>
        static void Capture(PlayerRig rig, string path)
        {
            var cameraObject = new GameObject("PreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            Vector3 head = rig.Head.position;
            Quaternion facing = rig.BodyYaw;
            cameraObject.transform.position = head + facing * new Vector3(0.9f, 0.45f, -1.5f);
            cameraObject.transform.LookAt(head + facing * new Vector3(0f, -0.3f, 0.6f));
            camera.fieldOfView = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.56f, 0.83f, 0.96f);

            var target = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
            camera.targetTexture = target;
            // In a headless run the SRP batcher has not uploaded material data before the
            // first manual render, which flattens every colour. Refresh it and warm up.
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            camera.Render();
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = true;
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, image.EncodeToPNG());

            camera.targetTexture = null;
            Object.Destroy(target);
            Object.Destroy(image);
            Object.Destroy(cameraObject);
        }
    }
}
