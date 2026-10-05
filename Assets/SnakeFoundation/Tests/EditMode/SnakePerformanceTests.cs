using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using SPF.L1.Spatial;
using SPF.Runtime.Scheduling;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Tests
{
    /// <summary>
    /// Tick benchmark with the production population (150 AI per region, default food). In Unity it runs
    /// Burst-compiled jobs on real worker threads; the report goes to Artifacts/perf-editmode.txt and the
    /// test output. It asserts only sanity, not timings: timings depend on the machine.
    /// </summary>
    [Category("Performance")]
    public class SnakePerformanceTests
    {
#if !SPF_DOTNET_HARNESS
        const int WarmupTicks = 150, MeasuredTicks = 600, SerialTicks = 300;
#else
        const int WarmupTicks = 20, MeasuredTicks = 40, SerialTicks = 20;
#endif

        /// <param name="bodyCell">Body grid cell size: compares rebuild cost against query cost.</param>
        /// <param name="largeRadius">Large-radius body layer threshold (0 = single layer).</param>
        [TestCase(4f, 0f)]
        [TestCase(8f, 0f)]
        [TestCase(8f, 1.6f)]
        public void TickBenchmark(float bodyCell, float largeRadius)
        {
            using var world = new SnakeTestWorld(aiPerRegion: 150, foodPerChunk: 150, propsPerChunk: 1, seed: 1234,
                tweak: c => { c.Capacity.BodyGridCellSize = bodyCell; c.Capacity.LargeBodyRadius = largeRadius; });
            world.StartPlayer();
            world.Step(WarmupTicks);

            // Pipelined (normal) operation: whole tick wall time, main-thread schedule and sync wait.
            var samples = new double[MeasuredTicks];
            var stats = world.Session.Pipeline.Stats;
            double scheduleSum = 0, waitSum = 0;
            for (int i = 0; i < MeasuredTicks; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                world.Session.Step();
                samples[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                scheduleSum += stats.ScheduleMs;
                waitSum += stats.SyncWaitMs;
            }

            // Serial profiling: per-system cost (each system's jobs complete before the next is scheduled).
            var pipeline = world.Session.Pipeline;
            pipeline.SerialProfiling = true;
            stats.ClearExecuteTimes();
            world.Step(SerialTicks);
            pipeline.SerialProfiling = false;

            var report = $"body grid cell: {bodyCell} m, large layer above radius {largeRadius} (0 = off)\n" + BuildReport(world, samples, scheduleSum / MeasuredTicks, waitSum / MeasuredTicks, pipeline);
            TestContext.WriteLine(report);
            Console.WriteLine(report);
            WriteArtifact($"perf-editmode-body{bodyCell:0}m-large{largeRadius * 10:0}.txt", report);

            Array.Sort(samples);
            Assert.Greater(samples[samples.Length / 2], 0.0);
            Assert.Greater(world.World.Table(SnakeKeys.Snake).Count, 100, "population should be alive");
        }

        struct CountVisitor : IGridVisitor
        {
            public int Hits;
            public bool Visit(in GridEntry entry) { Hits++; return true; }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct ItemQueryJob : IJob
        {
            public CellListReader Items;
            [ReadOnly] public NativeArray<float2> Points;
            public NativeArray<long> Hits;
            public float Radius;

            public void Execute()
            {
                long hits = 0;
                for (int i = 0; i < Points.Length; i++)
                {
                    var visitor = new CountVisitor();
                    Items.Query(Points[i], Radius, ref visitor);
                    hits += visitor.Hits;
                }
                Hits[0] = hits;
            }
        }

        /// <summary>
        /// Item-grid query throughput where it hurts: big snakes die near the player and drop dense food
        /// trails, so some cells hold dozens of items. Queries are centred on food (dense areas weighted by
        /// their item count), at the eat radius and the AI food-search radius. Burst job, single thread.
        /// Writes Artifacts/perf-itemgrid.txt.
        /// </summary>
        [Test]
        public void ItemGridQueryBenchmarkWithDeathDrops()
        {
            using var world = new SnakeTestWorld(aiPerRegion: 150, foodPerChunk: 150, propsPerChunk: 1, seed: 4321);
            world.StartPlayer();
            world.Step(30);

            // Stress case: twelve heavy snakes whose bodies all cross one point die at once. Each death
            // drops food every ~1.5 radii along the body, so the cells around that point end up holding
            // dozens of items (a single death trail only puts 2-3 items in an 8 m cell).
            float2 center = world.World.Column(SnakeKeys.Head)[world.PlayerRow] + new float2(80f, 0f);
            var deaths = world.World.Resource(SnakeKeys.Deaths);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * (2f * math.PI / 12f);
                float2 dir = new float2(math.cos(angle), math.sin(angle));
                var victim = world.SpawnAI(center + dir * 3f, dir, 400f);   // body runs back through the centre
                deaths.TryAdd(new DeathEvent { Victim = victim, Cause = DeathCause.Wall });
            }
            world.Step(3);

            var report = new StringBuilder();
            report.AppendLine("=== item grid queries (Burst, single thread) ===");
            MeasureItemQueries(world, center, 16f, "12-snake death crossing (realistic dense)", report);

            // Synthetic stress: 2000 food packed into 40 x 40 m (about 80 per 8 m cell).
            float2 cluster = center + new float2(0f, 120f);
            var w = world.World;
            var population = w.Resource(SnakeKeys.Populations).Food[world.Game.ActiveRegion];
            var settings = world.Runtime.Settings;
            var random = new Unity.Mathematics.Random(3);
            for (int i = 0; i < 2000; i++)
                SnakeSpawner.SpawnFood(w, population, settings, cluster + random.NextFloat2(-20f, 20f), 1f, 0xFFFFFFFFu);
            world.Step(1);
            MeasureItemQueries(world, cluster, 20f, "2000 food in 40 x 40 m (synthetic stress)", report);

            TestContext.WriteLine(report.ToString());
            Console.WriteLine(report.ToString());
            WriteArtifact("perf-itemgrid.txt", report.ToString());
        }

        static void MeasureItemQueries(SnakeTestWorld world, float2 hotspot, float hotspotHalfSize, string label, StringBuilder report)
        {
            var w = world.World;
            int foodCount = w.Table(SnakeKeys.Food).Count;
            var positions = w.Column(SnakeKeys.FoodPosition);
            int queries = math.min(foodCount, 8192);
            var points = new NativeArray<float2>(queries, Allocator.TempJob);
            var hits = new NativeArray<long>(1, Allocator.TempJob);
            report.AppendLine($"-- {label}: food {foodCount}, {queries} queries (half in the hot spot, half on food)");
            try
            {
                var random = new Unity.Mathematics.Random(11);
                int stride = math.max(foodCount / queries, 1);
                for (int i = 0; i < queries; i++)
                    points[i] = (i & 1) == 0
                        ? hotspot + random.NextFloat2(-hotspotHalfSize, hotspotHalfSize)
                        : positions[(i * stride) % foodCount] + new float2(0.37f, -0.21f);
                var items = w.Resource(SnakeKeys.ItemGrid).AsReader();
                foreach (float radius in new[] { 3f, 20f })
                {
                    var job = new ItemQueryJob { Items = items, Points = points, Hits = hits, Radius = radius };
                    job.Schedule().Complete();   // warm up (Burst compile, caches)
                    const int Repeats = 10;
                    long t0 = Stopwatch.GetTimestamp();
                    for (int r = 0; r < Repeats; r++) job.Schedule().Complete();
                    double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    double nsPerQuery = ms * 1e6 / (Repeats * queries);
                    report.AppendLine($"   radius {radius,4:0} m: {nsPerQuery,8:F1} ns/query, {hits[0] / (double)queries,7:F1} entries visited/query");
                    Assert.Greater(hits[0], 0);
                }
            }
            finally
            {
                points.Dispose();
                hits.Dispose();
            }
        }

        static string BuildReport(SnakeTestWorld world, double[] samples, double scheduleMs, double waitMs, TickPipeline pipeline)
        {
            var sorted = (double[])samples.Clone();
            Array.Sort(sorted);
            double mean = 0;
            foreach (var s in sorted) mean += s;
            mean /= sorted.Length;

            var w = world.World;
            var sb = new StringBuilder();
            sb.AppendLine("=== SPF snake tick benchmark ===");
            sb.AppendLine($"platform: {Environment.OSVersion}, {Environment.ProcessorCount} logical cores, 64-bit: {Environment.Is64BitProcess}");
            sb.AppendLine($"snakes: {w.Table(SnakeKeys.Snake).Count}  food: {w.Table(SnakeKeys.Food).Count}  props: {w.Table(SnakeKeys.Prop).Count}  projectiles: {w.Table(SnakeKeys.Projectile).Count}  body points used: {w.Resource(SnakeKeys.Bodies).UsedPoints}");
            sb.AppendLine($"pipelined tick ms  mean {mean:F3}  p50 {sorted[sorted.Length / 2]:F3}  p95 {sorted[(int)(sorted.Length * 0.95)]:F3}  max {sorted[sorted.Length - 1]:F3}");
            sb.AppendLine($"main-thread schedule ms {scheduleMs:F3}  sync wait ms {waitMs:F3}");
            for (int i = 0; i < pipeline.SystemCount; i++)
                if (pipeline.GetSystem(i) is SnakeFoundation.Systems.ItemGridSystem items)
                    sb.AppendLine($"item grid: {items.FullRebuilds} full rebuilds, {items.IncrementalUpdates} incremental updates ({items.DirtyRows} rows) in {pipeline.Stats.TickCount} ticks");
            sb.AppendLine("per-system ms (serial profiling, includes scheduling):");
            var stats = pipeline.Stats;
            var order = new int[stats.SystemCount];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => stats.SystemExecuteMs(b).CompareTo(stats.SystemExecuteMs(a)));
            double total = 0;
            foreach (int i in order)
            {
                total += stats.SystemExecuteMs(i);
                sb.AppendLine($"  {stats.SystemName(i),-28} {stats.SystemExecuteMs(i),8:F3}");
            }
            sb.AppendLine($"  {"TOTAL (serial)",-28} {total,8:F3}");
            return sb.ToString();
        }

        static void WriteArtifact(string fileName, string report)
        {
            try
            {
#if !SPF_DOTNET_HARNESS
                string dir = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".", "Artifacts");
#else
                string dir = Path.Combine(Path.GetTempPath(), "spf-artifacts");
#endif
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, fileName), report);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
