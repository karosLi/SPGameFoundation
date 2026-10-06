using System;
using System.Reflection;
using NUnit.Framework;
using SPF.Shell.Performance;
using UnityEngine;
using UnityEngine.TestTools.Constraints;

namespace SPF.Tests.EditMode
{
    public class FrameGovernorTests
    {
        static int Run(FrameBudget b, float ms, float seconds)
        {
            int changes = 0;
            for (float t = 0f; t < seconds; t += ms / 1000f)
                if (b.Feed(ms / 1000f)) changes++;
            return changes;
        }

        [Test]
        public void DegradesUnderSustainedLoadAndRecoversSlowly()
        {
            var b = new FrameBudget();
            Run(b, 16.6f, 5f);
            Assert.AreEqual(0, b.Level, "on budget: full quality");

            Run(b, 25f, 3f);
            Assert.AreEqual(1, b.Level, "two seconds over budget: one step down");
            Run(b, 25f, 20f);
            Assert.AreEqual(b.MaxLevel, b.Level, "clamped at the cheapest level");

            Run(b, 10f, 4f);
            Assert.AreEqual(b.MaxLevel, b.Level, "recovery needs a longer stretch of headroom");
            Run(b, 10f, 7f);
            Assert.AreEqual(b.MaxLevel - 1, b.Level, "then one step back up");
        }

        [Test]
        public void IgnoresHitchesAndBorderlineFrames()
        {
            var b = new FrameBudget();
            for (int i = 0; i < 20; i++) b.Feed(0.5f);       // loading hitches
            Assert.AreEqual(0, b.Level);
            Assert.AreEqual(0, Run(b, 18f, 30f), "inside the hysteresis band nothing moves");
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(0f)]
        [TestCase(-0.01f)]
        public void InvalidFrameDurationsDoNotPoisonTheBudget(float dt)
        {
            var b = new FrameBudget();
            b.Feed(0.025f);
            float average = b.AverageMs;
            Assert.IsFalse(b.Feed(dt));
            Assert.AreEqual(average, b.AverageMs);
            Assert.AreEqual(0, b.Level);
            Assert.AreEqual(1, Run(b, 25f, 3f), "valid measurements still drive adaptation");
        }

        [Test]
        public void InfiniteFrameIsIgnoredEvenWithoutASpikeLimit()
        {
            var b = new FrameBudget { SpikeSeconds = float.PositiveInfinity };
            Assert.IsFalse(b.Feed(float.PositiveInfinity));
            Assert.AreEqual(b.TargetMs, b.AverageMs);
        }

        [Test]
        public void IdleThrottleWakesOnActivity()
        {
            var idle = new IdleThrottle { IdleAfterSeconds = 1f };
            Assert.IsFalse(idle.Feed(0.5f, false));
            Assert.IsTrue(idle.Feed(0.6f, false));
            Assert.IsTrue(idle.Idle);
            Assert.IsTrue(idle.Feed(0.016f, true));
            Assert.IsFalse(idle.Idle);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(0f)]
        [TestCase(-0.5f)]
        public void InvalidIdleDurationsDoNotAdvanceOrPoisonTheTimer(float dt)
        {
            var idle = new IdleThrottle { IdleAfterSeconds = 1f };
            idle.Feed(0.5f, false);
            Assert.IsFalse(idle.Feed(dt, false));
            Assert.IsFalse(idle.Idle);
            Assert.IsTrue(idle.Feed(0.5f, false), "valid elapsed time is preserved");
            Assert.IsTrue(idle.Feed(dt, true), "activity wakes even when its duration is invalid");
            Assert.IsFalse(idle.Idle);
            Assert.IsFalse(idle.Feed(0.5f, false), "wake resets the quiet timer");
        }

