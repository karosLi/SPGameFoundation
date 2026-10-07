using System;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using SPF.Testing;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Jobs;

namespace SPF.Tests.EditMode
{
    [NonParallelizable]
    public class AccessStructureWindowTests
    {
        static readonly TableKey Things = new TableKey("AccessWindow.Things");
        static readonly ColumnKey<int> Value = new ColumnKey<int>(Things, "Value");
        static readonly ColumnKey<int> Other = new ColumnKey<int>(Things, "Other");
        static readonly TableKey Pool = new TableKey("AccessWindow.Pool");
        static readonly ColumnKey<int> PooledValue = new ColumnKey<int>(Pool, "Value");
        static TickTime Time => new TickTime(1, 1f / 30f, 0);
        bool m_WasEnabled;

        [SetUp]
        public void SetUp()
        {
            m_WasEnabled = AccessGuard.Enabled; AccessGuard.Enabled = true;
#if SPF_DOTNET_HARNESS
            JobHandle.TrackScheduledOwnershipForTesting = true;
#endif
        }

        [TearDown]
        public void TearDown()
        {
            AccessGuard.Enabled = m_WasEnabled;
#if SPF_DOTNET_HARNESS
            JobHandle.TrackScheduledOwnershipForTesting = false;
#endif
        }

        [TestCase("world"), TestCase("table"), TestCase("context")]
        public void ReadDeclarationCannotAcquireExplicitWrite(string route)
        {
            using var world = World();
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Callback { DeclareAction = a => a.Read(Value), Action = w => AcquireWrite(w, route) } });
            var e = Assert.Throws<InvalidOperationException>(() => pipeline.BeginTick(Time));
            StringAssert.Contains(Value.Name, e.Message);
            StringAssert.Contains("Write", e.Message);
        }

        [TestCase("world"), TestCase("table"), TestCase("context")]
        public void WriteDeclarationCanReadAndWriteAndReadOnlyViewHasNoSetter(string route)
        {
            using var world = World();
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Callback {
                DeclareAction = a => a.Write(Value), Action = w => {
                    var values = AcquireWrite(w, route); values[0] = 42;
                    NativeArray<int>.ReadOnly view = AcquireRead(w, route);
                    Assert.Null(view.GetType().GetProperty("Item").SetMethod, "read-only API has no index setter");
                    Assert.AreEqual(42, view[0]);
                    Assert.AreEqual(w.Table(Things).Capacity, view.Length, "views retain full capacity");
                } } });
            pipeline.BeginTick(Time); pipeline.EndTick();
        }

        [Test]
        public void UndeclaredExplicitWriteIsRejectedButLegacyReadDeclarationRemainsCompatible()
        {
            using var world = World();
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Callback { DeclareAction = a => a.Read(Other), Action = w => w.WriteColumn(Value) } });
            Assert.Throws<InvalidOperationException>(() => pipeline.BeginTick(Time));
            using var legacy = new TickPipeline(world, new ISimSystem[] { new Callback { DeclareAction = a => a.Read(Value), Action = w => { var values = w.Column(Value); values[0] = 7; } } });
            Assert.DoesNotThrow(() => { legacy.BeginTick(Time); legacy.EndTick(); });
        }

        [TestCase("create"), TestCase("destroy"), TestCase("spawn"), TestCase("spawn-range"),
         TestCase("compact"), TestCase("sort"), TestCase("clear-level"), TestCase("reset")]
        public void ReturnedWorkRejectsStructuralMutationUntilSync(string operation)
        {
            using var world = World();
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Writer() });
            pipeline.BeginTick(Time);
            int alive = world.Registry.AliveCount, count = world.Table(Things).Count, pooled = world.Table(Pool).Count;
            uint version = world.Table(Things).Version, pooledVersion = world.Table(Pool).Version;
            int level = world.LevelVersion, failures = world.CreateFailures;
            try
            {
                Assert.Throws<InvalidOperationException>(() => Mutate(world, operation));
                Assert.AreEqual(alive, world.Registry.AliveCount);
                Assert.AreEqual(count, world.Table(Things).Count); Assert.AreEqual(pooled, world.Table(Pool).Count);
                Assert.AreEqual(version, world.Table(Things).Version); Assert.AreEqual(pooledVersion, world.Table(Pool).Version);
                Assert.AreEqual(level, world.LevelVersion); Assert.AreEqual(failures, world.CreateFailures);
            }
            finally { pipeline.EndTick(); }
            Assert.DoesNotThrow(() => Mutate(world, operation));
        }

        [TestCase("world"), TestCase("table"), TestCase("context")]
        public void UndeclaredReadOnlyAcquisitionIsRejected(string route)
        {
            using var world = World();
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Callback { DeclareAction = a => a.Read(Other), Action = w => AcquireRead(w, route) } });
            Assert.Throws<InvalidOperationException>(() => pipeline.BeginTick(Time));
        }

        [Test]
        public void ReadOnlyNativeViewCanBePassedToDependentJob()
        {
            using var world = World();
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Writer(), new ReadViewSystem() });
            pipeline.BeginTick(Time); pipeline.EndTick();
            Assert.AreEqual(9, world.ReadColumn(Other)[0]);
        }

        [Test]
        public void CountOnlyAccessDiagnosticsStillRejectStructureAndStorageDisposal()
        {
            using var world = World();
            world.Guard.Throw = false;
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Writer() });
            pipeline.BeginTick(Time);
            try
            {
                Assert.Throws<InvalidOperationException>(() => world.ClearLevel());
                Assert.Throws<InvalidOperationException>(() => world.Table(Things).Dispose());
                Assert.Throws<InvalidOperationException>(() => world.Dispose());
                Assert.Throws<InvalidOperationException>(() => world.ReadSnapshot(new BinaryReader(new MemoryStream())));
                Assert.Throws<InvalidOperationException>(() => world.WriteSnapshot(new BinaryWriter(new MemoryStream())));
                Assert.IsTrue(world.Table(Things).Handles.IsCreated);
                Assert.AreEqual(0, world.LevelVersion); Assert.AreEqual(2, world.Registry.AliveCount);
                Assert.AreEqual(5, world.Guard.Violations);
            }
            finally { pipeline.EndTick(); }
        }

        [Test]
        public void SessionSyncReopensOnlyTheOwningSession()
        {
            using var a = new SimSession(new[] { new Module() }, SessionSettings.Default, 1);
            using var b = new SimSession(new[] { new Module() }, SessionSettings.Default, 1);
            a.World.CreateEntity(Things, out _); b.World.CreateEntity(Things, out _);
            a.Pipeline.BeginTick(Time); b.Pipeline.BeginTick(Time);
            try
            {
                a.Sync();
                Assert.DoesNotThrow(() => a.World.CreateEntity(Things, out _));
                Assert.Throws<InvalidOperationException>(() => b.World.CreateEntity(Things, out _));
            }
            finally { b.Sync(); }
            Assert.DoesNotThrow(() => b.World.CreateEntity(Things, out _));
        }

        [Test]
        public void WarmSuccessPathsAllocateNothingWithEnabledAndDisabledChecks()
        {
            using var world = World();
            var context = new SimContext(world, Time); var table = world.Table(Things);
            var readable = new bool[Value.Id + 1]; readable[Value.Id] = true;
            var writable = new bool[Value.Id + 1]; writable[Value.Id] = true;
            // Keep a non-default copy of an explicitly completed handle: exercises bookkeeping only,
            // with no native worker or borrowed storage inside the measured main-thread loop.
            var returned = new EmptyJob().Schedule(); var completing = returned; completing.Complete();
            const int iterations = 10000, sampleCount = 31;
            Action body = () => {
                world.Guard.Begin(readable, writable, "CostProbe");
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var a = context.WriteColumn(Value); a[0] = context.ReadColumn(Value)[0];
                        var b = table.WriteColumn(Value); b[0] = table.ReadColumn(Value)[0];
                        var c = world.WriteColumn(Value); c[0] = world.ReadColumn(Value)[0];
                        world.Guard.RecordReturnedWork(returned); world.Guard.CompleteReturnedWork(returned);
                        var entity = world.CreateEntity(Things, out _); world.DestroyEntity(entity);
                    }
                }
                finally { world.Guard.End(); }
            };
            var disabled = new double[sampleCount]; var enabled = new double[sampleCount];
            using var probe = new ManagedAllocationProbe();
            foreach (bool checks in new[] { false, true })
            {
                AccessGuard.Enabled = checks;
                for (int warm = 0; warm < 5; warm++) body();
                var before = probe.Calibrate(); var sample = probe.Measure(body); var after = probe.Calibrate();
                TestContext.WriteLine($"SPF_ACCESS_ALLOCATION enabled={checks} iterations={iterations} value={sample.Value} metric={sample.Metric} gen0={sample.Collections} controlsBefore={before.RetainedArrays.Value}/{before.Empty.Value} controlsAfter={after.RetainedArrays.Value}/{after.Empty.Value}");
                Assert.AreEqual(0, sample.Value);
            }
            // Alternating pair order reduces a simple warm-up/order bias. Timing is diagnostic only,
            // without changing any platform budget or treating stub timings as mobile performance.
            for (int sample = 0; sample < sampleCount; sample++)
                for (int turn = 0; turn < 2; turn++)
                {
                    bool checks = ((sample + turn) & 1) != 0;
                    AccessGuard.Enabled = checks;
                    long begin = Stopwatch.GetTimestamp(); body();
                    (checks ? enabled : disabled)[sample] = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
                }
            TestContext.WriteLine("SPF_ACCESS_COST disabled_ms=" + string.Join(",", disabled));
            TestContext.WriteLine("SPF_ACCESS_COST enabled_ms=" + string.Join(",", enabled));
        }

        [TestCase(SimPhase.ApplyCommands), TestCase(SimPhase.Resolve)]
        public void PhaseLabelDoesNotOpenWindowWithEarlierReturnedWork(SimPhase phase)
        {
            using var world = World();
            var mutate = new Callback { PhaseValue = phase, OrderValue = 1, DeclareAction = a => a.Write(Other), Action = w => w.CreateEntity(Things, out _) };
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Writer { PhaseValue = phase }, mutate });
            Assert.Throws<InvalidOperationException>(() => pipeline.BeginTick(Time));
            Assert.IsFalse(pipeline.HasPendingTick, "successful exception recovery completes owned work");
            Assert.DoesNotThrow(() => world.CreateEntity(Things, out _));
        }

        [Test]
        public void OnCreateBetweenTicksCompletedBarrierAndSerialCompletionAreLegal()
        {
            using var world = World();
            var barrier = new Callback { Action = w => w.CreateEntity(Things, out _), OnCreating = w => w.CreateEntity(Things, out _) };
            using (var pipeline = new TickPipeline(world, new ISimSystem[] { new Writer(), barrier }))
            {
                pipeline.BeginTick(Time);
                Assert.DoesNotThrow(() => world.CreateEntity(Things, out _), "all returned handles are already completed by barrier");
                pipeline.EndTick();
            }
            using var serial = new TickPipeline(world, new ISimSystem[] { new Writer(), new Callback { DeclareAction = a => a.Write(Other), Action = w => w.CreateEntity(Things, out _) } });
            serial.SerialProfiling = true;
            Assert.DoesNotThrow(() => { serial.BeginTick(Time); serial.EndTick(); });
        }

        [Test]
        public void CompletedBarrierThenSynchronousDeclaredReaderDoesNotReborrowCompletedWork()
        {
            using var world = World();
            using var pipeline = new TickPipeline(world, new ISimSystem[] {
                new Writer(), new Callback { OrderValue = 0 },
                new Callback { OrderValue = 1, DeclareAction = a => a.Read(Value) } });
            pipeline.BeginTick(Time);
            try { Assert.DoesNotThrow(() => world.CreateEntity(Things, out _), "an unchanged dependency after the completed barrier is already owned by the main thread"); }
            finally { pipeline.EndTick(); }
        }

        [Test]
        public void SynchronousDeclaredSystemDoesNotCloseStructuralWindow()
        {
            using var world = World();
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Callback { DeclareAction = a => a.Read(Value) } });
            pipeline.BeginTick(Time);
            Assert.DoesNotThrow(() => world.CreateEntity(Things, out _), "a pending tick is not evidence of returned jobs");
            pipeline.EndTick();
        }

        [Test]
        public void TwoWorldsDoNotShareAccessOrOutstandingOwnership()
        {
            using var a = World(); using var b = World();
            using var pa = new TickPipeline(a, new ISimSystem[] { new Writer() });
            using var pb = new TickPipeline(b, new ISimSystem[] { new Callback { DeclareAction = d => d.Read(Other), Action = w => a.WriteColumn(Value) } });
            pa.BeginTick(Time);
            try
            {
                Assert.DoesNotThrow(() => b.CreateEntity(Things, out _));
                // This only acquires an alias in a, no main-thread read/write while its job is outstanding.
                Assert.DoesNotThrow(() => { pb.BeginTick(Time); pb.EndTick(); });
                Assert.Throws<InvalidOperationException>(() => a.CreateEntity(Things, out _));
            }
            finally { pa.EndTick(); }
        }

