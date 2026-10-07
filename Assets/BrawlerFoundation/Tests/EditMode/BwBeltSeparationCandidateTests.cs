using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    /// <summary>Candidate only: production keeps its ordered main-thread pass until actual native
    /// measurements support changing it. No new gameplay mode, snapshot field or approximation.</summary>
#if SPF_DOTNET_HARNESS
    [NonParallelizable]
#endif
    public class BwBeltSeparationCandidateTests
    {
        struct Visitor : IGridVisitor
        {
            public int Row, Candidates; public float2 Self, Push; public float Height;
            [ReadOnly] public NativeArray<BwBeltMotion> Motions;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public bool Visit(in GridEntry entry)
            {
                if (entry.Owner == Row) return true;
                Candidates++;
                if (math.abs(Height - Motions[entry.Owner].Height) < .65f)
                    Push += GroundCombatQueries.Separation(Self, entry.Position, BwRules.BodyHalfWidth * 2, Handles[Row].Index, Handles[entry.Owner].Index);
                return true;
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        struct CandidateJob : IJobParallelFor
        {
            public GridReader Grid;
            [ReadOnly] public NativeArray<float2> Ground;
            [ReadOnly] public NativeArray<BwBeltMotion> Motions;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            [ReadOnly] public NativeArray<FighterInfo> Info;
            public NativeArray<float2> Output;
            public NativeArray<int> Counts, Backend;
            public void Execute(int i)
            {
                bool burst = true;
#if !SPF_DOTNET_HARNESS
                Managed(ref burst);
#else
                burst = false;
#endif
                Backend[i] = burst ? 1 : 0;
                var visitor = new Visitor { Row = i, Self = Ground[i], Height = Motions[i].Height, Motions = Motions, Handles = Handles };
                if (Info[i].State != FighterState.KO) Grid.Query(Ground[i], BwRules.BodyHalfWidth, ref visitor);
                float2 push = visitor.Push; float length = math.length(push); if (length > .16f) push *= .16f / length;
                Output[i] = BwBeltRules.ClampGround(Ground[i] + push); Counts[i] = visitor.Candidates;
            }
#if !SPF_DOTNET_HARNESS
            [BurstDiscard] static void Managed(ref bool burst) => burst = false;
#endif
        }
        [TestCase(32, false)] [TestCase(32, true)] [TestCase(128, false)] [TestCase(128, true)]
        [Category("Performance")]
        public void ImmutableGridParallelCandidateMatchesOrderedReferenceAndReportsCompletedCost(int count, bool clustered)
        {
            var config = BwBeltConfig.Default; config.Fighters = config.TargetsPerAttack = count;
            using var t = new BwTestWorld(belt: config); t.World.ClearLevel();
            for (int i = 0; i < count; i++)
            {
                float2 p = clustered ? new float2(i % 8 * .04f, i / 8 * .04f) : new float2(-7 + i % 16 * .9f, -2 + i / 16 * .45f);
                BwSpawner.Spawn(t.World, (byte)(i == 0 ? 0 : 1), p, 1, 1);
                var motion = t.World.Column(BwBeltKeys.Motion)[i]; motion.Height = i % 7 == 0 ? .8f : 0;
                t.World.Column(BwBeltKeys.Motion).Set(i, motion);
                if (i % 13 == 0 && i > 0) { var info = t.Info(i); info.State = FighterState.KO; t.World.Column(BwKeys.Info).Set(i, info); }
            }
            var state = t.World.Resource(BwBeltKeys.State); state.Rebuild(t.World);
            using var sequential = new NativeArray<float2>(count, Allocator.TempJob);
            using var parallel = new NativeArray<float2>(count, Allocator.TempJob);
            using var countsA = new NativeArray<int>(count, Allocator.TempJob);
            using var countsB = new NativeArray<int>(count, Allocator.TempJob);
            using var backend = new NativeArray<int>(count, Allocator.TempJob);
            var a = new CandidateJob { Grid = state.Grid.AsReader(), Ground = t.World.Column(BwBeltKeys.Ground), Motions = t.World.Column(BwBeltKeys.Motion),
                Handles = t.World.Table(BwKeys.Fighter).Handles, Info = t.World.Column(BwKeys.Info), Output = sequential, Counts = countsA, Backend = backend };
            var b = a; b.Output = parallel; b.Counts = countsB;
            for (int i = 0; i < count; i++) a.Execute(i); b.Schedule(count, 16).Complete();
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(sequential[i], parallel[i], "ordered float accumulation row=" + i); Assert.AreEqual(countsA[i], countsB[i]);
#if !SPF_DOTNET_HARNESS
                Assert.AreEqual(1, backend[i], "candidate must really execute through Burst for native evidence");
#endif
            }
            const int samples = 20, repeats = 30; var mainMs = new double[samples]; var jobMs = new double[samples];
            for (int sample = 0; sample < samples; sample++) for (int slot = 0; slot < 2; slot++)
            {
                bool scheduled = (sample + slot) % 2 != 0; long start = Stopwatch.GetTimestamp();
                for (int repeat = 0; repeat < repeats; repeat++)
                    if (scheduled) b.Schedule(count, 16).Complete(); else for (int i = 0; i < count; i++) a.Execute(i);
                (scheduled ? jobMs : mainMs)[sample] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency / repeats;
            }
            for (int i = 0; i < count; i++) { Assert.AreEqual(sequential[i], parallel[i]); Assert.AreEqual(countsA[i], countsB[i]); }
            string Summary(double[] times) { double sum = 0; foreach (double x in times) sum += x; Array.Sort(times); return string.Format(CultureInfo.InvariantCulture, "mean={0:F5}; p50={1:F5}; p95={2:F5} ms/pass", sum / times.Length, times[times.Length / 2], times[(int)(times.Length * .95)]); }
#if SPF_DOTNET_HARNESS
            const string runtime = "dotnet-stubs"; string directory = Path.Combine(Path.GetTempPath(), "spf-artifacts");
#else
            const string runtime = "unity-native-editmode"; string directory = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".", "Artifacts");
#endif
            string report = "Belt separation candidate ONLY; runtime=" + runtime + "; count=" + count + "; clustered=" + clustered + "\n" +
                "Frozen completed grid; unchanged visitor order/float sums; per-row positions/counts, no atomics; exact outputs=PASS.\n" +
                "Ordered main-thread: " + Summary(mainMs) + "\nScheduled parallel including Complete: " + Summary(jobMs) + "\n" +
                "20 alternating-order samples x 30 repeated passes; excludes grid build, copyback, rest of tick/render. Not retained in production pending native whole-tick benefit; stub job scheduling is synchronous and cannot establish a Burst speedup.\n";
            TestContext.WriteLine(report); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "perf-belt-separation-candidate-" + runtime + "-" + count + "-" + clustered + ".txt"), report);
        }
    }
}
