using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.Testing;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    /// <summary>Paired, report-only measurements. A tree is a benchmark reference, never a gameplay replacement.</summary>
#if SPF_DOTNET_HARNESS
    [NonParallelizable]
#endif
    public class SpatialBroadphaseComparisonTests
    {
        struct Probe
        {
            public float2 Center, From, To;
            public float Radius;
            public bool Swept;
        }

        sealed class Data : IDisposable
        {
            public readonly NativeArray<GridEntry> Entries;
            public readonly NativeArray<float2> Previous, Current;
            public readonly NativeArray<float> Radii;
            public readonly NativeArray<Probe> Queries;
            public readonly float2 Origin, Size;
            public readonly int2 Dimensions;
            public readonly float Cell;
            public readonly string Name;
            public Data(string name, int count, int queries, uint seed)
            {
                Name = name;
                Cell = name == "sparse-huge" ? 125f : name.StartsWith("belt", StringComparison.Ordinal) ? 1f : 2f;
                Size = name == "sparse-huge" ? new float2(7500) : name.StartsWith("belt", StringComparison.Ordinal) ? new float2(20, 8) : new float2(64);
                Origin = -Size * .5f; Dimensions = (int2)math.ceil(Size / Cell);
                Entries = new NativeArray<GridEntry>(count, Allocator.Persistent);
                Previous = new NativeArray<float2>(count, Allocator.Persistent); Current = new NativeArray<float2>(count, Allocator.Persistent);
                Radii = new NativeArray<float>(count, Allocator.Persistent); Queries = new NativeArray<Probe>(queries, Allocator.Persistent);
                var random = new Unity.Mathematics.Random(seed);
                for (int i = 0; i < count; i++)
                {
                    float2 p = random.NextFloat2(Origin + .01f, Origin + Size - .01f);
                    float radius = .3f;
                    if (name == "clustered" && i % 10 != 0)
                        p = new float2((i % 4 % 2) * 24 - 12, (i % 4 / 2) * 24 - 12) + random.NextFloat2(-2f, 2f);
                    if (name == "mixed") radius = i % 100 == 0 ? 4f : i % 25 == 0 ? .75f : .3f;
                    if (name == "dense") p = i % 2 == 0 ? float2.zero : new float2(i % 16, i / 16) * .001f;
                    if (name == "belt-clustered") p = new float2((i % 16 - 7.5f) * .045f, (i / 16 - 3.5f) * .045f);
                    if (name == "belt-spread") p = new float2(-8f + i % (count / 4) * (16f / (count / 4 - 1)), -2.1f + i / (count / 4) * 1.4f);
                    float2 previous = p, current = p;
                    if (name == "high-speed")
                    {
                        // 10/25 world units per 60 Hz tick: the existing 600/1500 speed regressions.
                        float2 delta = i % 3 == 0 ? new float2(0, 25) : i % 3 == 1 ? new float2(10, 0) : new float2(10, -10);
                        previous = p - delta * .5f; current = p + delta * .5f;
                    }
                    Previous[i] = previous; Current[i] = current; Radii[i] = radius;
                    Entries[i] = new GridEntry { Position = p, Radius = radius + math.distance(previous, current) * .5f, Owner = i, Data = i % 7 };
                }
                for (int q = 0; q < queries; q++)
                {
                    float2 center = q % 2 == 0 ? Entries[q % count].Position : random.NextFloat2(Origin, Origin + Size);
                    float radius = name == "sparse-huge" ? (q % 2 == 0 ? 150 : 300) : name.StartsWith("belt", StringComparison.Ordinal) ? .42f : q % 3 == 0 ? .35f : q % 3 == 1 ? 2f : 8f;
                    var query = new Probe { Center = center, Radius = radius };
                    if (name == "high-speed")
                    {
                        float2 delta = q % 4 == 0 ? float2.zero : q % 4 == 1 ? new float2(0, 25) : q % 4 == 2 ? new float2(25, 0) : new float2(20, -20);
                        query.From = center + delta * .5f; query.To = center - delta * .5f;
                        query.Swept = true; query.Radius = math.length(delta) * .5f + .101f;
                    }
                    Queries[q] = query;
                }
            }
            public void Dispose() { Entries.Dispose(); Previous.Dispose(); Current.Dispose(); Radii.Dispose(); Queries.Dispose(); }
        }

        struct HitVisitor : IGridVisitor
        {
            [ReadOnly] public NativeArray<float2> Previous, Current;
            [ReadOnly] public NativeArray<float> Radii;
            public Probe Query;
            public long Hits, Checksum;
            public bool Visit(in GridEntry e)
            {
                if (!Query.Swept || CombatSweep.Circles(Query.From, Query.To, .1f, Previous[e.Owner], Current[e.Owner], Radii[e.Owner], out _))
                { Hits++; Checksum = unchecked(Checksum + (e.Owner + 1L) * 2654435761L); }
                return true;
            }
        }
        struct ExactVisitor : IGridVisitor
        {
            public NativeArray<int> Seen;
            public HitVisitor Hit;
            public int Duplicates;
            public bool Visit(in GridEntry e)
            {
                long old = Hit.Hits; Hit.Visit(e);
                if (Hit.Hits != old) { if (Seen[e.Owner] != 0) Duplicates++; Seen[e.Owner] = 1; }
                return true;
            }
        }
        static HitVisitor Visitor(Data data, Probe query) => new HitVisitor { Query = query, Previous = data.Previous, Current = data.Current, Radii = data.Radii };
        static bool Expected(Data data, Probe q, int row)
        {
            var e = data.Entries[row];
            if (q.Swept) return CombatSweep.Circles(q.From, q.To, .1f, data.Previous[row], data.Current[row], data.Radii[row], out _);
            float r = q.Radius + e.Radius; return math.distancesq(q.Center, e.Position) < r * r;
        }
        static void Fill(SpatialGrid grid, Data data)
        {
            for (int i = 0; i < data.Entries.Length; i++) grid.Staging.Set(i, data.Entries[i]);
            grid.StagingCount.Set(0, data.Entries.Length);
        }
        static void Fill(BoundedQuadtreeReference tree, Data data)
        { for (int i = 0; i < data.Entries.Length; i++) tree.Staging.Set(i, data.Entries[i]); }

        [TestCase("uniform")] [TestCase("clustered")] [TestCase("sparse-huge")]
        [TestCase("mixed")] [TestCase("dense")] [TestCase("high-speed")]
        [TestCase("belt-spread")] [TestCase("belt-clustered")]
        public void AllBackendsMatchEveryBruteForceHitWithoutDuplicates(string distribution)
        {
            foreach (uint seed in new uint[] { 721, 1234, 9173 })
            using (var data = new Data(distribution, 192, 64, seed))
            using (var seen = new NativeArray<int>(data.Entries.Length, Allocator.TempJob))
            for (int mode = 0; mode < 4; mode++)
            using (var backend = new SpatialBackendFixture((SpatialTestBackend)mode, data.Dimensions, data.Cell, data.Origin, data.Entries.Length))
            {
                for (int i = 0; i < data.Entries.Length; i++) backend.Set(i, data.Entries[i]);
                backend.Build(data.Entries.Length);
                Assert.AreEqual(data.Entries.Length, backend.EntryCount); Assert.AreEqual(0, backend.Dropped);
                for (int q = 0; q < data.Queries.Length; q++)
                {
                    for (int i = 0; i < seen.Length; i++) seen.Set(i, 0);
                    var probe = data.Queries[q]; var visitor = new ExactVisitor { Seen = seen, Hit = Visitor(data, probe) };
                    backend.Query(probe.Center, probe.Radius, ref visitor);
                    Assert.AreEqual(0, visitor.Duplicates);
                    for (int i = 0; i < seen.Length; i++)
                        if ((seen[i] != 0) != Expected(data, probe, i))
                            Assert.Fail("seed=" + seed + "; query=" + q + "; backend=" + mode + "; row=" + i);
                }
            }
        }

        struct EarliestVisitor : IGridVisitor
        {
            [ReadOnly] public NativeArray<float2> Previous, Current;
            [ReadOnly] public NativeArray<float> Radii;
            public float2 From, To;
            public int Target;
            public float Fraction;
            public bool Visit(in GridEntry e)
            {
                if (CombatSweep.Circles(From, To, .1f, Previous[e.Owner], Current[e.Owner], Radii[e.Owner], out float t)
                    && (Target < 0 || t < Fraction || t == Fraction && e.Owner < Target))
                { Target = e.Owner; Fraction = t; }
                return true;
            }
        }

        [Test]
        public void MovingTargetEarliestFractionAndStableTieAgreeAcrossBackends()
        {
            using var data = new Data("high-speed", 192, 64, 9173);
            using var grid = new SpatialGrid(data.Dimensions, data.Cell, data.Entries.Length) { Origin = data.Origin };
            using var tree = new BoundedQuadtreeReference(data.Origin, data.Size, data.Entries.Length);
            // An exact simultaneous crossing pair exercises the stable-ID tie independently of traversal.
            data.Previous.Set(0, new float2(0, -10)); data.Current.Set(0, new float2(0, 10)); data.Radii.Set(0, .3f);
            data.Previous.Set(1, data.Previous[0]); data.Current.Set(1, data.Current[0]); data.Radii.Set(1, .3f);
            data.Entries.Set(0, new GridEntry { Position = float2.zero, Radius = 10.3f, Owner = 0 });
            data.Entries.Set(1, new GridEntry { Position = float2.zero, Radius = 10.3f, Owner = 1 });
            Fill(grid, data); grid.ScheduleBuild(default).Complete(); Fill(tree, data); tree.Build(data.Entries.Length);
            for (int q = 0; q < data.Queries.Length; q++)
            {
                var query = data.Queries[q];
                if (q == 0) query = new Probe { From = float2.zero, To = float2.zero, Center = float2.zero, Radius = .101f, Swept = true };
                var expected = new EarliestVisitor { Previous = data.Previous, Current = data.Current, Radii = data.Radii, From = query.From, To = query.To, Target = -1, Fraction = 2 };
                for (int i = 0; i < data.Entries.Length; i++) expected.Visit(data.Entries[i]);
                for (int mode = 0; mode < 3; mode++)
                {
                    var actual = new EarliestVisitor { Previous = data.Previous, Current = data.Current, Radii = data.Radii, From = query.From, To = query.To, Target = -1, Fraction = 2 };
                    if (mode == 0) grid.AsReader().Query(query.Center, query.Radius, ref actual);
                    else if (mode == 1) grid.AsReader().QueryPruned(query.Center, query.Radius, ref actual);
                    else tree.AsReader().Query(query.Center, query.Radius, ref actual);
                    Assert.AreEqual(expected.Target, actual.Target); Assert.AreEqual(expected.Fraction, actual.Fraction);
                }
            }
            // Reverse staging order for just the tied pair: the lower stable ID must still win.
            grid.Staging.Set(0, data.Entries[1]); grid.Staging.Set(1, data.Entries[0]); grid.StagingCount.Set(0, 2);
            grid.ScheduleBuild(default).Complete(); tree.Staging.Set(0, data.Entries[1]); tree.Staging.Set(1, data.Entries[0]); tree.Build(2);
            for (int mode = 0; mode < 3; mode++)
            {
                var tied = new EarliestVisitor { Previous = data.Previous, Current = data.Current, Radii = data.Radii, Target = -1, Fraction = 2 };
                if (mode == 0) grid.AsReader().Query(float2.zero, .101f, ref tied);
                else if (mode == 1) grid.AsReader().QueryPruned(float2.zero, .101f, ref tied);
                else tree.AsReader().Query(float2.zero, .101f, ref tied);
                Assert.AreEqual(0, tied.Target); Assert.AreEqual(.48f, tied.Fraction, .00001f);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct GridProbeJob : IJob
        {
            public GridReader Reader;
            [ReadOnly] public NativeArray<Probe> Queries;
            [ReadOnly] public NativeArray<float2> Previous, Current;
            [ReadOnly] public NativeArray<float> Radii;
            public NativeArray<long> Results;
            public NativeArray<GridQueryStats> Stats;
            public bool Pruned, Measured;
            public void Execute()
            {
                int burst = 1; MarkManaged(ref burst);
                long hits = 0, checksum = 0; var stats = new GridQueryStats();
                for (int i = 0; i < Queries.Length; i++)
                {
                    var q = Queries[i]; var visitor = new HitVisitor { Query = q, Previous = Previous, Current = Current, Radii = Radii };
                    if (Measured) Reader.QueryMeasured(q.Center, q.Radius, ref visitor, ref stats, Pruned);
                    else if (Pruned) Reader.QueryPruned(q.Center, q.Radius, ref visitor);
                    else Reader.Query(q.Center, q.Radius, ref visitor);
                    hits += visitor.Hits; checksum = unchecked(checksum + visitor.Checksum);
                }
                Results[0] = hits; Results[1] = checksum; Results[2] = burst; Stats[0] = stats;
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        struct TreeBuildJob : IJob
        {
            public BoundedQuadtreeReference.Builder Builder;
            public int Count;
            public NativeArray<long> Results;
            public void Execute() { int burst = 1; MarkManaged(ref burst); Builder.Build(Count); Results[3] = burst; }
        }
        [BurstCompile(CompileSynchronously = true)]
        struct TreeProbeJob : IJob
        {
            public BoundedQuadtreeReference.Reader Reader;
            [ReadOnly] public NativeArray<Probe> Queries;
            [ReadOnly] public NativeArray<float2> Previous, Current;
            [ReadOnly] public NativeArray<float> Radii;
            public NativeArray<long> Results;
            public NativeArray<GridQueryStats> Stats;
            public bool Measured;
            public void Execute()
            {
                int burst = 1; MarkManaged(ref burst);
                long hits = 0, checksum = 0; var stats = new GridQueryStats();
                for (int i = 0; i < Queries.Length; i++)
                {
                    var q = Queries[i]; var visitor = new HitVisitor { Query = q, Previous = Previous, Current = Current, Radii = Radii };
                    if (Measured) Reader.QueryMeasured(q.Center, q.Radius, ref visitor, ref stats);
                    else Reader.Query(q.Center, q.Radius, ref visitor);
                    hits += visitor.Hits; checksum = unchecked(checksum + visitor.Checksum);
                }
                Results[0] = hits; Results[1] = checksum; Results[2] = burst; Stats[0] = stats;
            }
        }
#if !SPF_DOTNET_HARNESS
        [BurstDiscard]
#endif
        static void MarkManaged(ref int burst) { burst = 0; }

        sealed class Runner : IDisposable
        {
            public readonly SpatialGrid Grid;
            public readonly BoundedQuadtreeReference Tree;
            public readonly NativeArray<long> Results;
            public readonly NativeArray<GridQueryStats> Counters;
            readonly Data m_Data;
            readonly int m_Mode;
            public Runner(Data data, int mode, int bucketSize = 16, int maxDepth = 12)
            {
                m_Data = data; m_Mode = mode;
                Results = new NativeArray<long>(4, Allocator.Persistent); Counters = new NativeArray<GridQueryStats>(1, Allocator.Persistent);
                if (mode < 3)
                { Grid = new SpatialGrid(data.Dimensions, data.Cell, data.Entries.Length, mode == 2 ? .75f : 0) { Origin = data.Origin }; Fill(Grid, data); }
                else { Tree = new BoundedQuadtreeReference(data.Origin, data.Size, data.Entries.Length, bucketSize, maxDepth); Fill(Tree, data); }
            }
            JobHandle Build()
            {
                if (Grid != null) { Grid.StagingCount.Set(0, m_Data.Entries.Length); return Grid.ScheduleBuild(default); }
                return new TreeBuildJob { Builder = Tree.AsBuilder(), Count = m_Data.Entries.Length, Results = Results }.Schedule();
            }
            JobHandle Query(JobHandle dependency, bool measured)
            {
                if (Grid != null) return new GridProbeJob { Reader = Grid.AsReader(), Queries = m_Data.Queries, Previous = m_Data.Previous, Current = m_Data.Current,
                    Radii = m_Data.Radii, Results = Results, Stats = Counters, Pruned = m_Mode == 1, Measured = measured }.Schedule(dependency);
                return new TreeProbeJob { Reader = Tree.AsReader(), Queries = m_Data.Queries, Previous = m_Data.Previous, Current = m_Data.Current,
                    Radii = m_Data.Radii, Results = Results, Stats = Counters, Measured = measured }.Schedule(dependency);
            }
            public void Total() { var build = Build(); Query(build, false).Complete(); }
            public void BuildOnly() { Build().Complete(); }
            public void QueryOnly() { Query(default, false).Complete(); }
            public void MeasureCounters() { Query(default, true).Complete(); }
            public long Bytes => Tree != null ? Tree.AllocatedBytes : 32L * m_Data.Entries.Length + 4L * (m_Data.Dimensions.x * m_Data.Dimensions.y + 1) + 4 + 20 +
                (m_Mode == 2 ? 16L * m_Data.Entries.Length + 4L * (Grid.LargeDimensions.x * Grid.LargeDimensions.y + 1) : 24);
            public void Dispose() { Grid?.Dispose(); Tree?.Dispose(); Results.Dispose(); Counters.Dispose(); }
        }

        static double Time(Action action) { long start = Stopwatch.GetTimestamp(); action(); return (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency; }
        static string Describe(double[] samples)
        {
            Array.Sort(samples); return string.Format(CultureInfo.InvariantCulture, "p50={0:F4};p95={1:F4};worst={2:F4}", samples[samples.Length / 2], samples[(int)(samples.Length * .95)], samples[samples.Length - 1]);
        }

        [TestCase("uniform")] [TestCase("clustered")] [TestCase("sparse-huge")]
        [TestCase("mixed")] [TestCase("dense")] [TestCase("high-speed")]
        [Category("Performance")]
        public void QuadtreeBucketAndDepthSensitivityReport(string distribution)
        {
            const int count = 512, queries = 512, samples = 16;
            using var data = new Data(distribution, count, queries, 1234);
            var runners = new Runner[9]; var actions = new Action[9]; var times = new double[9][];
            int[] buckets = { 8, 16, 32 }, depths = { 8, 12, 16 };
            try
            {
                for (int i = 0; i < 9; i++)
                {
                    runners[i] = new Runner(data, 3, buckets[i / 3], depths[i % 3]);
                    actions[i] = runners[i].Total; times[i] = new double[samples];
                }
                for (int i = 0; i < 4; i++) for (int k = 0; k < 9; k++) actions[(i + k) % 9]();
                for (int i = 0; i < samples; i++) for (int k = 0; k < 9; k++)
                { int variant = (i + k) % 9; times[variant][i] = Time(actions[variant]); }
                long expected = 0, checksum = 0;
                for (int q = 0; q < queries; q++) for (int i = 0; i < count; i++)
                    if (Expected(data, data.Queries[q], i)) { expected++; checksum = unchecked(checksum + (i + 1L) * 2654435761L); }
#if SPF_DOTNET_HARNESS
                const string runtime = "dotnet-stubs";
                string dir = Path.Combine(Path.GetTempPath(), "spf-artifacts");
#else
                const string runtime = "unity-native-editmode";
                string dir = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".", "Artifacts");
#endif
                var report = new StringBuilder("Quadtree sensitivity; runtime=" + runtime + "; distribution=" + distribution + "; seed=1234; entries=512; queries=512; warmup=4; samples=16\n");
                report.AppendLine("Same arrays; rotated configuration order; full scheduled build+query+completion; instrumentation separate. Compare configurations only within this fixture, not against the different-size primary grid matrix.");
                for (int i = 0; i < 9; i++)
                {
                    var r = runners[i]; r.MeasureCounters(); var c = r.Counters[0];
                    Assert.AreEqual(expected, r.Results[0]); Assert.AreEqual(checksum, r.Results[1]); Assert.AreEqual(0, r.Tree.RejectedCount);
#if !SPF_DOTNET_HARNESS
                    if (BurstCompiler.IsEnabled) { Assert.AreEqual(1, r.Results[2]); Assert.AreEqual(1, r.Results[3]); }
#endif
                    report.AppendLine("bucket=" + buckets[i / 3] + "; maxDepth=" + depths[i % 3] + "; total_ms=" + Describe(times[i])
                        + "; nodes=" + r.Tree.NodeCount + "; depth=" + r.Tree.Depth + "; saturated=" + r.Tree.SaturatedSplits
                        + "; raw_entries=" + c.EntriesExamined + "; visits=" + c.VisitorCalls + "; hits=" + r.Results[0]
                        + "; bytes=" + r.Bytes + "; queryBurst=" + r.Results[2] + "; buildBurst=" + r.Results[3]);
                }
                report.AppendLine("Report-only; no runtime tree adoption and no native/mobile claim from stubs. Primary matrix parameters were fixed in advance, not selected from this tuning pass.");
                Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "perf-spatial-tuning-" + runtime + "-" + distribution + ".txt"), report.ToString());
                TestContext.WriteLine(report.ToString());
            }
            finally { foreach (var runner in runners) runner?.Dispose(); }
        }

        [TestCase("uniform", 1024, 4096)] [TestCase("uniform", 4096, 4096)]
        [TestCase("clustered", 1024, 4096)] [TestCase("sparse-huge", 320, 1280)]
        [TestCase("mixed", 1024, 4096)] [TestCase("dense", 1024, 128)]
        [TestCase("high-speed", 1024, 4096)]
        [TestCase("belt-spread", 32, 128)] [TestCase("belt-clustered", 32, 128)]
        [TestCase("belt-spread", 64, 256)] [TestCase("belt-clustered", 64, 256)]
        [TestCase("belt-spread", 128, 512)] [TestCase("belt-clustered", 128, 512)]
        [Category("Performance")]
        public void PairedBuildAndQueryReport(string distribution, int entries, int queries)
        {
            const int warmup = 6, samples = 24;
            using var data = new Data(distribution, entries, queries, 721);
            var runners = new Runner[4]; var totals = new double[4][]; var builds = new double[4][]; var reads = new double[4][];
            string[] names = { "grid", "grid-pruned-opt-in", "grid-two-layer", "bounded-quadtree" };
            var totalActions = new Action[4]; var buildActions = new Action[4]; var queryActions = new Action[4];
            try
            {
                for (int mode = 0; mode < 4; mode++)
                {
                    runners[mode] = new Runner(data, mode); totals[mode] = new double[samples]; builds[mode] = new double[samples]; reads[mode] = new double[samples];
                    totalActions[mode] = runners[mode].Total; buildActions[mode] = runners[mode].BuildOnly; queryActions[mode] = runners[mode].QueryOnly;
                }
                // Every backend sees the identical immutable input, including previous/current target motion.
                for (int i = 0; i < warmup; i++) for (int k = 0; k < 4; k++) totalActions[(i + k) % 4]();
                for (int i = 0; i < samples; i++) for (int k = 0; k < 4; k++)
                {
                    int mode = (i + k) % 4;
                    totals[mode][i] = Time(totalActions[mode]); builds[mode][i] = Time(buildActions[mode]); reads[mode][i] = Time(queryActions[mode]);
                }
                long expectedHits = 0, expectedChecksum = 0;
                for (int q = 0; q < queries; q++) for (int i = 0; i < entries; i++)
                    if (Expected(data, data.Queries[q], i)) { expectedHits++; expectedChecksum = unchecked(expectedChecksum + (i + 1L) * 2654435761L); }
#if SPF_DOTNET_HARNESS
                const string runtime = "dotnet-stubs";
                string dir = Path.Combine(Path.GetTempPath(), "spf-artifacts");
#else
                const string runtime = "unity-native-editmode";
                string dir = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".", "Artifacts");
#endif
                var report = new StringBuilder();
                report.AppendLine("Spatial broadphase paired benchmark; runtime=" + runtime + "; distribution=" + distribution + "; seed=721; entries=" + entries + "; queries=" + queries);
                report.AppendLine("OS=" + Environment.OSVersion + "; logical_processors=" + Environment.ProcessorCount + "; 64bit=" + Environment.Is64BitProcess);
#if !SPF_DOTNET_HARNESS
                report.AppendLine("Unity=" + UnityEngine.Application.unityVersion + "; CPU=" + UnityEngine.SystemInfo.processorType + "; BurstEnabled=" + BurstCompiler.IsEnabled);
#endif
                report.AppendLine("Identical staged arrays and queries; full rebuild every sample; rotated backend order; warmup=" + warmup + "; samples=" + samples + "; cell=" + data.Cell + "; tree bucket=16/depth=12.");
                report.AppendLine("Total includes two scheduled jobs and completion; build/query separately include their own schedule+completion, so their sum is not the pipelined total. Staging data generation excluded equally; tree build includes partition/scratch work. Timing is uninstrumented; counters are a separate pass.");
                for (int mode = 0; mode < 4; mode++)
                {
                    var runner = runners[mode]; runner.MeasureCounters(); var stats = runner.Counters[0];
#if !SPF_DOTNET_HARNESS
                    if (BurstCompiler.IsEnabled)
                    {
                        Assert.AreEqual(1, runner.Results[2], names[mode] + " query did not execute with enabled Burst");
                        if (runner.Tree != null) Assert.AreEqual(1, runner.Results[3], "tree build did not execute with enabled Burst");
                    }
#endif
                    Assert.AreEqual(expectedHits, runner.Results[0], names[mode] + " brute-force total");
                    Assert.AreEqual(expectedChecksum, runner.Results[1], names[mode] + " brute-force checksum");
                    if (runner.Grid != null) Assert.AreEqual(0, runner.Grid.DroppedLastBuild);
                    else Assert.AreEqual(0, runner.Tree.RejectedCount);
                    Action allocationWindow = () => { for (int i = 0; i < 8; i++) runner.Total(); };
                    using var probe = new ManagedAllocationProbe(); var before = probe.Calibrate(); var allocation = probe.Measure(allocationWindow); var after = probe.Calibrate();
                    report.AppendLine(names[mode] + "; total_ms=" + Describe(totals[mode]) + "; build_ms=" + Describe(builds[mode]) + "; query_ms=" + Describe(reads[mode]));
                    report.AppendLine("  cells_or_nodes=" + stats.CellsVisited + "; pruned=" + stats.CellsPruned + "; entries_examined=" + stats.EntriesExamined + "; visitor_calls=" + stats.VisitorCalls + "; exact_hits=" + runner.Results[0] + "; bytes=" + runner.Bytes + "; queryBurst=" + runner.Results[2] + "; treeBuildBurst=" + (runner.Tree != null ? runner.Results[3].ToString() : "not-instrumented"));
                    if (runner.Tree != null) report.AppendLine("  tree nodes=" + runner.Tree.NodeCount + "; depth=" + runner.Tree.Depth + "; saturated_splits=" + runner.Tree.SaturatedSplits);
                    report.AppendLine("  warmed allocation=" + allocation.Value + " current-thread " + allocation.Metric + "; gen0=" + allocation.Collections + "; calibration before=" + before.RetainedArrays.Value + "/" + before.Empty.Value + "; after=" + after.RetainedArrays.Value + "/" + after.Empty.Value);
                    Assert.AreEqual(0, allocation.Value, names[mode] + " warmed benchmark must remain allocation-free");
                }
                report.AppendLine("No timing gates. A zero Burst sentinel means managed execution, even in Unity; do not call it native Burst performance. Does not measure gameplay decisions, render/upload, incremental Snake updates, all-thread/native allocations, or Android/iOS thermal/battery behavior. Dense true overlap remains quadratic. The quadtree and pruning are not enabled in existing gameplay.");
                Directory.CreateDirectory(dir); string path = Path.Combine(dir, "perf-spatial-" + runtime + "-" + distribution + "-" + entries + ".txt");
                File.WriteAllText(path, report.ToString()); TestContext.WriteLine(report.ToString());
            }
            finally { foreach (var runner in runners) runner?.Dispose(); }
        }
    }
}