#if SPF_DOTNET_HARNESS
        [Test]
        public void AcquiringExplicitViewsNeverCompletesOutstandingJobs()
        {
            using var world = World();
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Writer() });
            int completions = 0;
            var hook = typeof(TickPipeline).GetField("m_BeforeCompleteForTesting", BindingFlags.Instance | BindingFlags.NonPublic);
            hook.SetValue(pipeline, (Action<JobHandle>)(_ => completions++));
            pipeline.BeginTick(Time);
            _ = world.ReadColumn(Value); _ = world.WriteColumn(Value); _ = world.Column(Value);
            Assert.AreEqual(0, completions);
            Assert.Throws<InvalidOperationException>(() => world.CreateEntity(Things, out _));
            pipeline.EndTick(); Assert.AreEqual(1, completions);
        }
#endif

#if SPF_DOTNET_HARNESS
        [Test]
        public void SimulatedCompletionRejectionRetainsWindowUntilSuccessfulRetry()
        {
            using var world = World();
            var cause = new Exception("OnTick failure");
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new Writer(), new Callback { DeclareAction = a => a.Write(Other), Action = w => throw cause } });
            var hook = typeof(TickPipeline).GetField("m_BeforeCompleteForTesting", BindingFlags.Instance | BindingFlags.NonPublic);
            hook.SetValue(pipeline, (Action<JobHandle>)(_ => throw new Exception("simulated pre-completion rejection")));
            try
            {
                Assert.AreSame(cause, Assert.Catch(() => pipeline.BeginTick(Time)));
                Assert.IsTrue(pipeline.HasPendingTick);
                Assert.Throws<InvalidOperationException>(() => world.ClearLevel());
                Assert.Throws<InvalidOperationException>(() => world.Dispose());
                Assert.IsTrue(world.Table(Things).Handles.IsCreated);
            }
            finally { hook.SetValue(pipeline, null); pipeline.EndTick(); }
            Assert.DoesNotThrow(() => world.ClearLevel());
        }
