using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvFlyingSwordTests
    {
        static void Configure(SvConfig c)
        {
            c.Capacity.Enemies = 32; c.Capacity.Bullets = 32; c.Capacity.Gems = 64; c.Capacity.Events = 64;
            c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0;
            c.Settings.XpBase = 100000; c.Settings.Variant = SvVariant.FlyingSwordHorde;
            c.Settings.FlyingSwords = SvFlyingSwords.Default;
            c.Settings.FlyingSwords.Capacity = c.Settings.FlyingSwords.BaseCount = 1;
            c.Settings.FlyingSwords.OrbitTicks = 3;
            c.Settings.FlyingSwords.OrbitRadius = 0; c.Settings.FlyingSwords.Damage = 10;
            c.Settings.FlyingSwords.TurnRate = 1000;
            c.Enemies[0].Speed = 0; c.Enemies[0].Damage = 0; c.Enemies[0].Hp = 10000;
        }
        static SvFlyingSwordState S(SvTestWorld t) => t.World.Resource(SvFlyingSwordState.Key);
        static float Hp(SvTestWorld t, EntityHandle handle)
        {
            Assert.IsTrue(t.World.Registry.TryResolve(handle, out _, out int row)); return t.World.Column(SvKeys.Info)[row].Hp;
        }
        static void Flight(SvTestWorld t, float2 from, float2 direction, EntityHandle target)
        {
            var state = S(t); var b = state.Blades[0];
            b.Active = true; b.Phase = SvSwordPhase.Outbound; b.Position = b.Previous = from; b.Direction = direction; b.Target = target; b.Age = 0;
            b.Timeline.Begin(); var scope = state.Scopes[0]; HitHistory.Begin(state.History, 0, state.HistoryCapacity, ref scope, EntityHandle.Null, b.Timeline.PulseId);
            state.Blades[0] = b; state.Scopes[0] = scope; state.ActiveCount = 1;
        }

        [Test]
        public void FullSortieOrbitsLaunchesReturnsAndNeverDoubleHitsTarget()
        {
            using var t = new SvTestWorld(tweak: Configure); var target = t.Spawn(1, new float2(4, 0));
            t.Step(2); Assert.AreEqual(SvSwordPhase.Outbound, S(t).Blades[0].Phase);
            int pulse = (int)S(t).Blades[0].Timeline.PulseId; bool hit = false, returned = false;
            for (int i = 0; i < 50; i++)
            {
                t.Step(); hit |= S(t).Scopes[0].Count == 1;
                if (S(t).Blades[0].Phase == SvSwordPhase.Orbit) { returned = true; break; }
                Assert.AreEqual(pulse, S(t).Blades[0].Timeline.PulseId);
            }
            Assert.IsTrue(hit); Assert.IsTrue(returned); Assert.AreEqual(9990, Hp(t, target));
            Assert.AreEqual(0, t.Bullets, "sword count upgrade does not spawn legacy bolts");
            t.Game.Flow = SvFlow.LevelUp; var before = S(t).Blades[0]; t.Step(20);
            Assert.AreEqual(before.Position, S(t).Blades[0].Position); Assert.AreEqual(before.Age, S(t).Blades[0].Age);
        }

        [Test]
        public void HighSpeedSweepUsesEntryTimeThenStableHandleAndPierceBudget()
        {
            using var t = new SvTestWorld(tweak: c => { Configure(c); c.Settings.FlyingSwords.Speed = 3000; c.Settings.FlyingSwords.HistoryPerSword = 1; });
            var first = t.Spawn(1, new float2(2, 0)); var second = t.Spawn(1, new float2(6, 0));
            Flight(t, float2.zero, new float2(1, 0), second); t.Step();
            Assert.AreEqual(9990, Hp(t, first)); Assert.AreEqual(10000, Hp(t, second));
            Assert.AreEqual(first, S(t).History[0]); Assert.Greater(S(t).Counters[2], 0);
            // Identical entry times: larger physical row must not beat the lower stable handle.
            using var tied = new SvTestWorld(tweak: c => { Configure(c); c.Enemies[0].Radius = 0; c.Settings.FlyingSwords.Speed = 3000; c.Settings.FlyingSwords.HistoryPerSword = 1; });
            var a = tied.Spawn(1, new float2(4, 0)); var b = tied.Spawn(1, new float2(4, 0));
            using var keys = new NativeArray<uint>(new uint[] { 2, 1 }, Allocator.Temp); tied.World.SortRows(SvKeys.Enemy, keys);
            Flight(tied, float2.zero, new float2(1, 0), a); tied.Step();
            Assert.AreEqual(a, S(tied).History[0]); Assert.AreEqual(9990, Hp(tied, a)); Assert.AreEqual(10000, Hp(tied, b));
        }

        [Test]
        public void RelativeMotionCrossingEnemyIsHitWhenBothEndpointsMiss()
        {
            using var t = new SvTestWorld(tweak: c => { Configure(c); c.Settings.FlyingSwords.Speed = .0001f; c.Settings.FlyingSwords.Radius = .1f; });
            var crossing = t.Spawn(1, new float2(4, 0));
            Assert.IsTrue(t.World.Registry.TryResolve(crossing, out _, out int row));
            var info = t.World.Column(SvKeys.Info)[row]; info.Speed = 240; t.World.Column(SvKeys.Info).Set(row, info);
            var anchor = t.Spawn(1, new float2(12, 8)); Flight(t, float2.zero, new float2(1, 0), anchor);
            t.Step();
            Assert.Greater(math.distance(t.World.Column(SvKeys.Position)[row], float2.zero), 3);
            Assert.AreEqual(9990, Hp(t, crossing));
        }

        [Test]
        public void ReorderAndRecycledHandleDoNotReuseAnotherTargetsHitHistory()
        {
            using var t = new SvTestWorld(tweak: c => { Configure(c); c.Settings.FlyingSwords.Speed = 300; c.Settings.FlyingSwords.OutboundTicks = 100; });
            var a = t.Spawn(1, new float2(2, 0)); var b = t.Spawn(1, new float2(15, 0));
            Flight(t, float2.zero, new float2(1, 0), b); t.Step(); Assert.AreEqual(9990, Hp(t, a));
            using var keys = new NativeArray<uint>(new uint[] { 2, 1 }, Allocator.Temp); t.World.SortRows(SvKeys.Enemy, keys);
            var blade = S(t).Blades[0]; blade.Position = blade.Previous = float2.zero; S(t).Blades[0] = blade;
            t.Step(); Assert.AreEqual(9990, Hp(t, a), "row move does not authorize a duplicate");
            t.World.DestroyEntity(a); var recycled = t.Spawn(1, new float2(2, 0));
            Assert.AreEqual(a.Index, recycled.Index); Assert.AreNotEqual(a.Generation, recycled.Generation);
            blade = S(t).Blades[0]; blade.Position = blade.Previous = float2.zero; S(t).Blades[0] = blade;
            t.Step(); Assert.AreEqual(9990, Hp(t, recycled));
        }

        [Test]
        public void FullQueueRejectsDamageWithoutConsumingHistory()
        {
            using var t = new SvTestWorld(tweak: c => { Configure(c); c.Settings.FlyingSwords.Speed = 3000; });
            var a = t.Spawn(1, new float2(3, 0)); Flight(t, float2.zero, new float2(1, 0), a);
            var queue = t.World.Resource(SvKeys.Hits);
            for (int i = 0; i < queue.Capacity; i++) Assert.IsTrue(queue.TryAdd(new SvHit { Target = 0 }));
            t.Step(); Assert.AreEqual(10000, Hp(t, a)); Assert.AreEqual(0, S(t).Scopes[0].Count); Assert.Greater(S(t).Counters[3], 0);
            t.Step(); Assert.AreEqual(9990, Hp(t, a), "return sweep retries unrecorded target");
        }

        [Test]
        public void SnapshotResumesInFlightWithHistoryAndExactDeterminism()
        {
            using var t = new SvTestWorld(tweak: Configure);
            t.Spawn(1, new float2(4, 0)); t.Spawn(1, new float2(-4, 0)); t.Step(9);
            var mid = t.Session.CaptureSnapshot(); t.Step(100); var end = t.Session.CaptureSnapshot();
            using var restored = new SvTestWorld(tweak: Configure, start: false); restored.Session.RestoreSnapshot(mid);
            CollectionAssert.AreEqual(mid, restored.Session.CaptureSnapshot()); restored.Step(100);
            CollectionAssert.AreEqual(end, restored.Session.CaptureSnapshot());
            using var twin = new SvTestWorld(tweak: Configure); twin.Spawn(1, new float2(4, 0)); twin.Spawn(1, new float2(-4, 0)); twin.Step(109);
            CollectionAssert.AreEqual(end, twin.Session.CaptureSnapshot());
        }

        [Test]
        public void UpgradeWinDeathRestartAndMenuRunOnExistingFlow()
        {
            using var t = new SvTestWorld(tweak: c => { Configure(c); c.Settings.FlyingSwords.WaveTicks = 10; c.Settings.FlyingSwords.Capacity = 12; c.Settings.XpBase = 2; });
            t.World.Resource(SvKeys.Collected).TryAdd(2); t.Step(); Assert.AreEqual(SvFlow.LevelUp, t.Game.Flow);
            t.Game.Choices[0] = (int)Upgrade.Bolt; t.Game.Send(SvCommandKind.Choose); t.Step(); Assert.AreEqual(5, S(t).ActiveCount);
            t.Step(10); Assert.AreEqual(SvFlow.Won, t.Game.Flow);
            t.Game.Send(SvCommandKind.Start); t.Step(); Assert.AreEqual(SvFlow.Playing, t.Game.Flow); Assert.AreEqual(1, S(t).ActiveCount);
            Assert.AreEqual(0, S(t).Counters[1]);
            t.World.Resource(SvKeys.HeroDamage).TryAdd(10000); t.Step(); Assert.AreEqual(SvFlow.Dead, t.Game.Flow);
            t.Game.Send(SvCommandKind.Start); t.Step(); Assert.Greater(t.Game.Hp, 0); Assert.AreEqual(SvLossReason.None, t.Game.LossReason);
            t.Game.Send(SvCommandKind.Menu); t.Step(); Assert.AreEqual(SvFlow.Menu, t.Game.Flow); Assert.AreEqual(0, S(t).ActiveCount);
        }

        [Test]
        public void ClassicResourceLayoutUnchangedAndMalformedSwordSnapshotRejected()
        {
            using var classic = new SvTestWorld(); Assert.IsFalse(classic.World.HasResource(SvFlyingSwordState.Key));
            using var t = new SvTestWorld(tweak: Configure);
            Assert.Throws<InvalidDataException>(() => classic.Session.RestoreSnapshot(t.Session.CaptureSnapshot()));
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            S(t).ActiveCount = 200; S(t).WriteSnapshot(writer); stream.Position = 0;
            Assert.Throws<InvalidDataException>(() => S(t).ReadSnapshot(new BinaryReader(stream)));
        }

        [TestCase(SvVariant.Classic)]
        [TestCase(SvVariant.GuardBeacon)]
        public void DifferentHostVariantRejectsSnapshotBeforeTerminalFlowCanDiverge(SvVariant variant)
        {
            using var original = new SvTestWorld(tweak: c => { Configure(c); c.Settings.FlyingSwords.WaveTicks = 4; });
            original.Step();
            var snapshot = original.Session.CaptureSnapshot();
            using var changed = new SvTestWorld(tweak: c =>
            {
                Configure(c); c.Settings.FlyingSwords.WaveTicks = 4; c.Settings.Variant = variant;
            }, start: false);
            Assert.Throws<InvalidDataException>(() => changed.Session.RestoreSnapshot(snapshot),
                "identical sword rules do not imply identical host spawning or victory conditions");
            original.Step(5);
            Assert.AreEqual(SvFlow.Won, original.Game.Flow, "the source actually reaches the finite sword-horde terminal flow");
        }

        [Test]
        public void AlteredSwordRulesRejectSnapshotAndClassicOptInPreservesClock()
        {
            using var original = new SvTestWorld(tweak: Configure); original.Spawn(1, new float2(4, 0)); original.Step(8);
            var snapshot = original.Session.CaptureSnapshot();
            using var changed = new SvTestWorld(tweak: c => { Configure(c); c.Settings.FlyingSwords.Speed += 1; }, start: false);
            Assert.Throws<InvalidDataException>(() => changed.Session.RestoreSnapshot(snapshot));
            using var optIn = new SvTestWorld(tweak: c => { Configure(c); c.Settings.Variant = SvVariant.Classic; });
            optIn.Spawn(1, new float2(4, 0)); optIn.Step(20); var saved = optIn.Session.CaptureSnapshot();
            using var resumed = new SvTestWorld(tweak: c => { Configure(c); c.Settings.Variant = SvVariant.Classic; }, start: false);
            resumed.Session.RestoreSnapshot(saved); Assert.AreEqual(optIn.Game.RunTicks, resumed.Game.RunTicks);
            optIn.Step(30); resumed.Step(30); CollectionAssert.AreEqual(optIn.Session.CaptureSnapshot(), resumed.Session.CaptureSnapshot());
        }

        [Test]
        public void Dense1024Target24SwordWorkloadIsBoundedAndMeasuredWithoutTimingGate()
        {
            using var t = new SvTestWorld(tweak: c =>
            {
                Configure(c); c.Capacity.Enemies = 1024; c.Capacity.Events = 4096; c.Settings.MaxEnemies = 1024;
                c.Settings.FlyingSwords.Capacity = c.Settings.FlyingSwords.BaseCount = 24;
                c.Settings.FlyingSwords.OrbitRadius = 2.2f; c.Enemies[0].Speed = 1f; c.Enemies[0].Hp = 100000;
            });
            for (int i = 0; i < 1024; i++) { float a = i * 2.399963f; t.Spawn(1, new float2(math.cos(a), math.sin(a)) * (2f + math.sqrt(i) * .25f)); }
            t.Step(45);
            // Exercise generation reuse during dense targeting, before the allocation sample.
            for (int i = 0; i < 32; i++) { var h = t.World.Table(SvKeys.Enemy).Handles[i]; t.World.DestroyEntity(h); t.Spawn(1, new float2(5, i * .02f)); }
            var state = S(t);
            int candidatesBefore = state.Counters[4], contactsBefore = state.Counters[5], hitsBefore = state.Counters[1],
                historyBefore = state.Counters[2], queuesBefore = state.Counters[3], launchesBefore = state.Counters[0];
            Action work = () => t.Step(120);
            using var probe = new ManagedAllocationProbe(); var pre = probe.Calibrate();
            var watch = Stopwatch.StartNew(); var sample = probe.Measure(work); watch.Stop(); var post = probe.Calibrate();
            TestContext.WriteLine($"Report-only .NET/stub logic: 1024 enemies, 24 swords, 120 ticks after 45 warmup ticks; {watch.Elapsed.TotalMilliseconds:F2} ms; candidates={state.Counters[4] - candidatesBefore}, sweptContacts={state.Counters[5] - contactsBefore}, queuedHits={state.Counters[1] - hitsBefore}, historyRejects={state.Counters[2] - historyBefore}, queueRejects={state.Counters[3] - queuesBefore}, launches={state.Counters[0] - launchesBefore}. Allocation={sample.Value} {sample.Metric}; calibration before={pre.RetainedArrays.Value}/{pre.Empty.Value}, after={post.RetainedArrays.Value}/{post.Empty.Value}; no device-performance claim.");
            Assert.Greater(state.Counters[4], 1000); Assert.Greater(state.Counters[1], 100); Assert.Greater(state.Counters[0], 24);
            Assert.AreEqual(0, state.Counters[3]); Assert.AreEqual(0, sample.Value);
            Assert.AreEqual(24, state.ActiveCount); Assert.LessOrEqual(t.Enemies, 1024);
        }
    }
}
