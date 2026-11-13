using NUnit.Framework;
using SmallTown.Interaction;
using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown.Tests
{
    /// <summary>Grabbing, throwing, the bag, and how carrying interacts with hand locomotion.</summary>
    public class InteractionTests
    {
        const float Dt = 1f / 72f;
        RigHarness h;
        SimulationMode previousMode;

        [SetUp]
        public void SetUp()
        {
            previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = previousMode;
            h?.Dispose();
            h = null;
        }

        /// <summary>One rendered frame plus one physics step, the way the game loop interleaves them.</summary>
        void Frame(params GrabbableItem[] items)
        {
            h.Step(Dt);
            foreach (var item in items) item.StepHeld(Dt);
            Physics.Simulate(Dt);
        }

        Vector3 HandWorld(Hand hand) => h.Rig.HandTransform(hand).position;

        GrabbableItem ItemAtHand(Hand hand, bool storable = true)
        {
            h.Step(Dt);
            var item = h.AddItem(HandWorld(hand), 0.1f, 1f, false, storable);
            item.Body.useGravity = false;
            return item;
        }

        void Grip(Hand hand, bool down)
        {
            if (hand == Hand.Left) h.Input.leftGrip = down ? 1f : 0f;
            else h.Input.rightGrip = down ? 1f : 0f;
        }

        [Test]
        public void Grip_GrabsNearbyItem_AndReleaseDropsIt()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            var item = ItemAtHand(Hand.Right);

            Grip(Hand.Right, true);
            Frame(item);
            Assert.That(h.RightGrabber.Held, Is.EqualTo(item));
            Assert.That(item.State, Is.EqualTo(ItemState.Held));
            Assert.That(item.gameObject.layer, Is.EqualTo(GameLayers.HeldItem), "Held items leave the locomotion layers.");

            Grip(Hand.Right, false);
            Frame(item);
            Assert.That(h.RightGrabber.Held, Is.Null);
            Assert.That(item.State, Is.EqualTo(ItemState.Free));
        }

        [Test]
        public void HeldItem_FollowsHand()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            var item = ItemAtHand(Hand.Right);
            Grip(Hand.Right, true);
            Frame(item);

            for (int i = 0; i < 40; i++)
            {
                h.SetHand(Hand.Right, RigHarness.RestRight + new Vector3(0f, 0.3f, 0.2f) * (i / 40f));
                Frame(item);
            }
            for (int i = 0; i < 10; i++) Frame(item);
            Assert.That(Vector3.Distance(item.Body.position, HandWorld(Hand.Right)), Is.LessThan(0.03f));
        }

        [Test]
        public void ReleasingMidSwing_ThrowsWithHandVelocity()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            var item = ItemAtHand(Hand.Right);
            Grip(Hand.Right, true);
            Frame(item);

            // Swing forward at 4 m/s and let go mid-swing.
            Vector3 from = new Vector3(0.2f, -0.3f, -0.1f);
            for (int i = 1; i <= 10; i++)
            {
                h.SetHand(Hand.Right, from + Vector3.forward * (4f * Dt * i));
                Frame(item);
            }
            Grip(Hand.Right, false);
            h.SetHand(Hand.Right, from + Vector3.forward * (4f * Dt * 11));
            Frame(item);

            Assert.That(item.State, Is.EqualTo(ItemState.Free));
            Assert.That(item.Body.linearVelocity.z, Is.InRange(3f, 6f), "Throw speed should follow the swing.");
        }

        [Test]
        public void OccupiedHand_DoesNotPropel_FreeHandStillDoes()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            var item = ItemAtHand(Hand.Right);
            Grip(Hand.Right, true);
            Frame(item);
            Vector3 start = h.Head.position;

            // The carrying hand drags along the floor: nothing happens.
            Vector3 plantedRight = h.Plant(Hand.Right, 0.4f, Dt);
            h.Stroke(Hand.Right, plantedRight, new Vector3(plantedRight.x, plantedRight.y - 0.05f, -0.2f), 0.25f, Dt);
            Assert.That(Vector3.Distance(start, h.Head.position), Is.LessThan(0.02f), "A hand that is carrying must not propel.");
            Assert.That(h.Motor.HandPosition(Hand.Right).y, Is.GreaterThan(0.04f), "It still cannot pass through the floor.");
            h.SetHand(Hand.Right, RigHarness.RestRight);

            // The free hand does the same stroke: the body moves.
            Vector3 plantedLeft = h.Plant(Hand.Left, 0.4f, Dt);
            h.Stroke(Hand.Left, plantedLeft, new Vector3(plantedLeft.x, plantedLeft.y, -0.2f), 0.25f, Dt);
            Assert.That((h.Head.position - start).z, Is.GreaterThan(0.5f), "One-handed movement while carrying.");
            Assert.That(h.RightGrabber.Held, Is.EqualTo(item), "The item is still in hand.");
        }

        [Test]
        public void ItemsAreNotLocomotionSurfaces()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            // A crate sitting on the floor right where the hand will press.
            Vector3 press = h.Head.position + new Vector3(0.2f, -0.85f, 0.35f);
            var crate = h.AddItem(new Vector3(press.x, 0.15f, press.z), 0.3f);
            crate.Body.isKinematic = true;
            float headY = h.Head.position.y;

            h.Stroke(Hand.Right, RigHarness.RestRight, new Vector3(0.2f, -0.6f, 0.35f), 0.2f, Dt);
            Assert.That(h.Head.position.y, Is.EqualTo(headY).Within(0.01f),
                "Pressing on a loose item must not lift the player; only the world does.");
        }

        [Test]
        public void SecondHand_TakesLightItemFromFirst()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            h.SetHand(Hand.Left, new Vector3(-0.05f, -0.4f, 0.3f));
            h.SetHand(Hand.Right, new Vector3(0.05f, -0.4f, 0.3f));
            var item = ItemAtHand(Hand.Right);

            Grip(Hand.Right, true);
            Frame(item);
            Assert.That(h.RightGrabber.Held, Is.EqualTo(item));

            Grip(Hand.Left, true);
            Frame(item);
            Assert.That(h.LeftGrabber.Held, Is.EqualTo(item), "The other hand takes over.");
            Assert.That(h.RightGrabber.Held, Is.Null);
            Assert.That(item.HolderCount, Is.EqualTo(1));
        }

        [Test]
        public void ReleasingInsideBag_StoresItem_AndGripTakesItBack()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            var item = ItemAtHand(Hand.Right);
            Grip(Hand.Right, true);
            Frame(item);

            int storeSounds = Count(FeedbackSound.Store);
            // Move the hand into the hip bag and let go.
            Vector3 bagHeadRelative = h.Bag.bodyOffset;
            h.SetHand(Hand.Right, bagHeadRelative);
            Frame(item);
            Assert.That(h.Bag.Contains(HandWorld(Hand.Right)), Is.True);
            Grip(Hand.Right, false);
            Frame(item);

            Assert.That(item.State, Is.EqualTo(ItemState.Stored));
            Assert.That(h.Bag.Items, Has.Count.EqualTo(1));
            Assert.That(h.RightGrabber.Held, Is.Null);
            Assert.That(Count(FeedbackSound.Store), Is.EqualTo(storeSounds + 1), "Storing gives clear feedback.");
            Assert.That(h.Motor.GetHand(Hand.Right).canPush, Is.True, "The hand is free to push again.");

            // Empty hand in the bag + grip takes it back out.
            Grip(Hand.Left, false);
            h.SetHand(Hand.Left, bagHeadRelative);
            Frame(item);
            Grip(Hand.Left, true);
            Frame(item);
            Assert.That(h.LeftGrabber.Held, Is.EqualTo(item));
            Assert.That(item.State, Is.EqualTo(ItemState.Held));
            Assert.That(h.Bag.Items, Is.Empty);
        }

        [Test]
        public void Bag_RejectsWhenFull_AndRejectsUnstorableItems()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            h.Bag.capacity = 2;
            Assert.That(h.Bag.TryStore(h.AddItem(new Vector3(5f, 1f, 0f))), Is.True);
            Assert.That(h.Bag.TryStore(h.AddItem(new Vector3(5f, 1f, 1f))), Is.True);

            var extra = h.AddItem(new Vector3(5f, 1f, 2f));
            int rejects = Count(FeedbackSound.Reject);
            Assert.That(h.Bag.TryStore(extra), Is.False);
            Assert.That(extra.State, Is.EqualTo(ItemState.Free));
            Assert.That(Count(FeedbackSound.Reject), Is.EqualTo(rejects + 1));

            h.Bag.capacity = 10;
            var bulky = h.AddItem(new Vector3(5f, 1f, 3f), 0.5f, 20f, true, false);
            Assert.That(h.Bag.TryStore(bulky), Is.False, "Large items do not fit in the bag.");
            Assert.That(h.Bag.TryStore(extra), Is.True);
            Assert.That(h.Bag.TryStore(extra), Is.False, "The same item cannot be stored twice.");
            Assert.That(h.Bag.UsedSpace, Is.EqualTo(3));
        }

        [Test]
        public void ReleasingFullBag_KeepsItemInWorld()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            h.Bag.capacity = 1;
            h.Bag.TryStore(h.AddItem(new Vector3(5f, 1f, 0f)));

            var item = ItemAtHand(Hand.Right);
            Grip(Hand.Right, true);
            Frame(item);
            h.SetHand(Hand.Right, h.Bag.bodyOffset);
            Frame(item);
            Grip(Hand.Right, false);
            Frame(item);
            Assert.That(item.State, Is.EqualTo(ItemState.Free), "A rejected item is dropped, never lost.");
            Assert.That(h.Bag.Items, Has.Count.EqualTo(1));
        }

        [Test]
        public void HeavyItem_OneHandCannotLift_TwoHandsCan()
        {
            float Lift(bool twoHands)
            {
                using (var rig = new RigHarness(Vector3.zero))
                {
                    h = rig;
                    rig.Settle();
                    // 26 kg trunk on the floor in front of the player.
                    var trunk = rig.AddItem(new Vector3(0f, 0.15f, 0.45f), 0.3f, 26f, true, false);
                    trunk.transform.localScale = new Vector3(0.6f, 0.3f, 0.3f);
                    Physics.SyncTransforms();

                    rig.SetHand(Hand.Right, new Vector3(0.2f, -0.6f, 0.45f));
                    rig.SetHand(Hand.Left, twoHands ? new Vector3(-0.2f, -0.6f, 0.45f) : new Vector3(-0.3f, -0.3f, -0.2f));
                    Frame(trunk);
                    rig.Input.rightGrip = 1f;
                    rig.Input.leftGrip = twoHands ? 1f : 0f;
                    Frame(trunk);
                    Assert.That(trunk.HolderCount, Is.EqualTo(twoHands ? 2 : 1));
                    Assert.That(trunk.Body.useGravity, Is.True, "Heavy items stay under gravity while held.");

                    // Raise the hand(s) 35 cm over one second and hold.
                    for (int i = 1; i <= 144; i++)
                    {
                        float lift = Mathf.Min(1f, i / 72f) * 0.35f;
                        rig.SetHand(Hand.Right, new Vector3(0.2f, -0.6f + lift, 0.45f));
                        if (twoHands) rig.SetHand(Hand.Left, new Vector3(-0.2f, -0.6f + lift, 0.45f));
                        Frame(trunk);
                    }
                    float height = trunk.Body.worldCenterOfMass.y;
                    h = null;
                    return height;
                }
            }

            float oneHand = Lift(false);
            float bothHands = Lift(true);
            Assert.That(oneHand, Is.LessThan(0.3f), "One hand can tip or drag a heavy item but not carry it.");
            Assert.That(bothHands, Is.GreaterThan(0.38f), "Two hands lift it.");
        }

        [Test]
        public void DisablingGrabber_ReleasesItem_NothingStaysGrabbedForever()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            var item = ItemAtHand(Hand.Right);
            Grip(Hand.Right, true);
            Frame(item);
            Assert.That(item.IsHeld, Is.True);

            h.RightGrabber.enabled = false; // e.g. the owning player disconnects
            Assert.That(item.IsHeld, Is.False);
            Assert.That(item.State, Is.EqualTo(ItemState.Free));
        }

        static int Count(FeedbackSound sound)
        {
            GameFeedback.PlayCounts.TryGetValue(sound, out int count);
            return count;
        }
    }
}