#endif

        static NativeArray<int> AcquireWrite(SimWorld world, string route)
        {
            switch (route)
            {
                case "world": return world.WriteColumn(Value);
                case "table": return world.Table(Things).WriteColumn(Value);
                case "context": return new SimContext(world, Time).WriteColumn(Value);
                default: throw new ArgumentException(route);
            }
        }

        static NativeArray<int>.ReadOnly AcquireRead(SimWorld world, string route)
        {
            switch (route)
            {
                case "world": return world.ReadColumn(Value);
                case "table": return world.Table(Things).ReadColumn(Value);
                case "context": return new SimContext(world, Time).ReadColumn(Value);
                default: throw new ArgumentException(route);
            }
        }

        sealed class Module : IGameplayModule
        {
            public string Id => "AccessWindow";
            public void DeclareData(WorldLayout layout) => layout.Table(Things, 16).Column(Value).Column(Other);
            public void RegisterSystems(SystemRegistry registry) => registry.Add(new Writer());
        }

        sealed class ReadViewSystem : SimSystemBase
        {
            public override SimPhase Phase => SimPhase.Resolve;
            public override void Declare(AccessDeclaration access) => access.Read(Value).Write(Other);
            public override JobHandle OnTick(in SimContext context, JobHandle dependency) =>
                new ReadJob { Values = context.ReadColumn(Value), Output = context.WriteColumn(Other) }.Schedule(dependency);
        }

        struct EmptyJob : IJob { public void Execute() { } }

        struct ReadJob : IJob
        {
            [ReadOnly] public NativeArray<int>.ReadOnly Values;
            public NativeArray<int> Output;
            public void Execute() => Output[0] = Values[0];
        }

        static SimWorld World()
        {
            var layout = new WorldLayout();
            layout.Table(Things, 16).Column(Value).Column(Other).LevelScoped();
            layout.Table(Pool, 16).Pooled().Column(PooledValue).LevelScoped();
            var world = new SimWorld(layout, 1);
            world.CreateEntity(Things, out _); world.CreateEntity(Things, out _); world.Spawn(Pool);
            return world;
        }

        static void Mutate(SimWorld world, string operation)
        {
            switch (operation)
            {
                case "create": world.CreateEntity(Things, out _); break;
                case "destroy": world.DestroyEntity(world.Table(Things).Handles[0]); break;
                case "spawn": world.Spawn(Pool); break;
                case "spawn-range": world.SpawnRange(Pool, 1, out _); break;
                case "compact": world.CompactPools(); break;
                case "sort": using (var keys = new NativeArray<uint>(new uint[] { 2, 1 }, Allocator.Temp)) world.SortRows(Things, keys); break;
                case "clear-level": world.ClearLevel(); break;
                case "reset": world.Reset(); break;
                default: throw new ArgumentException(operation);
            }
        }

        sealed class Callback : SimSystemBase
        {
            public Action<AccessDeclaration> DeclareAction; public Action<SimWorld> Action, OnCreating;
            public SimPhase PhaseValue = SimPhase.Resolve; public int OrderValue;
            public override SimPhase Phase => PhaseValue;
            public override int Order => OrderValue;
            public override void Declare(AccessDeclaration access) => DeclareAction?.Invoke(access);
            public override void OnCreate(SimWorld world) => OnCreating?.Invoke(world);
            public override JobHandle OnTick(in SimContext context, JobHandle dependency) { Action?.Invoke(context.World); return dependency; }
        }

        sealed class Writer : SimSystemBase
        {
            public SimPhase PhaseValue = SimPhase.Move;
            public override SimPhase Phase => PhaseValue;
            public override void Declare(AccessDeclaration access) => access.Write(Value);
            public override JobHandle OnTick(in SimContext context, JobHandle dependency) => new WriteJob { Values = context.Column(Value) }.Schedule(dependency);
        }
        struct WriteJob : IJob
        {
            public NativeArray<int> Values;
            public void Execute() => Values[0] = 9;
        }
    }

}
