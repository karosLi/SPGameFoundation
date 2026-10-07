using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;

namespace SPF.Tests.EditMode
{
    public class ExecutionPlanTests
    {
        sealed class ProbeSystem : SimSystemBase, ISnapshotSystem
        {
            readonly Action<AccessDeclaration> m_Declare;
            readonly SimPhase m_Phase;
            readonly int m_Order;
            public int Declarations;
            public int Ticks;
            public ProbeSystem(Action<AccessDeclaration> declare = null, SimPhase phase = SimPhase.Move, int order = 0)
            { m_Declare = declare; m_Phase = phase; m_Order = order; }
            public override SimPhase Phase => m_Phase;
            public override int Order => m_Order;
            public override void Declare(AccessDeclaration access) { Declarations++; m_Declare?.Invoke(access); }
            public override JobHandle OnTick(in SimContext context, JobHandle dependency) { Ticks++; return dependency; }
            public void WriteSnapshot(BinaryWriter writer) => writer.Write(Ticks);
            public void ReadSnapshot(BinaryReader reader, SimWorld world) => Ticks = reader.ReadInt32();
        }

        sealed class ProbeModule : IGameplayModule
        {
            readonly ISimSystem[] m_Systems;
            public string Id { get; }
            public ProbeModule(string id, params ISimSystem[] systems) { Id = id; m_Systems = systems; }
            public void DeclareData(WorldLayout layout) { }
            public void RegisterSystems(SystemRegistry registry) { foreach (var system in m_Systems) registry.Add(system); }
        }

        [TestCase(false, true, ExecutionDependencyReason.ReadToWrite)]
        [TestCase(true, false, ExecutionDependencyReason.WriteToRead)]
        [TestCase(true, true, ExecutionDependencyReason.WriteToWrite)]
        public void ConflictEdgesNameActualKeyAndReason(bool firstWrite, bool secondWrite, ExecutionDependencyReason reason)
        {
            using var world = TestKeys.CreateWorld(4);
            var a = new ProbeSystem(d => { if (firstWrite) d.Write(TestKeys.Value); else d.Read(TestKeys.Value); });
            var b = new ProbeSystem(d => { if (secondWrite) d.Write(TestKeys.Value); else d.Read(TestKeys.Value); });
            using var pipeline = new TickPipeline(world, new ISimSystem[] { a, b });
            var plan = pipeline.GetExecutionPlan();
            Assert.AreEqual(1, plan.Dependencies.Count);
            var edge = plan.Dependencies[0];
            Assert.AreEqual(0, edge.SourceIndex); Assert.AreEqual(1, edge.TargetIndex);
            Assert.AreEqual(reason, edge.Reason);
            Assert.AreEqual(TestKeys.Value.Id, edge.KeyId); Assert.AreEqual(TestKeys.Value.Name, edge.KeyName);
        }

        [Test]
        public void ReadReadAndPhaseTransitionsDoNotAddJobDependenciesOrCompletions()
        {
            using var world = TestKeys.CreateWorld(4);
            using var pipeline = new TickPipeline(world, new ISimSystem[] {
                new ProbeSystem(d => d.Read(TestKeys.Value), SimPhase.Input),
                new ProbeSystem(d => d.Read(TestKeys.Value), SimPhase.Snapshot) });
            var plan = pipeline.GetExecutionPlan();
            Assert.IsEmpty(plan.Dependencies);
            StringAssert.Contains("style=dashed", plan.ToDot());
            StringAssert.Contains("read " + TestKeys.Value.Name, plan.ToDot());
            Assert.IsFalse(plan.Completions.Any(c => c.Reason == ExecutionCompletionReason.Barrier));
            Assert.IsFalse(plan.Completions.Any(c => c.Reason == ExecutionCompletionReason.SerialProfiling));
        }

        [Test]
        public void WriterWaitsForAllReadersSinceLastWriterAndResetsReaderSet()
        {
            using var world = TestKeys.CreateWorld(4);
            using var pipeline = new TickPipeline(world, new ISimSystem[] {
                new ProbeSystem(d => d.Read(TestKeys.Value)), new ProbeSystem(d => d.Read(TestKeys.Value)),
                new ProbeSystem(d => d.Write(TestKeys.Value)), new ProbeSystem(d => d.Write(TestKeys.Value)) });
            var edges = pipeline.GetExecutionPlan().Dependencies;
            CollectionAssert.AreEqual(new[] { "0:2:ReadToWrite", "1:2:ReadToWrite", "2:3:WriteToWrite" },
                edges.Select(e => $"{e.SourceIndex}:{e.TargetIndex}:{e.Reason}").ToArray());
        }

