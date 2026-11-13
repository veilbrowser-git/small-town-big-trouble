using NUnit.Framework;
using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown.Tests
{
    /// <summary>
    /// Integration tests for hand propulsion. Controller motion is scripted and
    /// the real solver runs against real colliders. These verify the simulation;
    /// they do not replace testing how it feels in a headset.
    /// </summary>
    public class LocomotionTests
    {
        const float Dt = 1f / 72f;
        RigHarness h;

        [TearDown]
        public void TearDown()
        {
            h?.Dispose();
            h = null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        [Test]
        public void Standing_RestsOnGround_WithoutDrifting()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle(1f);
            Vector3 start = h.Head.position;
            h.Run(2f, Dt);
            Assert.That(h.Motor.IsGrounded, Is.True);
            Assert.That(Vector3.Distance(start, h.Head.position), Is.LessThan(0.005f));
            Assert.That(h.Motor.BodyBottom.y, Is.EqualTo(0f).Within(0.03f));
        }

        [Test]
        public void SwingingHandsThroughAir_DoesNotPropel()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            Vector3 start = h.Head.position;
            h.Run(2f, Dt, t =>
            {
                float swing = Mathf.Sin(t * 14f) * 0.45f;
                h.SetHand(Hand.Left, new Vector3(-0.25f, -0.25f, swing));
                h.SetHand(Hand.Right, new Vector3(0.25f, -0.25f, -swing));
            });
            Assert.That(Vector3.Distance(start, h.Head.position), Is.LessThan(0.01f), "Air swings must never move the body.");
            Assert.That(h.Motor.AnyHandSupporting, Is.False);
        }

        [Test]
        public void DraggingHandBackAlongFloor_MovesBodyForward()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            Vector3 start = h.Head.position;

            Vector3 planted = h.Plant(Hand.Right, 0.4f, Dt);
            Assert.That(h.Motor.IsHandTouching(Hand.Right), Is.True, "The planted hand should be in contact.");
            Assert.That(Vector3.Distance(start, h.Head.position), Is.LessThan(0.05f), "A gentle plant barely moves the body.");
            h.Stroke(Hand.Right, planted, new Vector3(planted.x, planted.y, -0.2f), 0.25f, Dt);

            Vector3 moved = h.Head.position - start;
            Assert.That(moved.z, Is.GreaterThan(0.5f), "Body should move opposite to the hand drag.");
            Assert.That(Mathf.Abs(moved.x), Is.LessThan(0.05f));

            // The virtual hand stayed planted while the controller travelled.
            Assert.That(h.Motor.HandPosition(Hand.Right).y, Is.GreaterThan(h.Settings.handRadius * 0.9f));
        }

        [Test]
        public void ReleasingAfterPush_KeepsControlledMomentum()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            Vector3 planted = h.Plant(Hand.Right, 0.4f, Dt);
            Vector3 back = new Vector3(planted.x, planted.y, -0.2f);
            h.Stroke(Hand.Right, planted, back, 0.25f, Dt); // 2.4 m/s
            h.Stroke(Hand.Right, back, new Vector3(0.2f, -0.5f, -0.2f), 0.05f, Dt);

            Assert.That(h.Motor.AnyHandSupporting, Is.False);
            float speed = Flat(h.Motor.Velocity).magnitude;
            Assert.That(speed, Is.InRange(1.6f, 3.2f), "Release speed should be near push speed times the multiplier.");
            Assert.That(h.Motor.Velocity.magnitude, Is.LessThanOrEqualTo(h.Settings.maxLaunchSpeed + 0.01f));

            // Momentum decays on the ground instead of lasting forever.
            h.RestHands();
            h.Run(3f, Dt);
            Assert.That(Flat(h.Motor.Velocity).magnitude, Is.LessThan(0.05f));
        }

        [Test]
        public void ViolentPush_IsClampedToMaxLaunchSpeed()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            Vector3 planted = h.Plant(Hand.Right, 0.4f, Dt);
            Vector3 back = new Vector3(planted.x, planted.y, -0.3f);
            h.Stroke(Hand.Right, planted, back, 0.05f, Dt); // 14 m/s
            h.Stroke(Hand.Right, back, new Vector3(0.2f, -0.5f, -0.3f), 0.03f, Dt);
            Assert.That(h.Motor.Velocity.magnitude, Is.GreaterThan(3f), "A violent push should still launch.");
            Assert.That(h.Motor.Velocity.magnitude, Is.LessThanOrEqualTo(h.Settings.maxLaunchSpeed + 0.01f));
        }

        [Test]
        public void AlternatingPushes_RunAcrossTheGround()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            Vector3 start = h.Head.position;
            h.Run(3f, Dt, t => h.ApplyStride(t));
            float distance = (h.Head.position - start).z;
            Assert.That(distance, Is.GreaterThan(3f), "Three seconds of strides should cover real ground.");
            Assert.That(distance, Is.LessThan(3f * h.Settings.maxLaunchSpeed));
        }

        [Test]
        public void StrongDownwardPush_Jumps_ThenLands()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            h.StrokeBoth(RigHarness.RestRight, new Vector3(0.2f, -0.6f, 0.15f), 0.1f, Dt);
            h.StrokeBoth(new Vector3(0.2f, -0.6f, 0.15f), new Vector3(0.2f, -0.98f, 0.15f), 0.12f, Dt);
            h.StrokeBoth(new Vector3(0.2f, -0.98f, 0.15f), new Vector3(0.2f, -0.4f, 0.15f), 0.06f, Dt);

            float peak = 0f;
            bool airborne = false;
            h.Run(1.5f, Dt, _ =>
            {
                peak = Mathf.Max(peak, h.Motor.BodyBottom.y);
                if (!h.Motor.IsGrounded && !h.Motor.AnyHandSupporting) airborne = true;
            });
            Assert.That(airborne, Is.True);
            Assert.That(peak, Is.GreaterThan(0.4f), "A hard two-handed shove should clear at least 40 cm.");
            Assert.That(h.Motor.IsGrounded, Is.True, "The player should have landed again.");
            Assert.That(h.Motor.BodyBottom.y, Is.EqualTo(0f).Within(0.03f));
        }

        [Test]
        public void Hand_CannotPassThroughWall_AndPushesBodyAway()
        {
            h = new RigHarness(Vector3.zero);
            h.AddBox(new Vector3(0f, 1.5f, 0.75f), new Vector3(6f, 3f, 0.3f)); // near face at z = 0.6
            h.Settle();
            Vector3 start = h.Head.position;

            h.Stroke(Hand.Right, RigHarness.RestRight, new Vector3(0.2f, -0.2f, 0.3f), 0.1f, Dt);
            h.Stroke(Hand.Right, new Vector3(0.2f, -0.2f, 0.3f), new Vector3(0.2f, -0.2f, 0.8f), 0.3f, Dt);

            float limit = 0.6f - h.Settings.handRadius;
            Assert.That(h.Motor.HandPosition(Hand.Right).z, Is.LessThanOrEqualTo(limit + 0.002f), "The virtual hand must stop at the wall.");
            Assert.That((h.Head.position - start).z, Is.LessThan(-0.15f), "Pushing a wall should push the body away from it.");
        }

        [Test]
        public void Body_CannotBeWalkedThroughWall()
        {
            h = new RigHarness(Vector3.zero);
            h.AddBox(new Vector3(0f, 1.5f, 0.75f), new Vector3(6f, 3f, 0.3f));
            h.Settle();
            float startRigZ = h.Root.transform.position.z;

            // The player physically walks 1 m forward in their room, hands held back.
            h.Run(1f, Dt, t =>
            {
                h.SetHeadLocal(new Vector3(0f, RigHarness.HeadHeight, t));
                h.SetHand(Hand.Left, new Vector3(-0.2f, -0.4f, -0.15f));
                h.SetHand(Hand.Right, new Vector3(0.2f, -0.4f, -0.15f));
            });

            float limit = 0.6f - h.Settings.bodyRadius;
            Assert.That(h.Head.position.z, Is.LessThanOrEqualTo(limit + 0.002f), "The head must stay on the near side of the wall.");
            Assert.That(h.Root.transform.position.z, Is.LessThan(startRigZ - 0.3f), "The rig is pushed back instead.");
        }

        [Test]
        public void InsideCorner_DoesNotTrapOrLeak()
        {
            h = new RigHarness(Vector3.zero);
            h.AddBox(new Vector3(0f, 1.5f, 1.15f), new Vector3(6f, 3f, 0.3f));   // face z = 1.0
            h.AddBox(new Vector3(1.15f, 1.5f, 0f), new Vector3(0.3f, 3f, 6f));   // face x = 1.0
            h.Settle();
            // Walk diagonally into the corner.
            h.Run(2f, Dt, t =>
            {
                h.SetHeadLocal(new Vector3(t, RigHarness.HeadHeight, t));
                h.SetHand(Hand.Left, new Vector3(-0.2f, -0.4f, -0.15f));
                h.SetHand(Hand.Right, new Vector3(0.1f, -0.4f, -0.15f));
            });
            float limit = 1f - h.Settings.bodyRadius + 0.003f;
            Assert.That(h.Head.position.x, Is.LessThanOrEqualTo(limit));
            Assert.That(h.Head.position.z, Is.LessThanOrEqualTo(limit));
            Assert.That(h.Head.position.x, Is.GreaterThan(0.6f), "The body should have reached the corner.");
            Assert.That(h.Head.position.z, Is.GreaterThan(0.6f));
        }

        [Test]
        public void Motion_IsConsistentAcrossFrameRates()
        {
            float Distance(float dt)
            {
                using (var rig = new RigHarness(Vector3.zero))
                {
                    rig.Settle();
                    Vector3 start = rig.Head.position;
                    rig.Run(3f, dt, t => rig.ApplyStride(t));
                    return (rig.Head.position - start).z;
                }
            }

            float at72 = Distance(1f / 72f);
            float at120 = Distance(1f / 120f);
            float at45 = Distance(1f / 45f);
            Assert.That(at120, Is.EqualTo(at72).Within(at72 * 0.12f), "120 Hz vs 72 Hz");
            Assert.That(at45, Is.EqualTo(at72).Within(at72 * 0.15f), "45 Hz (hitching) vs 72 Hz");
        }

        [Test]
        public void MovingPlatform_CarriesPlayer_WithoutLaunching()
        {
            h = new RigHarness(new Vector3(30f, 0.3f, 0f));
            GameObject platform = h.AddBox(new Vector3(30f, 0f, 0f), new Vector3(3f, 0.6f, 3f));
            h.Rig.TeleportFeet(new Vector3(30f, 0.3f, 0f));
            h.Settle();
            float startOffset = h.Head.position.x - platform.transform.position.x;

            h.Run(2f, Dt, _ => platform.transform.position += Vector3.right * Dt); // 1 m/s
            Assert.That(platform.transform.position.x, Is.EqualTo(32f).Within(0.02f));
            Assert.That(h.Head.position.x - platform.transform.position.x, Is.EqualTo(startOffset).Within(0.05f), "Player rides the platform.");
            Assert.That(h.Motor.Velocity.magnitude, Is.LessThan(0.2f), "Riding is carry, not self-propulsion.");

            // Rest a hand on the moving platform, then lift it: no launch.
            h.Run(1f, Dt, t =>
            {
                platform.transform.position += Vector3.right * Dt;
                float depth = Mathf.Lerp(-0.5f, h.PressDepth, Mathf.Clamp01(t / 0.3f));
                if (t > 0.7f) depth = -0.45f;
                h.SetHand(Hand.Right, new Vector3(0.2f, depth, 0.2f));
            });
            Assert.That(h.Head.position.x - platform.transform.position.x, Is.EqualTo(startOffset).Within(0.08f));
            Assert.That(Flat(h.Motor.Velocity).magnitude, Is.LessThan(0.3f), "Platform motion must not count as a hand push.");

            // The platform stops: the rider is not flung off.
            Vector3 stopped = h.Head.position;
            h.RestHands();
            h.Run(0.5f, Dt);
            Assert.That(Vector3.Distance(Flat(stopped), Flat(h.Head.position)), Is.LessThan(0.1f));
        }

        [Test]
        public void HandTrackingLoss_DoesNotLaunch()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            h.Plant(Hand.Left, 0.2f, Dt);
            h.Run(0.2f, Dt);
            Vector3 start = Flat(h.Head.position);

            // Tracking drops and the controller pose is garbage, then it returns somewhere else.
            h.Input.leftTracked = false;
            h.SetHand(Hand.Left, new Vector3(1.4f, 0.6f, 1.2f));
            h.Run(0.15f, Dt);
            h.Input.leftTracked = true;
            h.SetHand(Hand.Left, new Vector3(-0.3f, -0.3f, 0.5f));
            h.Run(0.3f, Dt);

            Assert.That(Vector3.Distance(start, Flat(h.Head.position)), Is.LessThan(0.02f));
            Assert.That(Flat(h.Motor.Velocity).magnitude, Is.LessThan(0.1f));
        }

        [Test]
        public void ControllerTeleportGlitch_DoesNotLaunch()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            Vector3 planted = h.Plant(Hand.Right, 0.2f, Dt);
            Vector3 start = Flat(h.Head.position);
            // One-frame 0.7 m jump of a planted hand: a tracking glitch, not a push.
            h.SetHand(Hand.Right, new Vector3(planted.x, planted.y, -0.5f));
            h.Step(Dt);
            h.Run(0.3f, Dt);
            Assert.That(Vector3.Distance(start, Flat(h.Head.position)), Is.LessThan(0.05f));
        }

        [Test]
        public void Recentre_KeepsBodyInPlace()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            Vector3 start = h.Head.position;

            Vector3 jump = new Vector3(1.5f, 0f, -1f);
            h.SetHeadLocal(new Vector3(0f, RigHarness.HeadHeight, 0f) + jump);
            h.RestHands();
            h.Step(Dt);
            h.Run(0.3f, Dt);

            Assert.That(Vector3.Distance(start, h.Head.position), Is.LessThan(0.02f), "A recentre must not move the player through the world.");
            Assert.That(h.Motor.Velocity.magnitude, Is.LessThan(0.1f));
        }

        [Test]
        public void SnapTurn_PivotsAroundHead_OncePerFlick()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            Vector3 headBefore = h.Head.position;
            float yawBefore = h.Root.transform.eulerAngles.y;

            h.Input.rightStick = new Vector2(1f, 0f);
            h.Run(0.5f, Dt); // held: still only one turn
            float turned = Mathf.DeltaAngle(yawBefore, h.Root.transform.eulerAngles.y);
            Assert.That(turned, Is.EqualTo(h.Settings.snapTurnDegrees).Within(0.01f));

            h.Input.rightStick = Vector2.zero;
            h.Step(Dt);
            h.Input.rightStick = new Vector2(-1f, 0f);
            h.Step(Dt);
            h.Input.rightStick = Vector2.zero;
            h.Run(0.3f, Dt);

            Assert.That(Mathf.DeltaAngle(yawBefore, h.Root.transform.eulerAngles.y), Is.EqualTo(0f).Within(0.01f));
            Assert.That(Vector3.Distance(headBefore, h.Head.position), Is.LessThan(0.01f), "Turning pivots on the head.");
            Assert.That(h.Motor.Velocity.magnitude, Is.LessThan(0.1f));
        }

        [Test]
        public void SnapTurn_WhileHandIsPlanted_DoesNotLaunch()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            h.Plant(Hand.Right, 0.3f, Dt);
            Assert.That(h.Motor.IsHandTouching(Hand.Right), Is.True);
            Vector3 start = Flat(h.Head.position);
            h.Input.rightStick = new Vector2(1f, 0f);
            h.Run(0.4f, Dt);
            Assert.That(Vector3.Distance(start, Flat(h.Head.position)), Is.LessThan(0.03f));
        }

        [Test]
        public void GentleSlope_Holds_SteepSlope_Slides()
        {
            float Slide(float angle)
            {
                using (var rig = new RigHarness(Vector3.zero, 0f, false))
                {
                    Quaternion tilt = Quaternion.Euler(-angle, 0f, 0f);
                    Vector3 centre = new Vector3(0f, 0f, 0f);
                    rig.AddBox(centre, new Vector3(20f, 1f, 40f), tilt);
                    Vector3 surface = centre + tilt * Vector3.up * 0.5f;
                    rig.Rig.TeleportFeet(surface + Vector3.up * 0.05f);
                    rig.Run(0.5f, Dt);
                    Vector3 start = rig.Head.position;
                    rig.Run(2f, Dt);
                    return Vector3.Distance(start, rig.Head.position);
                }
            }

            Assert.That(Slide(15f), Is.LessThan(0.03f), "A walkable slope should hold the player.");
            Assert.That(Slide(60f), Is.GreaterThan(0.5f), "A slope past the limit should shed the player.");
        }

        [Test]
        public void Stairs_AreClimbedByStriding()
        {
            h = new RigHarness(Vector3.zero);
            for (int i = 0; i < 5; i++)
            {
                float height = 0.15f * (i + 1);
                h.AddBox(new Vector3(0f, height / 2f, 1.5f + 0.4f * i + 0.2f), new Vector3(3f, height, 0.4f));
            }
            h.AddBox(new Vector3(0f, 0.375f, 18.5f), new Vector3(3f, 0.75f, 30f)); // long landing at the top
            h.Settle();
            h.Run(5f, Dt, t => h.ApplyStride(t));
            h.RestHands();
            h.Run(1f, Dt);
            Assert.That(h.Motor.BodyBottom.y, Is.GreaterThan(0.6f), "Striding into stairs should carry the player up them.");
            Assert.That(h.Head.position.z, Is.GreaterThan(3.3f));
        }

        [Test]
        public void LowCeiling_BlocksStanding_AllowsCrouching()
        {
            h = new RigHarness(Vector3.zero);
            h.AddBox(new Vector3(0f, 0.72f + 0.25f, 2.5f), new Vector3(4f, 0.5f, 3f)); // underside at 0.72, z 1..4
            h.Settle();

            Vector3 handsUp = new Vector3(0.2f, -0.1f, -0.1f);
            void Hands()
            {
                h.SetHand(Hand.Right, handsUp);
                h.SetHand(Hand.Left, new Vector3(-handsUp.x, handsUp.y, handsUp.z));
            }

            // Standing, walk forward physically: the head stops at the tunnel mouth.
            h.Run(1.5f, Dt, t =>
            {
                h.SetHeadLocal(new Vector3(0f, RigHarness.HeadHeight, t));
                Hands();
            });
            Assert.That(h.Head.position.z, Is.LessThan(1f - h.Settings.bodyRadius + 0.01f));

            // Duck 40 cm in real life: the body shortens instead of the rig rising.
            float rigY = h.Root.transform.position.y;
            h.Run(0.5f, Dt, t =>
            {
                h.SetHeadLocal(new Vector3(0f, RigHarness.HeadHeight - 0.8f * t, 1.5f));
                Hands();
            });
            Assert.That(h.Root.transform.position.y, Is.EqualTo(rigY).Within(0.02f));
            Assert.That(h.Motor.BodyLength, Is.LessThan(h.Settings.standingBodyLength - 0.3f));

            // Crouched, keep walking: now it fits.
            h.Run(2f, Dt, t =>
            {
                h.SetHeadLocal(new Vector3(0f, RigHarness.HeadHeight - 0.4f, 1.5f + t * 2f));
                Hands();
            });
            Assert.That(h.Head.position.z, Is.GreaterThan(4.2f), "Crouching should let the player through the tunnel.");

            // Stand back up in the open: full height returns.
            h.Run(0.5f, Dt, t =>
            {
                h.SetHeadLocal(new Vector3(0f, RigHarness.HeadHeight - 0.4f + 0.8f * t, 5.5f));
                Hands();
            });
            Assert.That(h.Motor.BodyLength, Is.EqualTo(h.Settings.standingBodyLength).Within(0.01f));
        }

        [Test]
        public void GripOnClimbableSurface_Holds_OrdinaryWallDoesNot()
        {
            float Drop(bool climbable, out float pulledUp)
            {
                using (var rig = new RigHarness(Vector3.zero))
                {
                    GameObject wall = rig.AddBox(new Vector3(0f, 0f, 0.75f), new Vector3(6f, 80f, 0.3f)); // face z = 0.6
                    if (climbable) wall.AddComponent<LocomotionSurface>().climbable = true;
                    rig.Settle();

                    // Press both hands lightly into the wall and squeeze the grips.
                    Vector3 touch = new Vector3(0.2f, -0.1f, 0.6f - rig.Settings.handRadius + 0.03f);
                    rig.StrokeBoth(RigHarness.RestRight, touch, 0.25f, Dt);
                    rig.Input.leftGrip = rig.Input.rightGrip = 1f;
                    rig.Run(0.1f, Dt);
                    Assert.That(rig.Motor.IsLatched(Hand.Left) && rig.Motor.IsLatched(Hand.Right), Is.EqualTo(climbable));

                    // The floor falls away.
                    rig.Floor.transform.position += Vector3.down * 50f;
                    float startY = rig.Head.position.y;
                    rig.Run(1f, Dt);
                    float drop = startY - rig.Head.position.y;

                    // Pull the hands down 30 cm: on a climbable wall the body rises by the same amount.
                    float before = rig.Head.position.y;
                    rig.StrokeBoth(touch, touch + Vector3.down * 0.3f, 0.4f, Dt);
                    pulledUp = rig.Head.position.y - before;
                    return drop;
                }
            }

            Assert.That(Drop(true, out float up), Is.LessThan(0.02f), "A gripped climbable surface holds the body.");
            Assert.That(up, Is.EqualTo(0.3f).Within(0.03f), "Pulling down on the grip raises the body.");
            Assert.That(Drop(false, out _), Is.GreaterThan(0.5f), "An ordinary wall cannot be gripped.");
        }

        [Test]
        public void ReleasingClimbGrip_FlingsWithPullVelocity()
        {
            h = new RigHarness(Vector3.zero);
            GameObject wall = h.AddBox(new Vector3(0f, 0f, 0.75f), new Vector3(6f, 80f, 0.3f));
            wall.AddComponent<LocomotionSurface>().climbable = true;
            h.Settle();
            Vector3 touch = new Vector3(0.2f, 0.1f, 0.6f - h.Settings.handRadius + 0.03f);
            h.StrokeBoth(RigHarness.RestRight, touch, 0.25f, Dt);
            h.Input.leftGrip = h.Input.rightGrip = 1f;
            h.Run(0.1f, Dt);

            // Yank down hard and let go: the body keeps rising.
            h.StrokeBoth(touch, touch + Vector3.down * 0.5f, 0.2f, Dt); // 2.5 m/s
            h.Input.leftGrip = h.Input.rightGrip = 0f;
            h.StrokeBoth(touch + Vector3.down * 0.5f, new Vector3(0.2f, -0.4f, 0.2f), 0.05f, Dt);
            Assert.That(h.Motor.IsLatched(Hand.Left), Is.False);
            Assert.That(h.Motor.Velocity.y, Is.GreaterThan(1.5f), "Letting go mid-pull keeps the upward momentum.");
        }

        [Test]
        public void Unstuck_ReturnsToLastSafeGround()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle(2f);
            Vector3 safe = h.Head.position;

            // Shove the player somewhere bad: mid-air, far away.
            h.Rig.TeleportHead(new Vector3(40f, 25f, 40f));
            h.Step(Dt);
            h.Rig.UnstuckNow();
            h.Run(0.5f, Dt);

            Assert.That(Vector3.Distance(Flat(safe), Flat(h.Head.position)), Is.LessThan(0.1f));
            Assert.That(h.Motor.IsGrounded, Is.True);
            Assert.That(h.Motor.Velocity.magnitude, Is.LessThan(0.1f));
        }

        [Test]
        public void HoldingBothPrimaryButtons_TriggersUnstuck()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle(2f);
            int fired = 0;
            h.Rig.Unstuck += () => fired++;
            h.Rig.TeleportHead(new Vector3(40f, 25f, 40f));
            h.Input.leftPrimary = h.Input.rightPrimary = true;
            h.Run(h.Rig.holdSeconds + 0.3f, Dt);
            Assert.That(fired, Is.EqualTo(1), "Holding the buttons fires exactly once.");
            h.Input.leftPrimary = h.Input.rightPrimary = false;
        }

        [Test]
        public void FallingOutOfWorld_Respawns()
        {
            h = new RigHarness(Vector3.zero);
            var spawn = new GameObject("Spawn").transform;
            spawn.position = new Vector3(3f, 0f, 3f);
            h.Rig.SetSpawnPoint(spawn);
            h.Settle();
            h.Rig.TeleportHead(new Vector3(0f, h.Settings.killPlaneY - 5f, 0f));
            h.Run(0.5f, Dt);
            Assert.That(Vector3.Distance(Flat(h.Head.position), new Vector3(3f, 0f, 3f)), Is.LessThan(0.05f));
            Assert.That(h.Motor.IsGrounded, Is.True);
            Object.DestroyImmediate(spawn.gameObject);
        }

        [Test]
        public void Seat_CarriesRig_AndExitRestoresLocomotion()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            var seat = new GameObject("Seat").transform;
            seat.position = new Vector3(2f, 0.4f, 0f);

            h.Rig.EnterSeat(seat, seat.position + Vector3.up * 0.6f);
            Assert.That(h.Rig.Mode, Is.EqualTo(MovementMode.Seated));
            Vector3 relative = h.Head.position - seat.position;

            // The vehicle drives off and tilts; hands flail against the floor: no propulsion.
            h.Run(1f, Dt, t =>
            {
                seat.position = new Vector3(2f + t * 8f, 0.4f, 0f);
                seat.rotation = Quaternion.Euler(10f * t, 30f * t, 0f);
                h.ApplyStride(t);
            });
            Vector3 expected = seat.TransformPoint(relative);
            Assert.That(Vector3.Distance(h.Head.position, expected), Is.LessThan(0.01f), "The head stays on the seat's head anchor.");
            Assert.That(h.Root.transform.up.y, Is.EqualTo(1f).Within(1e-4f), "Vehicle pitch and roll never tilt the view.");

            h.RestHands();
            h.Rig.ExitSeat(new Vector3(10f, 0f, 2f));
            h.Run(0.5f, Dt);
            Assert.That(h.Rig.Mode, Is.EqualTo(MovementMode.HandLocomotion));
            Assert.That(Vector3.Distance(Flat(h.Head.position), new Vector3(10f, 0f, 2f)), Is.LessThan(0.05f));
            Assert.That(h.Motor.Velocity.magnitude, Is.LessThan(0.1f), "Leaving a seat must not launch the player.");
            Assert.That(h.Motor.IsGrounded, Is.True);
            Object.DestroyImmediate(seat.gameObject);
        }

        [Test]
        public void Calibration_SetsBodyLengthAndReachFromPose()
        {
            h = new RigHarness(Vector3.zero);
            h.Settle();
            h.SetHand(Hand.Left, new Vector3(-0.22f, -0.7f, 0.05f));
            h.SetHand(Hand.Right, new Vector3(0.22f, -0.7f, 0.05f));
            h.Rig.CalibrateFromCurrentPose();
            Assert.That(h.Motor.StandingLength, Is.EqualTo(0.7f + h.Settings.handRadius + 0.06f).Within(0.01f));
            Assert.That(h.Motor.ArmReach, Is.InRange(0.5f, 1.15f));
            h.Rig.ResetCalibration();
            Assert.That(h.Motor.StandingLength, Is.EqualTo(h.Settings.standingBodyLength));
        }
    }
}
