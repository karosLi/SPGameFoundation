using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using SPF.Runtime.Scheduling;

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

        [Test]
        public void TickBenchmark()
        {
            using var world = new SnakeTestWorld(aiPerRegion: 150, foodPerChunk: 150, propsPerChunk: 1, seed: 1234);
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

            var report = BuildReport(world, samples, scheduleSum / MeasuredTicks, waitSum / MeasuredTicks, pipeline);
            TestContext.WriteLine(report);
            Console.WriteLine(report);
            WriteArtifact(report);

            Array.Sort(samples);
            Assert.Greater(samples[samples.Length / 2], 0.0);
            Assert.Greater(world.World.Table(SnakeKeys.Snake).Count, 100, "population should be alive");
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

        static void WriteArtifact(string report)
        {
            try
            {
#if !SPF_DOTNET_HARNESS
                string dir = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".", "Artifacts");
#else
                string dir = Path.Combine(Path.GetTempPath(), "spf-artifacts");
#endif
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "perf-editmode.txt"), report);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
