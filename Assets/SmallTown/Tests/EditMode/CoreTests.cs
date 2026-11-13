using System;
using NUnit.Framework;
using SmallTown.Locomotion;
using SmallTown.UI;
using UnityEngine;

namespace SmallTown.Tests
{
    public class CoreTests
    {
        [Test]
        public void VelocityWindow_AveragesOverTime_NotFrames()
        {
            var fast = new VelocityWindow(0.1f);
            var slow = new VelocityWindow(0.1f);
            // The same 2 m/s motion sampled at 120 Hz and at 36 Hz.
            for (int i = 0; i < 60; i++) fast.Add(Vector3.forward * (2f / 120f), 1f / 120f);
            for (int i = 0; i < 18; i++) slow.Add(Vector3.forward * (2f / 36f), 1f / 36f);
            Assert.That(fast.Average().z, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(slow.Average().z, Is.EqualTo(2f).Within(1e-3f));
        }

        [Test]
        public void VelocityWindow_ForgetsMotionOlderThanWindow()
        {
            var window = new VelocityWindow(0.1f);
            for (int i = 0; i < 20; i++) window.Add(Vector3.right * 0.05f, 0.01f); // 5 m/s, long ago
            for (int i = 0; i < 10; i++) window.Add(Vector3.zero, 0.01f);          // then still for 0.1 s
            Assert.That(window.Average().magnitude, Is.LessThan(1e-4f));
            window.Clear();
            Assert.That(window.Average(), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Friction_UsesWallValueOnSteepSurfaces_AndClimbableOverrides()
        {
            var settings = ScriptableObject.CreateInstance<HandLocomotionSettings>();
            settings.groundFriction = 1f;
            settings.wallFriction = 0.4f;
            Assert.That(settings.FrictionFor(Vector3.up, null), Is.EqualTo(1f));
            Assert.That(settings.FrictionFor(Vector3.forward, null), Is.EqualTo(0.4f));

            var go = new GameObject("Surface");
            var surface = go.AddComponent<LocomotionSurface>();
            surface.climbable = true;
            Assert.That(settings.FrictionFor(Vector3.forward, surface), Is.EqualTo(1f));
            surface.climbable = false;
            surface.overrideFriction = true;
            surface.friction = 0.1f;
            Assert.That(settings.FrictionFor(Vector3.up, surface), Is.EqualTo(0.1f));
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void LocomotionMask_ExcludesEverythingAPlayerCanCarry()
        {
            Assert.That(GameLayers.LocomotionSurfaces & (1 << GameLayers.Item), Is.Zero);
            Assert.That(GameLayers.LocomotionSurfaces & (1 << GameLayers.HeldItem), Is.Zero);
            Assert.That(GameLayers.LocomotionSurfaces & (1 << GameLayers.PlayerBody), Is.Zero);
            Assert.That(GameLayers.LocomotionSurfaces & (1 << GameLayers.PlayerHand), Is.Zero);
            Assert.That(GameLayers.LocomotionSurfaces & (1 << GameLayers.World), Is.Not.Zero);
        }

        [Test]
        public void EveryTutorialStep_HasInstructions()
        {
            foreach (TutorialController.Step step in Enum.GetValues(typeof(TutorialController.Step)))
            {
                TutorialController.Describe(step, out string title, out string body);
                Assert.That(title, Is.Not.Empty, step.ToString());
                Assert.That(body, Is.Not.Empty, step.ToString());
            }
        }

        [Test]
        public void ProjectLayers_MatchCodeConstants()
        {
            foreach (var (index, name) in GameLayers.Names)
            {
                Assert.That(LayerMask.LayerToName(index), Is.EqualTo(name),
                    "Run Small Town > Setup > Configure Project Settings to restore layers.");
            }
        }
    }
}
