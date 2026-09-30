using System.Collections.Generic;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SPF.Tests.EditMode
{
    public class PipelineTests
    {
        sealed class RecordingSystem : SimSystemBase
        {
            readonly List<string> m_Log;
            readonly string m_Name;
            readonly SimPhase m_Phase;
            readonly int m_Order;

            public RecordingSystem(List<string> log, string name, SimPhase phase, int order = 0)
            {
                m_Log = log;
                m_Name = name;
                m_Phase = phase;
                m_Order = order;
            }

            public override SimPhase Phase => m_Phase;
            public override int Order => m_Order;
            public override void Declare(AccessDeclaration access) { }

            public override JobHandle OnTick(in SimContext context, JobHandle dependency)
            {
                m_Log.Add(m_Name);
                return dependency;
            }
        }

        [Test]
        public void SystemsRunInPhaseThenOrderThenRegistrationOrder()
        {
            var log = new List<string>();
            using var world = TestKeys.CreateWorld(4);
            var systems = new ISimSystem[]
            {
                new RecordingSystem(log, "snapshot", SimPhase.Snapshot),
                new RecordingSystem(log, "move-late", SimPhase.Move, order: 10),
                new RecordingSystem(log, "move-a", SimPhase.Move),
                new RecordingSystem(log, "move-b", SimPhase.Move),
                new RecordingSystem(log, "apply", SimPhase.ApplyCommands),
            };
            using var pipeline = new TickPipeline(world, systems);

            pipeline.BeginTick(new TickTime(0, 0.033f, 0));
            pipeline.EndTick();

            CollectionAssert.AreEqual(new[] { "apply", "move-a", "move-b", "move-late", "snapshot" }, log);
        }

        [BurstCompile]
        struct FillJob : IJobParallelFor
        {
            public NativeArray<int> Values;
            public int Base;
            public void Execute(int index) => Values[index] = Base + index;
        }

        [BurstCompile]
        struct SumToSnapshotJob : IJob
        {
            [ReadOnly] public NativeArray<int> Values;
            public NativeArray<int> Output;
            public NativeArray<int> OutputCount;
            public int Count;

            public void Execute()
            {
                int sum = 0;
                for (int i = 0; i < Count; i++) sum += Values[i];
                Output[0] = sum;
                OutputCount[0] = 1;
            }
        }

        sealed class SpawnSystem : SimSystemBase
        {
            public override SimPhase Phase => SimPhase.ApplyCommands;
            public override void Declare(AccessDeclaration access) => access.Write(TestKeys.Item);

            public override JobHandle OnTick(in SimContext context, JobHandle dependency)
            {
                while (context.Count(TestKeys.Item) < 64)
                    context.World.CreateEntity(TestKeys.Item, out _);
                return dependency;
            }
        }

        sealed class FillSystem : SimSystemBase
        {
            public override SimPhase Phase => SimPhase.Move;
            public override void Declare(AccessDeclaration access) => access.Write(TestKeys.Value);

            public override JobHandle OnTick(in SimContext context, JobHandle dependency) =>
                new FillJob { Values = context.Column(TestKeys.Value), Base = (int)context.Time.Tick }
                    .Schedule(context.Count(TestKeys.Item), 8, dependency);
        }

        sealed class SumSystem : SimSystemBase
        {
            public override SimPhase Phase => SimPhase.Snapshot;
            public override void Declare(AccessDeclaration access) => access.Read(TestKeys.Value).Write(TestKeys.Snapshot);

            public override JobHandle OnTick(in SimContext context, JobHandle dependency)
            {
                var snapshot = context.Resource(TestKeys.Snapshot);
                return new SumToSnapshotJob
                {
                    Values = context.Column(TestKeys.Value),
                    Output = snapshot.Write,
                    OutputCount = snapshot.WriteCount,
                    Count = context.Count(TestKeys.Item),
                }.Schedule(dependency);
            }
        }

        [Test]
        public void DeclaredAccessChainsJobsAndSnapshotsRotateAtSync()
        {
            using var world = TestKeys.CreateWorld(64);
            // Registered in reverse so ordering comes from phases, dependencies from declarations.
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new SumSystem(), new FillSystem(), new SpawnSystem() });
            var snapshot = world.Resource(TestKeys.Snapshot);

            for (uint tick = 0; tick < 3; tick++)
            {
                pipeline.BeginTick(new TickTime(tick, 0.033f, 0));
                pipeline.EndTick();

                int expected = 64 * (int)tick + 63 * 64 / 2;
                Assert.AreEqual(1, snapshot.CurrentCount);
                Assert.AreEqual(expected, snapshot.Current[0]);
            }
            Assert.AreEqual(3u, snapshot.Version);
            Assert.AreEqual(64 * 1 + 63 * 64 / 2, snapshot.Previous[0]);
        }

        [Test]
        public void BeginTickTwiceWithoutEndThrows()
        {
            using var world = TestKeys.CreateWorld(4);
            using var pipeline = new TickPipeline(world, new ISimSystem[0]);
            pipeline.BeginTick(new TickTime(0, 0.033f, 0));
            Assert.Throws<System.InvalidOperationException>(() => pipeline.BeginTick(new TickTime(1, 0.033f, 0)));
            pipeline.EndTick();
        }
    }
}