        [Test]
        public void WakingIgnoresIdlePacingAndStartsFreshMeasurements()
        {
            WithGovernor((g, step) =>
            {
                g.ThrottleWhenIdle = true;
                g.Idle.IdleAfterSeconds = 0.1f;
                g.Budget.Reset(1);
                Run(g.Budget, 25f, 1.9f);
                Assert.AreEqual(1, g.Level);

                step(0.1f, false);
                Assert.IsTrue(g.IsIdle);
                Assert.AreEqual(30, Application.targetFrameRate);
                float before = g.Budget.AverageMs;
                step(0.2f, false);
                Assert.AreEqual(before, g.Budget.AverageMs, "idle frames are never load measurements");

                step(0.2f, true);
                Assert.IsFalse(g.IsIdle);
                Assert.AreEqual(60, Application.targetFrameRate);
                Assert.AreEqual(1, g.Level, "the slow wake frame must not lower quality");
                Assert.AreEqual(g.Budget.TargetMs, g.Budget.AverageMs);
                Assert.AreEqual(0, Run(g.Budget, 25f, 0.5f), "pre-idle load is not consecutive with new load");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisablingIdleThrottleWakesAndRespectsTheThermalCap(bool hot)
        {
            WithGovernor((g, step) =>
            {
                g.Thermal.Override = new DeviceState
                {
                    Thermal = hot ? ThermalLevel.Serious : ThermalLevel.Nominal,
                    Battery = 0.8f,
                    Charging = false,
                };
                g.ApplyDeviceState();
                g.SetFrameRates(60, 15);
                g.ThrottleWhenIdle = true;
                g.Idle.IdleAfterSeconds = 0.1f;
                step(0.1f, false);
                Assert.IsTrue(g.IsIdle);
                Assert.AreEqual(15, Application.targetFrameRate);

                g.ThrottleWhenIdle = false;
                step(0.2f, false);
                Assert.IsFalse(g.IsIdle);
                Assert.AreEqual(hot ? 30 : 60, Application.targetFrameRate);
                Assert.AreEqual(hot ? 2 : 0, g.Level);
                Assert.AreEqual(g.Budget.TargetMs, g.Budget.AverageMs, "ignore the final idle-paced frame");

                step(0.02f, false);
                Assert.AreEqual(g.Budget.TargetMs + (20f - g.Budget.TargetMs) * 0.05f,
                    g.Budget.AverageMs, 0.0001f, "adaptive measurements resume on the following frame");
            });
        }

        [Test]
        public void DisabledIdleThrottleClearsPartiallyElapsedQuietTime()
        {
            WithGovernor((g, step) =>
            {
                g.ThrottleWhenIdle = true;
                g.Idle.IdleAfterSeconds = 1f;
                step(0.75f, false);
                g.ThrottleWhenIdle = false;
                step(0.1f, false);
                g.ThrottleWhenIdle = true;
                step(0.5f, false);
                Assert.IsFalse(g.IsIdle, "re-enabling starts a new quiet interval");
                step(0.5f, false);
                Assert.IsTrue(g.IsIdle);
            });
        }

        [TestCase(25f, 1.8f, 120)]
        [TestCase(5f, 5.9f, 30)]
        public void ActiveTargetChangeResetsAverageAndHysteresis(float ms, float seconds, int active)
        {
            WithGovernor((g, step) =>
            {
                g.Budget.Reset(1);
                Assert.AreEqual(0, Run(g.Budget, ms, seconds));
                g.SetFrameRates(active, 15);
                Assert.AreEqual(1000f / active, g.Budget.TargetMs);
                Assert.AreEqual(g.Budget.TargetMs, g.Budget.AverageMs);
                Assert.AreEqual(1, g.Level, "changing frame targets preserves the quality level");
                Assert.AreEqual(0, Run(g.Budget, ms, 0.5f), "old slow/fast time is discarded");
            });
        }

        [Test]
        public void ActiveTargetChangeIgnoresOnePreviouslyPacedFrame()
        {
            WithGovernor((g, step) =>
            {
                g.SetFrameRates(120, 30);
                step(0.2f, true);
                Assert.AreEqual(g.Budget.TargetMs, g.Budget.AverageMs);
                step(0.025f, true);
                Assert.Greater(g.Budget.AverageMs, g.Budget.TargetMs, "only the transition sample is skipped");
            });
        }

        [Test]
        public void UnchangedActiveTargetPreservesMeasurements()
        {
            WithGovernor((g, step) =>
            {
                Run(g.Budget, 25f, 1.8f);
                float average = g.Budget.AverageMs;
                g.SetFrameRates(60, 15);
                g.ApplyDeviceState();
                Assert.AreEqual(average, g.Budget.AverageMs);
                Assert.AreEqual(1, Run(g.Budget, 25f, 0.5f), "reapplying the same target cannot defer adaptation");
            });
        }

        [Test]
        public void ThermalTargetTransitionsKeepFloorsAndDiscardOldPacing()
        {
            WithGovernor((g, step) =>
            {
                Run(g.Budget, 25f, 1.8f);
                g.Thermal.Override = new DeviceState { Thermal = ThermalLevel.Serious, Battery = 0.8f };
                g.ApplyDeviceState();
                Assert.AreEqual(2, g.Level);
                Assert.AreEqual(2, g.Budget.Floor);
                Assert.AreEqual(30, Application.targetFrameRate);
                Assert.AreEqual(1000f / 30f, g.Budget.AverageMs);
                Run(g.Budget, 5f, 7f);
                Assert.AreEqual(2, g.Level, "thermal floor blocks quality recovery");

                g.Thermal.Override = new DeviceState { Thermal = ThermalLevel.Nominal, Battery = 0.8f };
                g.ApplyDeviceState();
                Assert.AreEqual(0, g.Budget.Floor);
                Assert.AreEqual(2, g.Level, "cooling down does not instantly restore expensive quality");
                Assert.AreEqual(60, Application.targetFrameRate);
                Assert.AreEqual(1000f / 60f, g.Budget.AverageMs);
                Assert.AreEqual(0, Run(g.Budget, 5f, 0.5f), "headroom accumulated at the thermal cap is discarded");
            });
        }

        [Test]
        public void SteadyFramePolicyDoesNotAllocate()
        {
            WithGovernor((g, step) =>
            {
                g.ThrottleWhenIdle = true;
                TestDelegate drive = () =>
                {
                    for (int i = 0; i < 1000; i++) step(1f / 60f, true);
                };
                drive();
                Assert.That(drive, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            });
        }

        [Test]
        public void CollectionCountsAreSeparateFromAllocationFramesAndResetIndependently()
        {
            WithGovernor((g, step) =>
            {
                g.ResetGcStats();
                int before = GC.CollectionCount(0);
                GC.Collect(0); // Intentional test-only event; production never forces a collection.
                int observed = GC.CollectionCount(0) - before;
                Assert.Greater(observed, 0);
                Assert.GreaterOrEqual(g.GcCollectionsSinceReset, observed);
                Assert.AreEqual(0, g.GcFramesSinceReset, "collection events are not allocation-frame samples");
                Assert.AreEqual(0L, g.GcBytesSinceReset);
                g.ResetGcStats();
                int reset = g.GcCollectionsSinceReset;
                Assert.AreEqual(0, reset);
            });
        }

        static void WithGovernor(Action<FrameGovernor, Action<float, bool>> test)
        {
            int originalRate = Application.targetFrameRate;
            var go = new GameObject("FrameGovernorTest");
            go.SetActive(false);
            var g = go.AddComponent<FrameGovernor>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                // Drive the same policy as Update, without relying on engine wall-clock timing.
                // Explicit lifecycle calls keep Unity and the non-lifecycle harness equivalent.
                typeof(FrameGovernor).GetMethod("OnEnable", flags).Invoke(g, null);
                g.Thermal.Override = new DeviceState { Thermal = ThermalLevel.Nominal, Battery = 0.8f };
                var step = (Action<float, bool>)Delegate.CreateDelegate(typeof(Action<float, bool>), g,
                    typeof(FrameGovernor).GetMethod("UpdateFramePolicy", flags));
                test(g, step);
            }
            finally
            {
                typeof(FrameGovernor).GetMethod("OnDisable", flags).Invoke(g, null);
                UnityEngine.Object.DestroyImmediate(go);
                Application.targetFrameRate = originalRate;
            }
        }

        [Test]
        public void ThermalFloorDegradesAtOnceAndBlocksRecovery()
        {
            var b = new FrameBudget();
            Run(b, 16.6f, 2f);
            var hot = new DeviceState { Thermal = ThermalLevel.Serious, Battery = 0.8f, Charging = false };
            b.Floor = hot.QualityFloor(b.MaxLevel);
            Assert.AreEqual(2, b.Level, "serious heat: two steps down immediately");
            Assert.AreEqual(30, hot.FrameRateCap);
            Run(b, 5f, 30f);
            Assert.AreEqual(2, b.Level, "plenty of headroom, but the device is hot");

            var cool = new DeviceState { Thermal = ThermalLevel.Nominal, Battery = 0.8f, Charging = false };
            b.Floor = cool.QualityFloor(b.MaxLevel);
            Assert.AreEqual(0, cool.FrameRateCap);
            Run(b, 5f, 20f);
            Assert.AreEqual(0, b.Level, "cooled down: recovers step by step");

            var saver = new DeviceState { Thermal = ThermalLevel.Unknown, LowPowerMode = true, Battery = 0.5f };
            Assert.AreEqual(1, saver.QualityFloor(3));
            Assert.AreEqual(30, saver.FrameRateCap);
            var lowBattery = new DeviceState { Thermal = ThermalLevel.Nominal, Battery = 0.1f, Charging = false };
            Assert.AreEqual(1, lowBattery.QualityFloor(3));
            Assert.AreEqual(3, new DeviceState { Thermal = ThermalLevel.Critical }.QualityFloor(3));
        }
    }
}
