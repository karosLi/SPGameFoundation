using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Testing;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    /// <summary>Full fixed-tick diagnostic in both .NET stubs and native Unity EditMode. Timings and
    /// allocations are reports, not device budgets; correctness/determinism remain hard assertions.</summary>
    [NonParallelizable]
    public class BwBeltWorkloadTests
    {
        const int WarmupTicks = 90, TimedTicks = 120, AllocationTicks = 120;

        struct Totals
        {
            public long Separation, Hits;
            public int SeparationPeak, HitPeak, GridDropped, FeedbackPeak;
            public void Observe(BwTestWorld world)
            {
                var state = world.World.Resource(BwBeltKeys.State);
                Separation += state.SeparationCandidates; Hits += state.HitCandidates;
                SeparationPeak = Math.Max(SeparationPeak, state.SeparationCandidates); HitPeak = Math.Max(HitPeak, state.HitCandidates);
                GridDropped += state.LastGridDropped; FeedbackPeak = Math.Max(FeedbackPeak, world.World.Resource(BwKeys.Feedback).Count);
            }
        }

        static void Populate(BwTestWorld t, int count, bool clustered)
        {
            t.World.ClearLevel();
            t.World.Resource(BwKeys.Feedback).OnReset();
            t.Game.Flow = BwFlow.Fighting; t.Game.Wave = 1; t.Game.FlowTimer = 0; t.Game.Score = t.Game.Kos = 0; t.Game.Input = default;
            int columns = count / 4;
            for (int i = 0; i < count; i++)
            {
                float2 ground = i == 0 ? float2.zero : clustered
                    ? new float2((i % 16 - 7.5f) * .045f, (i / 16 - 3.5f) * .045f)
                    : new float2(-8f + i % columns * (16f / (columns - 1)), -2.1f + i / columns * 1.4f);
                BwSpawner.Spawn(t.World, (byte)(i == 0 ? 0 : 1), ground, ground.x > 0 ? -1 : 1, (byte)(i == 0 ? 0 : 1 + i % 3));
                var f = t.Info(i); f.Hp = f.MaxHp = 1000000; f.Cooldown = (i % 7) * .045f;
                t.World.Column(BwKeys.Info).Set(i, f);
            }
            // Exercise reject-newest capacity deliberately once, outside the timing/allocation windows.
            BwSpawner.Spawn(t.World, 1, float2.zero, -1, 1);
            Assert.AreEqual(count, t.Count);
        }

        static void Tick(BwTestWorld t, int tick)
        {
            // One output-only feedback drain per tick models a reader without rendering. Count both
            // drained and pending overflow in the report; never let a missing view hide queue pressure.
            t.World.Resource(BwKeys.Feedback).Clear();
            uint action = tick % 120 == 0 ? 2u : tick % 90 == 30 ? 4u : 0u;
            t.Game.Input = new InputFrame { Held = 1, Pressed = action, Move = new float2(tick % 100 < 50 ? .6f : -.6f, tick % 80 < 40 ? .25f : -.25f) };
            t.Step(); // completes the real session pipeline, including the two scheduled grid builds
        }
        static void Run(BwTestWorld t, int start, int ticks) { for (int i = 0; i < ticks; i++) Tick(t, start + i); }
        static ulong Hash(byte[] bytes)
        {
            ulong value = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++) value = unchecked((value ^ bytes[i]) * 1099511628211UL);
            return value;
        }

        [TestCase(32, false)] [TestCase(32, true)]
        [TestCase(64, false)] [TestCase(64, true)]
        [TestCase(128, false)] [TestCase(128, true)]
        [Category("Performance")]
        public void FullBeltLogicWorkloadReport(int fighters, bool clustered)
        {
            var config = BwBeltConfig.Default; config.Fighters = config.TargetsPerAttack = fighters;
            config.Waves = config.FirstWaveEnemies = 1;
            using var measured = new BwTestWorld(start: false, belt: config);
            using var replay = new BwTestWorld(start: false, belt: config);
            Populate(measured, fighters, clustered); Populate(replay, fighters, clustered);
            Run(measured, 0, WarmupTicks); Run(replay, 0, WarmupTicks);
            // Restore the authored density after branch/JIT/job warmup. Timed gameplay then evolves
            // naturally, rather than teleporting actors each measured tick to manufacture density.
            Populate(measured, fighters, clustered); Populate(replay, fighters, clustered);
            var times = new double[TimedTicks]; var totals = new Totals(); double sum = 0;
            for (int tick = 0; tick < TimedTicks; tick++)
            {
                long start = Stopwatch.GetTimestamp(); Tick(measured, tick);
                double milliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                times[tick] = milliseconds; sum += milliseconds; totals.Observe(measured);
            }
            var replayTotals = new Totals();
            for (int tick = 0; tick < TimedTicks; tick++) { Tick(replay, tick); replayTotals.Observe(replay); }
            byte[] measuredSnapshot = measured.Session.CaptureSnapshot();
            CollectionAssert.AreEqual(measuredSnapshot, replay.Session.CaptureSnapshot(), "same initial density and inputs reproduce all saved gameplay state");
            Assert.AreEqual(totals.Separation, replayTotals.Separation); Assert.AreEqual(totals.Hits, replayTotals.Hits);
            var state = measured.World.Resource(BwBeltKeys.State); var shared = measured.World.Resource(BwKeys.SharedCombat);
            var feedback = measured.World.Resource(BwKeys.Feedback);
            long feedbackOverflow = feedback.TotalOverflow + feedback.Raw.Overflow;
            int rejectedSpawns = state.RejectedSpawns, rejectedDrops = state.RejectedDrops, rejectedHits = shared.RejectedHits, rejectedScopes = shared.RejectedScopes;
            Assert.AreEqual(fighters, measured.Count); Assert.AreEqual(1, rejectedSpawns, "one deliberate setup overflow, no excess gameplay spawn attempts");
            Assert.AreEqual(0, totals.GridDropped, "the bounded arena must remain covered by the capacity-matched grid");
            Assert.Greater(totals.Separation, 0); Assert.Greater(totals.Hits, 0);

            // Separate allocation window, with calibration on both sides. Delegate construction,
            // formatting, snapshot comparison, timing arrays and file output stay outside the probe.
            Populate(measured, fighters, clustered); Populate(replay, fighters, clustered);
            Action allocationWindow = () => Run(measured, 0, AllocationTicks);
            using var probe = new ManagedAllocationProbe();
            var before = probe.Calibrate(); var allocation = probe.Measure(allocationWindow); var after = probe.Calibrate();
            Run(replay, 0, AllocationTicks);
            CollectionAssert.AreEqual(measured.Session.CaptureSnapshot(), replay.Session.CaptureSnapshot());
            Array.Sort(times);
#if SPF_DOTNET_HARNESS
            const string runtime = "dotnet-stubs";
            string directory = Path.Combine(Path.GetTempPath(), "spf-artifacts");
#else
            const string runtime = "unity-native-editmode";
            string directory = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".", "Artifacts");