        [Test]
        public void EmptyDeclarationBarrierWaitsForAllAndFeedsLaterSystems()
        {
            using var world = TestKeys.CreateWorld(4);
            using var pipeline = new TickPipeline(world, new ISimSystem[] {
                new ProbeSystem(d => d.Read(TestKeys.Value)), new ProbeSystem(),
                new ProbeSystem(d => d.Read(TestKeys.Snapshot)), new ProbeSystem() });
            var plan = pipeline.GetExecutionPlan();
            CollectionAssert.AreEqual(new[] { "0:1:BarrierAwaitsEarlierWork", "1:2:PreviousBarrier",
                "0:3:BarrierAwaitsEarlierWork", "1:3:BarrierAwaitsEarlierWork", "2:3:BarrierAwaitsEarlierWork" },
                plan.Dependencies.Select(e => $"{e.SourceIndex}:{e.TargetIndex}:{e.Reason}").ToArray());
            CollectionAssert.AreEqual(new[] { 1, 3 }, plan.Completions.Where(c => c.Reason == ExecutionCompletionReason.Barrier).Select(c => c.SystemIndex));
            Assert.IsTrue(plan.Systems[1].IsBarrier);
        }

        [Test]
        public void StablePhaseOrderRegistrationAndModuleProvenanceAreCapturedWithoutGuessing()
        {
            using var world = TestKeys.CreateWorld(4);
            var a = new ProbeSystem(order: 5); var b = new ProbeSystem(); var c = new ProbeSystem();
            var early = new ProbeSystem(phase: SimPhase.Input);
            var modules = new IGameplayModule[] { new ProbeModule("explicit.first", a, b), new ProbeModule("explicit.second", c, early) };
            using var pipeline = WorldComposer.BuildPipeline(modules, world);
            var plan = pipeline.GetExecutionPlan();
            CollectionAssert.AreEqual(new[] { 3, 1, 2, 0 }, plan.Systems.Select(s => s.RegistrationIndex));
            CollectionAssert.AreEqual(new[] { early, b, c, a }, Enumerable.Range(0, pipeline.SystemCount).Select(pipeline.GetSystem));
            CollectionAssert.AreEqual(new[] { "explicit.second", "explicit.first", "explicit.second", "explicit.first" }, plan.Systems.Select(s => s.Source.ModuleId));
            Assert.AreEqual(typeof(ProbeModule).FullName, plan.Systems[0].Source.ModuleType);
            Assert.AreEqual(typeof(ProbeSystem).FullName, plan.Systems[0].SystemType);
            Assert.AreEqual(SimPhase.Input, plan.Systems[0].Phase); Assert.AreEqual(5, plan.Systems[3].Order);
        }

        [Test]
        public void DirectPipelineAndRawRegistryKeepUnknownOriginAndInternalCompletion()
        {
            using var world = TestKeys.CreateWorld(4);
            var registry = new SystemRegistry().Add(new ProbeSystem());
            using var pipeline = new TickPipeline(world, registry.Systems);
            var plan = pipeline.GetExecutionPlan();
            Assert.IsFalse(plan.Systems[0].Source.IsKnown);
            Assert.AreEqual("unknown", plan.Systems[0].Source.ModuleId);
            Assert.AreEqual("unknown", plan.Systems[0].InternalCompletion);
            StringAssert.Contains("internal Complete=unknown", plan.ToText());
            StringAssert.Contains("source=unknown", plan.ToText());
        }

