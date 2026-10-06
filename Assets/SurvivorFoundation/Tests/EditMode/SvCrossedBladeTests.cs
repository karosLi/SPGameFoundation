using System;
using System.IO;
using NUnit.Framework;
using SPF.Testing;
using SPF.Contracts;
using Unity.Collections;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvCrossedBladeTests
    {
        static void Configure(SvConfig c)
        {
            c.Capacity.Enemies = 16; c.Capacity.Bullets = 64; c.Capacity.Gems = 32; c.Capacity.Events = 32;
            c.Settings.MaxEnemies = 16; c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0;
            c.Settings.XpBase = 100000;
            c.Settings.CrossedBlades = new SvCrossedBlades { Enabled = true, TickInterval = 3, MaxTargets = 8, Reach = 5, HalfWidth = .5f, DamagePerPulse = 10 };
            c.Enemies[0].Speed = 0; c.Enemies[0].Hp = 100; c.Enemies[0].Damage = 0;
        }

        static void Quiet(SvTestWorld t)
        {
            for (int i = 0; i < t.Game.Upgrades.Length; i++) t.Game.Upgrades[i] = 0;
            t.World.Resource(SvKeys.BulletSpawns).Clear();
            var bullets = t.World.Table(SvKeys.Bullet);
            for (int i = 0; i < bullets.Count; i++) bullets.DeadFlags.Set(i, (byte)1);
            t.World.Resource(SvKeys.CrossedBlades).OnReset();
        }

        static float Hp(SvTestWorld t, EntityHandle target)
        {
            Assert.IsTrue(t.World.Registry.TryResolve(target, out _, out int row));
            return t.World.Column(SvKeys.Info)[row].Hp;
        }

        [Test]
        public void OverlappingPathsHitOncePerPulseAndPauseOutsidePlaying()
        {
            using var t = new SvTestWorld(tweak: Configure); Quiet(t);
            var target = t.Spawn(1, new float2(.25f, .25f));
            t.Step(2); Assert.AreEqual(100, Hp(t, target));
            t.Step(); Assert.AreEqual(90, Hp(t, target));
            var state = t.World.Resource(SvKeys.CrossedBlades);
            Assert.AreEqual(1, state.Scope[0].Count); Assert.AreEqual(1, state.Pulses);
            t.Game.Flow = SvFlow.LevelUp; t.Step(12); Assert.AreEqual(1, state.Pulses);
            t.Game.Flow = SvFlow.Playing; t.Step(3);
            Assert.AreEqual(80, Hp(t, target)); Assert.AreEqual(2, state.Pulses);
        }

        [Test]
        public void IndependentBladePathsBothHitAndIncludeEndTangency()
        {
            using var t = new SvTestWorld(tweak: Configure); Quiet(t);
            var horizontal = t.Spawn(1, new float2(3, 0));
            var vertical = t.Spawn(1, new float2(0, 3));
            var tangent = t.Spawn(1, new float2(5.8f, 0)); // reach 5 + width .5 + enemy radius .3
            var outside = t.Spawn(1, new float2(5.81f, 0));
            t.Step(3);
            Assert.AreEqual(90, Hp(t, horizontal)); Assert.AreEqual(90, Hp(t, vertical));
            Assert.AreEqual(90, Hp(t, tangent)); Assert.AreEqual(100, Hp(t, outside));
        }

        [Test]
        public void HistoryCapacityRejectsUnrecordedDamageAndDoesNotEvictAcceptedTarget()
        {
            using var t = new SvTestWorld(tweak: c => { Configure(c); c.Settings.CrossedBlades.MaxTargets = 1; }); Quiet(t);
            var a = t.Spawn(1, new float2(.15f, .15f)); var b = t.Spawn(1, new float2(.3f, .3f));
            t.Step(3);
            Assert.AreEqual(190, Hp(t, a) + Hp(t, b), "one history slot authorizes exactly one damage event across both queries");
            var state = t.World.Resource(SvKeys.CrossedBlades);
            Assert.AreEqual(1, state.Scope[0].Count); Assert.Greater(state.Rejections[0], 0);
        }

        [Test]
        public void FullDamageQueueDoesNotConsumeHistoryAndNextPulseRetries()
        {
            using var t = new SvTestWorld(tweak: Configure); Quiet(t);
            var target = t.Spawn(1, new float2(.25f, .25f)); t.Step(2);
            var hits = t.World.Resource(SvKeys.Hits);
            for (int i = 0; i < hits.Capacity; i++) Assert.IsTrue(hits.TryAdd(new SvHit { Target = 0, Damage = 0 }));
            t.Step();
            var state = t.World.Resource(SvKeys.CrossedBlades);
            Assert.AreEqual(100, Hp(t, target)); Assert.AreEqual(0, state.Scope[0].Count);
            Assert.Greater(state.Rejections[1], 0);
            t.Step(3); Assert.AreEqual(90, Hp(t, target)); Assert.AreEqual(1, state.Scope[0].Count);
        }

        [Test]
        public void SnapshotWithAcceptedHistoryResumesThroughSortDestroyAndRecycle()
        {
            using var t = new SvTestWorld(tweak: Configure); Quiet(t);
            var a = t.Spawn(1, new float2(3, 0)); var b = t.Spawn(1, new float2(0, 3));
            t.Step(3); var mid = t.Session.CaptureSnapshot();
            void Continue(SvTestWorld world)
            {
                using var keys = new NativeArray<uint>(new uint[] { 2, 1 }, Allocator.Temp);
                world.World.SortRows(SvKeys.Enemy, keys);
                world.World.DestroyEntity(a);
                var replacement = world.Spawn(1, new float2(3, 0));
                Assert.AreEqual(a.Index, replacement.Index); Assert.AreNotEqual(a.Generation, replacement.Generation);
                world.Step(6);
                Assert.AreEqual(70, Hp(world, b)); Assert.AreEqual(80, Hp(world, replacement));
            }
            Continue(t); byte[] expected = t.Session.CaptureSnapshot();
            using var restored = new SvTestWorld(tweak: Configure, start: false);
            restored.Session.RestoreSnapshot(mid);
            CollectionAssert.AreEqual(mid, restored.Session.CaptureSnapshot());
            Continue(restored);
            CollectionAssert.AreEqual(expected, restored.Session.CaptureSnapshot());
        }

        [Test]
        public void LevelResetClearsScopeAndClassicDoesNotInstallExtension()
        {
            using var classic = new SvTestWorld(); Assert.IsFalse(classic.World.HasResource(SvKeys.CrossedBlades));
            using var t = new SvTestWorld(tweak: Configure); Quiet(t); t.Spawn(1, float2.zero); t.Step(3);
            var state = t.World.Resource(SvKeys.CrossedBlades); Assert.AreEqual(1, state.Scope[0].Count);
            t.World.ClearLevel();
            Assert.AreEqual(0, state.Scope[0].Count); Assert.AreEqual(0, state.Pulses); Assert.IsTrue(state.Targets[0].IsNull);
            Assert.AreEqual(0, state.Timeline.Tick);
        }

        [Test]
        public void ClassicAndBladeSnapshotsHaveExplicitlyDifferentResourceLayouts()
        {
            using var classic = new SvTestWorld(tweak: c => { Configure(c); c.Settings.CrossedBlades.Enabled = false; });
            using var blades = new SvTestWorld(tweak: Configure);
            byte[] classicBytes = classic.Session.CaptureSnapshot(), bladeBytes = blades.Session.CaptureSnapshot();
            Assert.Throws<InvalidDataException>(() => classic.Session.RestoreSnapshot(bladeBytes));
            Assert.Throws<InvalidDataException>(() => blades.Session.RestoreSnapshot(classicBytes));
        }

        [Test]
        public void SnapshotAfterLethalPulsePreservesPendingDestroy()
        {
            using var t = new SvTestWorld(tweak: c => { Configure(c); c.Enemies[0].Hp = 10; }); Quiet(t);
            t.Spawn(1, new float2(3, 0)); t.Step(3);
            Assert.AreEqual(1, t.Enemies, "death removal is deferred until next tick");
            var mid = t.Session.CaptureSnapshot();
            t.Step(6); Assert.AreEqual(0, t.Enemies); var expected = t.Session.CaptureSnapshot();
            using var restored = new SvTestWorld(tweak: c => { Configure(c); c.Enemies[0].Hp = 10; }, start: false);
            restored.Session.RestoreSnapshot(mid); restored.Step(6);
            CollectionAssert.AreEqual(expected, restored.Session.CaptureSnapshot());
        }

        [Test]
        public void CrossedBladeFixedTicksAllocateNoManagedMemoryAfterWarmup()
        {
            using var t = new SvTestWorld(tweak: c => { Configure(c); c.Enemies[0].Hp = 100000; }); Quiet(t);
            for (int i = 0; i < 8; i++) t.Spawn(1, new float2(3, i * .02f));
            t.Step(30);
            Action measured = () => t.Step(60);
            using var probe = new ManagedAllocationProbe();
            var calibrationBefore = probe.Calibrate();
            var sample = probe.Measure(measured);
            var calibrationAfter = probe.Calibrate();
            TestContext.WriteLine($"Survivor crossed blades, 60 ticks after 30 warm-up ticks: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty calibration before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
            Assert.AreEqual(0, sample.Value, $"Warmed simulation must allocate zero current-thread {sample.Metric}; excludes rendering/native allocations.");
            Assert.AreEqual(30, t.World.Resource(SvKeys.CrossedBlades).Pulses);
        }

        [Test]
        public void SnapshotRejectsReusedPulseButAcceptsInitialEmptyAndWrappedScopes()
        {
            using var state = new SvCrossedBladeState(8);
            void RoundTrip()
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                state.WriteSnapshot(writer); stream.Position = 0;
                using var reader = new BinaryReader(stream);
                state.ReadSnapshot(reader);
            }
            RoundTrip(); // initial state: no completed pulse and inactive history
            state.Timeline.Advance(2); RoundTrip(); // initial waiting ticks remain valid
            state.Pulses = 1; state.Timeline.Begin();
            state.Scope[0] = new SPF.L2.Combat.HitHistoryState { Pulse = 1 };
            RoundTrip(); // a completed pulse with no accepted targets is valid
            state.Targets[0] = new EntityHandle(2, 3);
            state.Scope[0] = new SPF.L2.Combat.HitHistoryState { Pulse = 1, Count = 1 };
            RoundTrip(); // accepted target belongs to the completed pulse
            state.Scope[0] = new SPF.L2.Combat.HitHistoryState { Pulse = state.Timeline.PulseId, Count = 1 };
            Assert.Throws<InvalidDataException>(() => RoundTrip(), "same pulse would suppress the next accepted pulse's targets");
            state.Targets[0] = default;
            state.Scope[0] = new SPF.L2.Combat.HitHistoryState { Pulse = uint.MaxValue };
            state.Timeline.PulseId = 1; state.Pulses = int.MaxValue;
            RoundTrip(); // nonzero pulse counter wraps; completed pulse count saturates
            state.Pulses = 0;
            Assert.Throws<InvalidDataException>(() => RoundTrip(), "initial state cannot already hold completed history");
        }

        [Test]
        public void SnapshotRejectsCapacityMismatchAndInvalidHistoryBounds()
        {
            using var state = new SvCrossedBladeState(8);
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            state.WriteSnapshot(writer); var bytes = stream.ToArray();
            using var other = new SvCrossedBladeState(4);
            Assert.Throws<InvalidDataException>(() => other.ReadSnapshot(new BinaryReader(new MemoryStream(bytes))));
            var scope = state.Scope[0]; scope.Count = 9; state.Scope[0] = scope;
            stream.SetLength(0); state.WriteSnapshot(writer); bytes = stream.ToArray();
            Assert.Throws<InvalidDataException>(() => state.ReadSnapshot(new BinaryReader(new MemoryStream(bytes))));
        }
    }
}
