// .NET-harness-only diagnostic. Compile against SPF.Runtime.Core + SPF.Testing from each compared
// checkout. No Unity-native, retained-heap, mobile, timing or steady-state allocation claim.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SPF.Contracts;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using SPF.Testing;
using Unity.Jobs;

static class Program
{
    static readonly AccessKey[] Keys = Enumerable.Range(0, 8).Select(i => (AccessKey)new ResourceKey<object>("ColdCost." + i)).ToArray();
    sealed class SystemUnderTest : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;
        public override void Declare(AccessDeclaration access)
        { for (int i = 0; i < Keys.Length; i++) { if (i < 4) access.Read(Keys[i]); else access.Write(Keys[i]); } }
        public override JobHandle OnTick(in SimContext context, JobHandle dependency) => dependency;
    }

    static object Inspect(int systemCount)
    {
        using var world = new SimWorld(new WorldLayout { DestroyQueueCapacity = 1 }, 1);
        var systems = Enumerable.Range(0, systemCount).Select(_ => (ISimSystem)new SystemUnderTest()).ToArray();
        TickPipeline pipeline = null;
        Action create = () => pipeline = new TickPipeline(world, systems);
        for (int i = 0; i < 5; i++) { create(); pipeline.Dispose(); }
        using var probe = new ManagedAllocationProbe();
        var before = probe.Calibrate();
        var construction = new List<long>();
        int collections = 0;
        for (int i = 0; i < 20; i++)
        {
            var sample = probe.Measure(create); construction.Add(sample.Value); collections += sample.Collections;
            pipeline.Dispose();
        }
        var exports = new List<long>();
        using (pipeline = new TickPipeline(world, systems))
        {
            // Reflect once so this identical diagnostic also compiles against the pre-A1 API.
            var method = typeof(TickPipeline).GetMethod("GetExecutionPlan");
            if (method != null)
            {
                var export = (Func<object>)method.CreateDelegate(typeof(Func<object>), pipeline);
                object retained = null;
                Action capture = () => retained = export();
                for (int i = 0; i < 5; i++) capture();
                for (int i = 0; i < 20; i++)
                { var sample = probe.Measure(capture); exports.Add(sample.Value); collections += sample.Collections; }
                GC.KeepAlive(retained);
            }
        }
        var after = probe.Calibrate();
        return new { systemCount, readsPerSystem = 4, writesPerSystem = 4, repetitions = 20,
            metric = probe.Metric.ToString(), thread = "current synchronous test thread", collections,
            beforePositive = before.RetainedArrays.Value, beforeEmpty = before.Empty.Value,
            afterPositive = after.RetainedArrays.Value, afterEmpty = after.Empty.Value,
            constructionBytes = construction, unformattedPlanExportBytes = exports };
    }

    static void Main() => Console.WriteLine(JsonSerializer.Serialize(new[] { Inspect(1), Inspect(20), Inspect(64) },
        new JsonSerializerOptions { WriteIndented = true }));
}