        [Test]
        public void ExportUsesSavedDeclarationOnceAndWriteUpgradeIsAccurate()
        {
            using var world = TestKeys.CreateWorld(4);
            var system = new ProbeSystem(d => d.Read(TestKeys.Value).Write(TestKeys.Snapshot).Write(TestKeys.Value).Read(TestKeys.Value));
            using var pipeline = new TickPipeline(world, new ISimSystem[] { system });
            var first = pipeline.GetExecutionPlan();
            for (int i = 0; i < 4; i++) { pipeline.GetExecutionPlan().ToText(); pipeline.GetExecutionPlan().ToDot(); }
            Assert.AreEqual(1, system.Declarations);
            Assert.IsEmpty(first.Systems[0].Reads);
            CollectionAssert.AreEqual(new[] { TestKeys.Snapshot.Name, TestKeys.Value.Name }, first.Systems[0].Writes.Select(k => k.Name));
            Assert.Throws<NotSupportedException>(() => ((IList<ExecutionPlanSystem>)first.Systems).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<ExecutionPlanKey>)first.Systems[0].Writes).Clear());
        }

        [Test]
        public void ExportDoesNotTickCompleteChangeProfilingOrChangeRawSnapshot()
        {
            using var world = TestKeys.CreateWorld(4);
            var system = new ProbeSystem(d => d.Write(TestKeys.Value));
            using var pipeline = new TickPipeline(world, new ISimSystem[] { system });
            pipeline.BeginTick(new TickTime(0, .033f, 0));
            var text = pipeline.GetExecutionPlan().ToText(); var dot = pipeline.GetExecutionPlan().ToDot();
            Assert.IsTrue(pipeline.HasPendingTick, "an exporter must not synchronize");
            Assert.AreEqual(1, system.Ticks); Assert.IsFalse(pipeline.SerialProfiling);
            var before = Snapshot(pipeline);
            for (int i = 0; i < 3; i++)
            { Assert.AreEqual(text, pipeline.GetExecutionPlan().ToText()); Assert.AreEqual(dot, pipeline.GetExecutionPlan().ToDot()); }
            CollectionAssert.AreEqual(before, Snapshot(pipeline));
        }

        [Test]
        public void FrameworkCompletionReasonsIncludeConditionalSerialAndFailureRecovery()
        {
            using var world = TestKeys.CreateWorld(4);
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new ProbeSystem(d => d.Read(TestKeys.Value)) });
            var normal = pipeline.GetExecutionPlan();
            pipeline.SerialProfiling = true;
            var serial = pipeline.GetExecutionPlan();
            Assert.IsFalse(normal.SerialProfiling); Assert.IsTrue(serial.SerialProfiling);
            Assert.IsFalse(normal.Completions.Any(c => c.Reason == ExecutionCompletionReason.SerialProfiling));
            Assert.AreEqual(1, serial.Completions.Count(c => c.Reason == ExecutionCompletionReason.SerialProfiling));
            Assert.IsTrue(serial.Completions.Any(c => c.Reason == ExecutionCompletionReason.EndTick));
            Assert.IsTrue(serial.Completions.Any(c => c.Reason == ExecutionCompletionReason.SchedulingFailureRecovery));
            StringAssert.Contains("SerialProfiling", serial.ToDot());
        }

        [Test]
        public void RemovingAddedModuleRestoresIdenticalPlan()
        {
            using var world = TestKeys.CreateWorld(4);
            var kept = new ProbeModule("kept", new ProbeSystem(d => d.Read(TestKeys.Value)));
            var extra = new ProbeModule("extra", new ProbeSystem(d => d.Write(TestKeys.Value)));
            using var before = WorldComposer.BuildPipeline(new IGameplayModule[] { kept }, world);
            using var added = WorldComposer.BuildPipeline(new IGameplayModule[] { kept, extra }, world);
            using var removed = WorldComposer.BuildPipeline(new IGameplayModule[] { kept }, world);
            Assert.AreEqual(before.GetExecutionPlan().ToText(), removed.GetExecutionPlan().ToText());
            Assert.AreEqual(2, added.GetExecutionPlan().Systems.Count);
            Assert.AreEqual(ExecutionDependencyReason.ReadToWrite, added.GetExecutionPlan().Dependencies.Single().Reason);
        }

        [Test]
        public void CapturedMetadataDoesNotAliasTheSavedDeclarationOrCallerProvenance()
        {
            using var world = TestKeys.CreateWorld(4);
            AccessDeclaration retained = null;
            var source = new[] { new SystemRegistrationSource("actual", "ActualModule") };
            using var pipeline = new TickPipeline(world, new ISimSystem[] {
                new ProbeSystem(d => { retained = d; d.Read(TestKeys.Value); }) }, source);
            var before = pipeline.GetExecutionPlan().ToText();
            retained.Write(TestKeys.Snapshot);
            source[0] = new SystemRegistrationSource("changed", "ChangedModule");
            Assert.AreEqual(before, pipeline.GetExecutionPlan().ToText(), "the report captures construction-time metadata");
        }

        [Test]
        public void MismatchedProvenanceIsRejectedBeforeDeclareOrOnCreate()
        {
            using var world = TestKeys.CreateWorld(4);
            var system = new ProbeSystem();
            Assert.Throws<ArgumentException>(() => new TickPipeline(world, new ISimSystem[] { system },
                new SystemRegistrationSource[0]));
            Assert.AreEqual(0, system.Declarations);
        }

        [Test]
        public void ExportsEscapeUserSuppliedNamesAndUseInvariantOrderNumbers()
        {
            using var world = TestKeys.CreateWorld(4);
            var key = new ResourceKey<object>("key \"quoted\"\nnext\\part");
            var source = new[] { new SystemRegistrationSource("module\n\"name\"", "Type\\Name") };
            using var pipeline = new TickPipeline(world, new ISimSystem[] { new ProbeSystem(d => d.Read(key), order: -10) }, source);
            var prior = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
                var plan = pipeline.GetExecutionPlan();
                StringAssert.Contains("order=-10", plan.ToText());
                StringAssert.Contains("module\\n\\\"name\\\"", plan.ToText());
                StringAssert.Contains("key \\\"quoted\\\"\\nnext\\\\part", plan.ToDot());
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = prior; }
        }

        static byte[] Snapshot(TickPipeline pipeline)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            pipeline.WriteSnapshot(writer); writer.Flush(); return stream.ToArray();
        }
    }
}
