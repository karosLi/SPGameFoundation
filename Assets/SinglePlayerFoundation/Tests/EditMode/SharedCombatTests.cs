using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class SharedCombatTests
    {
        [Test]
        public void HistorySurvivesSwapbackSortAndRecycledSlots()
        {
            var key = new TableKey("Test.SharedCombat");
            var layout = new WorldLayout(); layout.Table(key, 4);
            using var world = new SimWorld(layout, 9);
            using var targets = new NativeArray<EntityHandle>(4, Allocator.Temp);
            var removed = world.CreateEntity(key, out _);
            var survivor = world.CreateEntity(key, out _);
            var state = default(HitHistoryState);
            Assert.IsTrue(HitHistory.Begin(targets, 0, 4, ref state, survivor, 1));
            Assert.AreEqual(HitRecordResult.Added, HitHistory.TryRecord(targets, 0, 4, ref state, survivor));
            world.DestroyEntity(removed);
            Assert.AreEqual(survivor, world.Table(key).Handles[0]);
            Assert.AreEqual(HitRecordResult.Duplicate, HitHistory.TryRecord(targets, 0, 4, ref state, world.Table(key).Handles[0]));
            var recycled = world.CreateEntity(key, out _);
            Assert.AreEqual(removed.Index, recycled.Index);
            Assert.AreNotEqual(removed.Generation, recycled.Generation);
            Assert.AreEqual(HitRecordResult.Added, HitHistory.TryRecord(targets, 0, 4, ref state, removed));
            Assert.AreEqual(HitRecordResult.Added, HitHistory.TryRecord(targets, 0, 4, ref state, recycled));
            using var keys = new NativeArray<uint>(new uint[] { 2, 1 }, Allocator.Temp);
            world.SortRows(key, keys);
            Assert.AreEqual(HitRecordResult.Duplicate, HitHistory.Check(targets, 0, 4, state, survivor));
            Assert.AreEqual(HitRecordResult.Duplicate, HitHistory.Check(targets, 0, 4, state, recycled));
        }

        [Test]
        public void HistoryCapacityPulseOwnerAndDisjointScopesHaveExplicitContracts()
        {
            using var targets = new NativeArray<EntityHandle>(130, Allocator.Temp);
            var a = default(HitHistoryState); var b = default(HitHistoryState);
            var owner = new EntityHandle(200, 1);
            Assert.IsTrue(HitHistory.Begin(targets, 0, 65, ref a, owner, 1));
            Assert.IsTrue(HitHistory.Begin(targets, 65, 65, ref b, owner, 2));
            Assert.AreEqual(HitRecordResult.InvalidTarget, HitHistory.TryRecord(targets, 0, 65, ref a, EntityHandle.Null));
            for (int i = 0; i < 65; i++) Assert.AreEqual(HitRecordResult.Added, HitHistory.TryRecord(targets, 0, 65, ref a, new EntityHandle(i, 1)));
            Assert.AreEqual(HitRecordResult.Duplicate, HitHistory.TryRecord(targets, 0, 65, ref a, new EntityHandle(64, 1)));
            Assert.AreEqual(HitRecordResult.Full, HitHistory.TryRecord(targets, 0, 65, ref a, new EntityHandle(65, 1)));
            Assert.AreEqual(65, a.Count);
            Assert.AreEqual(HitRecordResult.Added, HitHistory.TryRecord(targets, 65, 65, ref b, new EntityHandle(64, 1)), "independent simultaneous scope");
            HitHistory.Begin(targets, 0, 65, ref a, owner, 1);
            Assert.AreEqual(65, a.Count, "same pulse does not clear between shapes");
            HitHistory.Begin(targets, 0, 65, ref a, owner, 2);
            Assert.AreEqual(0, a.Count, "next pulse may hit again");
            Assert.IsTrue(targets[64].IsNull, "inactive prefix is cleared deterministically");
            HitHistory.TryRecord(targets, 0, 65, ref a, new EntityHandle(64, 1));
            HitHistory.Begin(targets, 0, 65, ref a, new EntityHandle(owner.Index, 2), 2);
            Assert.AreEqual(0, a.Count, "source generation changed");
            Assert.IsFalse(HitHistory.Begin(targets, -1, 65, ref a, owner, 3));
            Assert.IsFalse(HitHistory.Begin(targets, 129, 2, ref a, owner, 3));
            Assert.IsFalse(HitHistory.Begin(targets, 0, 65, ref a, owner, 0));
            HitHistory.Release(targets, 65, 65, ref b);
            Assert.AreEqual(HitRecordResult.InvalidScope, HitHistory.Check(targets, 65, 65, b, owner));
            Assert.IsTrue(HitHistory.Begin(targets, 130, 0, ref b, owner, 1));
            Assert.AreEqual(HitRecordResult.Full, HitHistory.Check(targets, 130, 0, b, owner));
        }

        [Test]
        public void HistoryAndTimelineSnapshotResumeExactScope()
        {
            using var targets = new NativeArray<EntityHandle>(3, Allocator.Temp);
            using var restored = new NativeArray<EntityHandle>(3, Allocator.Temp);
            var scope = default(HitHistoryState);
            HitHistory.Begin(targets, 0, 3, ref scope, new EntityHandle(7, 2), 9);
            HitHistory.TryRecord(targets, 0, 3, ref scope, new EntityHandle(1, 4));
            var timeline = new ActionTimeline(); timeline.Begin(); timeline.Advance(8);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            NativeIO.WriteValue(writer, scope); NativeIO.Write(writer, targets); NativeIO.WriteValue(writer, timeline);
            stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            var resumed = NativeIO.ReadValue<HitHistoryState>(reader); NativeIO.ReadAll(reader, restored);
            var resumedTimeline = NativeIO.ReadValue<ActionTimeline>(reader);
            Assert.AreEqual(HitRecordResult.Duplicate, HitHistory.TryRecord(restored, 0, 3, ref resumed, new EntityHandle(1, 4)));
            Assert.AreEqual(HitRecordResult.Added, HitHistory.TryRecord(restored, 0, 3, ref resumed, new EntityHandle(1, 5)));
            timeline.Advance(3); resumedTimeline.Advance(3);
            Assert.AreEqual(timeline.Tick, resumedTimeline.Tick);
            Assert.AreEqual(timeline.PulseId, resumedTimeline.PulseId);
        }

        [Test]
        public void TimelineIncludesTickZeroAndSkippedWindowsButCancelUsesCurrentTick()
        {
            var timeline = new ActionTimeline(); timeline.Begin();
            Assert.IsTrue(timeline.Crossed(new ActionWindow(0, 1)));
            timeline.Advance(10);
            Assert.IsTrue(timeline.Crossed(new ActionWindow(3, 5)), "large fixed-tick step cannot miss active window");
            Assert.IsFalse(timeline.TryCancel(new ActionWindow(3, 5)), "cannot retroactively cancel");
            Assert.IsTrue(timeline.Crossed(new ActionWindow(10, 11)));
            Assert.IsFalse(timeline.Crossed(new ActionWindow(11, 13)));
            timeline.Advance(0);
            Assert.IsFalse(timeline.Crossed(new ActionWindow(10, 11)), "zero step cannot repeat a pulse");
            Assert.IsFalse(timeline.Advance(-1));
            Assert.IsTrue(timeline.TryCancel(new ActionWindow(10, 11)));
            Assert.IsFalse(timeline.Advance());
            timeline.Begin();
            Assert.AreEqual(2, timeline.PulseId);
            Assert.AreEqual(0, timeline.Tick);
            Assert.IsFalse(timeline.Crossed(new ActionWindow(4, 4)));
            timeline.PulseId = uint.MaxValue; timeline.Begin(); Assert.AreEqual(1, timeline.PulseId);
        }

        [Test]
        public void InputBufferExpiresAtBoundaryPreservesBlockedAndConsumesOnce()
        {
            var input = new TickInputBuffer();
            Assert.IsFalse(input.Push(1, 10, 0));
            Assert.IsTrue(input.Push(1, 10, 4));
            Assert.IsFalse(input.TryConsume(9, true, out _));
            Assert.IsFalse(input.TryConsume(11, false, out _));
            Assert.IsTrue(input.TryConsume(13, true, out int command)); Assert.AreEqual(1, command);
            Assert.IsFalse(input.TryConsume(13, true, out _));
            input.Push(1, 20, 4); input.Push(2, 21, 4);
            Assert.IsTrue(input.TryConsume(24, true, out command)); Assert.AreEqual(2, command);
            input.Push(1, 30, 4); Assert.IsFalse(input.TryConsume(34, true, out _)); Assert.IsFalse(input.Pending);
            input.Push(1, 40, 4); input.Clear(); Assert.IsFalse(input.TryConsume(40, true, out _));
        }

        [Test]
        public void RelativeSweepIncludesTangencyEndpointsInitialOverlapAndMovingTargets()
        {
            Assert.IsTrue(CombatSweep.Circles(new float2(-2, 1), new float2(2, 1), .5f, float2.zero, float2.zero, .5f, out float t));
            Assert.AreEqual(.5f, t, .00001f);
            Assert.IsTrue(CombatSweep.Circles(float2.zero, float2.zero, .5f, new float2(4, 0), float2.zero, .5f, out t));
            Assert.AreEqual(.75f, t, .00001f, "stationary bullet and moving target");
            Assert.IsTrue(CombatSweep.Circles(new float2(-2, 0), new float2(2, 0), .5f, new float2(2, 0), new float2(-2, 0), .5f, out t));
            Assert.AreEqual(.375f, t, .00001f);
            Assert.IsTrue(CombatSweep.Circles(float2.zero, float2.zero, 1, float2.zero, float2.zero, 1, out t)); Assert.AreEqual(0, t);
            Assert.IsFalse(CombatSweep.Circles(float2.zero, new float2(2, 0), .5f, new float2(4, 0), new float2(6, 0), .5f, out _));
            Assert.IsTrue(CombatSweep.PointCircle(new float2(-2, 0), new float2(-1, 0), float2.zero, 1, out t)); Assert.AreEqual(1, t);
            Assert.IsFalse(CombatSweep.PointCircle(new float2(-2, 1.001f), new float2(2, 1.001f), float2.zero, 1, out _));
        }

        [BurstCompile(CompileSynchronously = true)]
        struct PrimitiveJob : IJob
        {
            public NativeArray<EntityHandle> Targets;
            public NativeArray<int> Result;
            public void Execute()
            {
                var scope = default(HitHistoryState);
                HitHistory.Begin(Targets, 0, Targets.Length, ref scope, new EntityHandle(1, 1), 1);
                var added = HitHistory.TryRecord(Targets, 0, Targets.Length, ref scope, new EntityHandle(65, 2));
                var timeline = new ActionTimeline(); timeline.Begin(); timeline.Advance(9);
                var input = new TickInputBuffer(); input.Push(7, 5, 6);
                Result[0] = added == HitRecordResult.Added && timeline.Crossed(new ActionWindow(3, 6)) && input.TryConsume(9, true, out _) &&
                    CombatSweep.Circles(float2.zero, new float2(4, 0), .5f, new float2(4, 0), float2.zero, .5f, out _) ? 1 : 0;
            }
        }

        [Test]
        public void PrimitivesExecuteInBurstJobAndAllocateNoManagedMemoryWhenWarm()
        {
            using var targets = new NativeArray<EntityHandle>(8, Allocator.Persistent);
            using var result = new NativeArray<int>(1, Allocator.Persistent);
            var job = new PrimitiveJob { Targets = targets, Result = result };
            job.Schedule().Complete(); Assert.AreEqual(1, result[0]);
            for (int i = 0; i < 32; i++) job.Execute();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) job.Execute();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, bytes, "primitive calls only; scheduler/frame allocations are a separate measurement");
        }
    }
}