#endif
            string layout = clustered ? "clustered" : "spread";
            string report = string.Format(CultureInfo.InvariantCulture,
                "Belt complete-logic workload | runtime={0} | fighters={1} | initial layout={2}\n" +
                "Warmup={3} ticks; density reset after warmup; samples={4} complete evolving ticks; fixed rate=60Hz.\n" +
                "Tick ms: mean={5:F4}, p50={6:F4}, p95={7:F4}, worst={8:F4}.\n" +
                "Separation candidates: total={9}, mean/tick={10:F2}, peak={11}; hit candidates: total={12}, mean/tick={13:F2}, peak={14}.\n" +
                "Grid drops={15}; feedback pending peak={16}/128; feedback overflow={17}; rejected spawn={18} (one intentional setup request); rejected drop={19}; rejected hit={20}; rejected scope={21}.\n" +
                "Exact deterministic snapshot replay=PASS; timed-window state FNV64={22:x16}.\n" +
                "Separate allocation window={23} ticks from the same reset density: {24} current-thread {25}; process-wide gen0 collections={26}. Calibration retained/empty: before={27}/{28}, after={29}/{30}.\n" +
                "Includes main-thread flow, skill/input, all fighter AI/movement, local separation, hit resolution, loot scan, session sync, two completed grid builds and feedback clearing. Grid build is scheduled Burst-capable work; the belt decisions/separation/hit loops currently execute on the main thread. Dense worst-case local overlap remains quadratic.\n" +
                "Timing/allocation are report-only; no arbitrary device threshold. Excludes rendering, GPU upload, full-frame/other-thread/native allocation and target-device thermal/battery behavior. .NET stubs do not prove Burst or Unity performance. Native Unity results require running this same fixture centrally.\n",
                runtime, fighters, layout, WarmupTicks, TimedTicks, sum / TimedTicks, times[TimedTicks / 2], times[(int)(TimedTicks * .95)], times[TimedTicks - 1],
                totals.Separation, totals.Separation / (double)TimedTicks, totals.SeparationPeak, totals.Hits, totals.Hits / (double)TimedTicks, totals.HitPeak,
                totals.GridDropped, totals.FeedbackPeak, feedbackOverflow, rejectedSpawns, rejectedDrops, rejectedHits, rejectedScopes, Hash(measuredSnapshot),
                AllocationTicks, allocation.Value, allocation.Metric, allocation.Collections, before.RetainedArrays.Value, before.Empty.Value, after.RetainedArrays.Value, after.Empty.Value);
            TestContext.WriteLine(report);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "perf-belt-" + runtime + "-" + fighters + "-" + layout + ".txt"), report);
        }
    }
}
