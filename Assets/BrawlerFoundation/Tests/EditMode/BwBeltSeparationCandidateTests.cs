using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
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
        struct ReferenceVisitor : IGridVisitor
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
        struct Visitor : IGridVisitor
        {
            public int Row, Candidates; public float2 Self, Push; public float Height;
            public SeparationEvaluation Evaluation;
            [ReadOnly] public NativeArray<BwBeltMotion> Motions;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public bool Visit(in GridEntry entry)
            {
                if (entry.Owner == Row) return true;
                Candidates++;
                if (BwBeltSeparationArithmetic.SameHeight(Height, Motions[entry.Owner].Height, Evaluation))
                    Push += BwBeltSeparationArithmetic.Contribution(Self, entry.Position, BwRules.BodyHalfWidth * 2,
                        Handles[Row].Index, Handles[entry.Owner].Index, Evaluation);
                return true;
            }
        }
        // Strict/High alone was insufficient: legacy Mono uses wider scalar intermediates.
        // The selected explicit profile is immutable job input; exact production parity is the gate.
        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        struct CandidateJob : IJobParallelFor
        {
            public GridReader Grid;
            public SeparationEvaluation Evaluation;
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
                var visitor = new Visitor { Row = i, Self = Ground[i], Height = Motions[i].Height, Motions = Motions, Handles = Handles, Evaluation = Evaluation };
                if (Info[i].State != FighterState.KO) Grid.Query(Ground[i], BwRules.BodyHalfWidth, ref visitor);
                float2 push = visitor.Push; float length = BwBeltSeparationArithmetic.Length(push, Evaluation); if (length > .16f) push *= .16f / length;
                Output[i] = BwBeltRules.ClampGround(Ground[i] + push); Counts[i] = visitor.Candidates;
            }
#if !SPF_DOTNET_HARNESS
            [BurstDiscard] static void Managed(ref bool burst) => burst = false;
#endif
        }
        // Independent frozen production reference: no candidate arithmetic, job Execute, or
        // profile selection is called by this path. Keep aligned with the ordered production pass.
        static void ExecuteReference(in CandidateJob data, int i)
        {
            var visitor = new ReferenceVisitor { Row = i, Self = data.Ground[i], Height = data.Motions[i].Height,
                Motions = data.Motions, Handles = data.Handles };
            if (data.Info[i].State != FighterState.KO) data.Grid.Query(data.Ground[i], BwRules.BodyHalfWidth, ref visitor);
            float2 push = visitor.Push; float length = math.length(push); if (length > .16f) push *= .16f / length;
            var output = data.Output; var counts = data.Counts;
            output[i] = BwBeltRules.ClampGround(data.Ground[i] + push); counts[i] = visitor.Candidates;
        }

        [Test]
        public void CandidatePinsStrictHighPrecisionForExactManagedParity()
        {
            var settings = (BurstCompileAttribute)Attribute.GetCustomAttribute(typeof(CandidateJob), typeof(BurstCompileAttribute));
            Assert.IsNotNull(settings);
            Assert.AreEqual(FloatMode.Strict, settings.FloatMode);
            Assert.AreEqual(FloatPrecision.High, settings.FloatPrecision);
            Assert.IsTrue(settings.CompileSynchronously);
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
            var evaluation = BwBeltSeparationArithmetic.DetectManagedEvaluation();
            var a = new CandidateJob { Evaluation = evaluation, Grid = state.Grid.AsReader(), Ground = t.World.Column(BwBeltKeys.Ground), Motions = t.World.Column(BwBeltKeys.Motion),
                Handles = t.World.Table(BwKeys.Fighter).Handles, Info = t.World.Column(BwKeys.Info), Output = sequential, Counts = countsA, Backend = backend };
            var b = a; b.Output = parallel; b.Counts = countsB;
            for (int i = 0; i < count; i++) ExecuteReference(a, i); b.Schedule(count, 16).Complete();
            WriteArithmeticDiagnostics(a, b, count, "original-" + clustered);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(sequential[i], parallel[i], "ordered float accumulation row=" + i +
                    "; managed bits=" + math.asuint(sequential[i]) + "; Burst bits=" + math.asuint(parallel[i]) +
                    "; policy=Strict/High/" + evaluation + "; backend=" + backend[i]);
                Assert.AreEqual(math.asuint(sequential[i]), math.asuint(parallel[i]), "raw output bits row=" + i);
                Assert.AreEqual(countsA[i], countsB[i]);
#if !SPF_DOTNET_HARNESS
                Assert.AreEqual(1, backend[i], "candidate must really execute through Burst for native evidence");
#endif
            }
            const int samples = 20, repeats = 30; var mainMs = new double[samples]; var jobMs = new double[samples];
            for (int sample = 0; sample < samples; sample++) for (int slot = 0; slot < 2; slot++)
            {
                bool scheduled = (sample + slot) % 2 != 0; long start = Stopwatch.GetTimestamp();
                for (int repeat = 0; repeat < repeats; repeat++)
                    if (scheduled) b.Schedule(count, 16).Complete(); else for (int i = 0; i < count; i++) ExecuteReference(a, i);
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
                "Frozen completed grid; independent unchanged production reference; explicit arithmetic=" + evaluation + "; Burst Strict/High; per-row positions/counts, no atomics; exact outputs=PASS.\n" +
                "Ordered main-thread: " + Summary(mainMs) + "\nScheduled parallel including Complete: " + Summary(jobMs) + "\n" +
                "20 alternating-order samples x 30 repeated passes; excludes grid build, copyback, rest of tick/render. Not retained in production pending native whole-tick benefit; stub job scheduling is synchronous and cannot establish a Burst speedup.\n";
            TestContext.WriteLine(report); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "perf-belt-separation-candidate-" + runtime + "-" + count + "-" + clustered + ".txt"), report);
        }
        [TestCase(32, 0)] [TestCase(128, 0)] [TestCase(128, 1)] [TestCase(128, 2)]
        public void AdversarialDenseCandidateMatchesFrozenProductionAcrossBatchSizes(int count, int pattern)
        {
            var config = BwBeltConfig.Default; config.Fighters = config.TargetsPerAttack = count;
            using var t = new BwTestWorld(belt: config); t.World.ClearLevel();
            for (int i = 0; i < count; i++)
            {
                // Negative cells, mirrored cancellation, adjacent representable coordinates,
                // exact coincident handles, tiny distances and height-threshold neighbours.
                float x = math.asfloat(0x3D000000u + (uint)((i * 37) % 128) * 60001u);
                float y = math.asfloat(0x3D000000u + (uint)((i * 53) % 128) * 50021u);
                float2 p = pattern == 0 ? new float2(i % 2 == 0 ? x : -x, i % 4 < 2 ? y : -y) :
                    pattern == 1 ? new float2(i % 9 == 0 ? 0 : x * .00001f, i % 9 == 0 ? 0 : -y * .00001f) :
                    new float2(-.5f + i % 8 * .04f, -.5f + i / 8 * .04f);
                BwSpawner.Spawn(t.World, (byte)(i == 0 ? 0 : 1), p, 1, 1);
                var motion = t.World.Column(BwBeltKeys.Motion)[i];
                motion.Height = i % 5 == 0 ? math.asfloat(math.asuint(.65f) - 1u) :
                    i % 5 == 1 ? .65f : i % 5 == 2 ? math.asfloat(math.asuint(.65f) + 1u) : 0;
                t.World.Column(BwBeltKeys.Motion).Set(i, motion);
                if (i % 17 == 0 && i > 0) { var info = t.Info(i); info.State = FighterState.KO; t.World.Column(BwKeys.Info).Set(i, info); }
            }
            var state = t.World.Resource(BwBeltKeys.State); state.Rebuild(t.World);
            using var sequential = new NativeArray<float2>(count, Allocator.TempJob);
            using var parallel = new NativeArray<float2>(count, Allocator.TempJob);
            using var countsA = new NativeArray<int>(count, Allocator.TempJob);
            using var countsB = new NativeArray<int>(count, Allocator.TempJob);
            using var backend = new NativeArray<int>(count, Allocator.TempJob);
            var a = new CandidateJob { Evaluation = BwBeltSeparationArithmetic.DetectManagedEvaluation(), Grid = state.Grid.AsReader(),
                Ground = t.World.Column(BwBeltKeys.Ground), Motions = t.World.Column(BwBeltKeys.Motion),
                Handles = t.World.Table(BwKeys.Fighter).Handles, Info = t.World.Column(BwKeys.Info), Output = sequential, Counts = countsA, Backend = backend };
            var b = a; b.Output = parallel; b.Counts = countsB;
            for (int i = 0; i < count; i++) ExecuteReference(a, i);
            foreach (int batch in new[] { 1, 7, 32 })
            {
                b.Schedule(count, batch).Complete();
                if (batch == 1) WriteArithmeticDiagnostics(a, b, count, "adversarial-" + pattern);
                for (int i = 0; i < count; i++)
                {
                    Assert.AreEqual(math.asuint(sequential[i]), math.asuint(parallel[i]), "row=" + i + "; batch=" + batch + "; profile=" + a.Evaluation);
                    Assert.AreEqual(countsA[i], countsB[i]);
#if !SPF_DOTNET_HARNESS
                    Assert.AreEqual(1, backend[i], "candidate must really execute through Burst");
#endif
                }
            }
        }

        struct PairInput { public float2 Self, Other; public int SelfOrder, OtherOrder; }
        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        struct PairJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<PairInput> Input;
            public NativeArray<float2> Output;
            public NativeArray<int> Backend;
            public SeparationEvaluation Evaluation;
            public void Execute(int i)
            {
                bool burst = true;
#if !SPF_DOTNET_HARNESS
                Managed(ref burst);
#else
                burst = false;
#endif
                Backend[i] = burst ? 1 : 0;
                var p = Input[i]; Output[i] = BwBeltSeparationArithmetic.Contribution(p.Self, p.Other, .64f, p.SelfOrder, p.OtherOrder, Evaluation);
            }
#if !SPF_DOTNET_HARNESS
            [BurstDiscard] static void Managed(ref bool burst) => burst = false;
#endif
        }
        [Test]
        public void ExplicitArithmeticMatchesUnchangedProductionOnBoundaryAndSeededPairs()
        {
            const int count = 4096;
            using var input = new NativeArray<PairInput>(count, Allocator.TempJob);
            using var output = new NativeArray<float2>(count, Allocator.TempJob);
            using var backend = new NativeArray<int>(count, Allocator.TempJob);
            uint seed = 0x1A297C31u;
            float Next() { seed = unchecked(seed * 1664525u + 1013904223u); return math.asfloat(0x3F800000u | (seed & 0x7FFFFFu)) - 1.5f; }
            for (int i = 0; i < count; i++)
            {
                float2 self = new float2(Next(), Next()), other = new float2(Next(), Next());
                if (i < 64)
                {
                    self = float2.zero;
                    uint center = i < 32 ? math.asuint(.64f) : math.asuint(.001f);
                    float adjacent = math.asfloat(center + (uint)(i % 16) - 8u);
                    other = i % 2 == 0 ? new float2(adjacent, 0) : new float2(0, -adjacent);
                }
                if (i % 127 == 0) other = self;
                input.Set(i, new PairInput { Self = self, Other = other, SelfOrder = i % 2, OtherOrder = 1 - i % 2 });
            }
            var evaluation = BwBeltSeparationArithmetic.DetectManagedEvaluation();
            new PairJob { Input = input, Output = output, Backend = backend, Evaluation = evaluation }.Schedule(count, 31).Complete();
            var csv = new StringBuilder("case,profile,backend,selfXBits,selfYBits,otherXBits,otherYBits,expectedXBits,expectedYBits,actualXBits,actualYBits\n");
            int firstMismatch = -1;
            for (int i = 0; i < count; i++)
            {
                var p = input[i]; var expected = GroundCombatQueries.Separation(p.Self, p.Other, .64f, p.SelfOrder, p.OtherOrder);
                csv.Append(i).Append(',').Append(evaluation).Append(',').Append(backend[i]).Append(',').Append(math.asuint(p.Self.x)).Append(',').Append(math.asuint(p.Self.y))
                    .Append(',').Append(math.asuint(p.Other.x)).Append(',').Append(math.asuint(p.Other.y)).Append(',').Append(math.asuint(expected.x)).Append(',').Append(math.asuint(expected.y))
                    .Append(',').Append(math.asuint(output[i].x)).Append(',').Append(math.asuint(output[i].y)).AppendLine();
                if (firstMismatch < 0 && !math.all(math.asuint(expected) == math.asuint(output[i]))) firstMismatch = i;
            }
            Directory.CreateDirectory(ArtifactDirectory()); File.WriteAllText(Path.Combine(ArtifactDirectory(), "separation-arithmetic-pairs.csv"), csv.ToString());
            TestContext.WriteLine("Arithmetic profile=" + evaluation + "; exact pair cases=" + count + "; first mismatch=" + firstMismatch);
            Assert.AreEqual(-1, firstMismatch, "exact unchanged production pair; see raw-bit artifact");
#if !SPF_DOTNET_HARNESS
            for (int i = 0; i < count; i++) Assert.AreEqual(1, backend[i], "pair must really execute through Burst");
#endif
        }

        struct ArithmeticTrace
        {
            public int Owner, SameHeight;
            public float2 Delta, Contribution, Accumulated;
            public float Square, Distance, Scale;
        }
        struct RowTrace
        {
            public float2 Push, Output;
            public float Length, ClampScale;
            public int Count, Backend;
        }
        struct TraceVisitor : IGridVisitor
        {
            public int Row, Count, Offset;
            public float2 Self, Push;
            public float Height;
            public bool Legacy;
            public SeparationEvaluation Evaluation;
            [ReadOnly] public NativeArray<BwBeltMotion> Motions;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<ArithmeticTrace> Traces;
            public bool Visit(in GridEntry entry)
            {
                if (entry.Owner == Row) return true;
                float2 delta = Self - entry.Position;
                float square = Legacy ? math.lengthsq(delta) : BwBeltSeparationArithmetic.Square(delta, Evaluation);
                float distance = Legacy ? math.sqrt(square) : BwBeltSeparationArithmetic.Distance(square);
                float scale = distance > 0 ? (Legacy ? (BwRules.BodyHalfWidth * 2 - distance) * .5f / distance :
                    BwBeltSeparationArithmetic.Scale(BwRules.BodyHalfWidth * 2, distance, Evaluation)) : 0;
                bool sameHeight = Legacy ? math.abs(Height - Motions[entry.Owner].Height) < .65f :
                    BwBeltSeparationArithmetic.SameHeight(Height, Motions[entry.Owner].Height, Evaluation);
                float2 contribution = float2.zero;
                if (sameHeight) contribution = Legacy ? GroundCombatQueries.Separation(Self, entry.Position,
                    BwRules.BodyHalfWidth * 2, Handles[Row].Index, Handles[entry.Owner].Index) :
                    BwBeltSeparationArithmetic.Contribution(Self, entry.Position, BwRules.BodyHalfWidth * 2,
                        Handles[Row].Index, Handles[entry.Owner].Index, Evaluation);
                Push += contribution;
                Traces[Offset + Count++] = new ArithmeticTrace { Owner = entry.Owner, SameHeight = sameHeight ? 1 : 0,
                    Delta = delta, Square = square, Distance = distance, Scale = scale, Contribution = contribution, Accumulated = Push };
                return true;
            }
        }
        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        struct TraceJob : IJob
        {
            public GridReader Grid;
            public int Count;
            public bool Legacy;
            public SeparationEvaluation Evaluation;
            [ReadOnly] public NativeArray<float2> Ground;
            [ReadOnly] public NativeArray<BwBeltMotion> Motions;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            [ReadOnly] public NativeArray<FighterInfo> Info;
            public NativeArray<ArithmeticTrace> Traces;
            public NativeArray<RowTrace> Rows;
            public void Execute()
            {
                bool burst = true;
#if !SPF_DOTNET_HARNESS
                Managed(ref burst);
#else
                burst = false;
#endif
                for (int i = 0; i < Count; i++)
                {
                    var visitor = new TraceVisitor { Row = i, Offset = i * Count, Self = Ground[i], Height = Motions[i].Height,
                        Motions = Motions, Handles = Handles, Traces = Traces, Legacy = Legacy, Evaluation = Evaluation };
                    if (Info[i].State != FighterState.KO) Grid.Query(Ground[i], BwRules.BodyHalfWidth, ref visitor);
                    float2 push = visitor.Push;
                    float length = Legacy ? math.length(push) : BwBeltSeparationArithmetic.Length(push, Evaluation);
                    float scale = length > .16f ? .16f / length : 1f;
                    if (length > .16f) push *= scale;
                    Rows[i] = new RowTrace { Push = visitor.Push, Length = length, ClampScale = scale,
                        Output = BwBeltRules.ClampGround(Ground[i] + push), Count = visitor.Count, Backend = burst ? 1 : 0 };
                }
            }
#if !SPF_DOTNET_HARNESS
            [BurstDiscard] static void Managed(ref bool burst) => burst = false;
#endif
        }
        static string ArtifactDirectory()
        {
#if SPF_DOTNET_HARNESS
            return Path.Combine(Path.GetTempPath(), "spf-artifacts");
#else
            return Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".", "Artifacts");
#endif
        }
        static void WriteArithmeticDiagnostics(in CandidateJob reference, in CandidateJob candidate, int count, string label)
        {
            using var managedTraces = new NativeArray<ArithmeticTrace>(count * count, Allocator.TempJob);
            using var burstTraces = new NativeArray<ArithmeticTrace>(count * count, Allocator.TempJob);
            using var managedRows = new NativeArray<RowTrace>(count, Allocator.TempJob);
            using var burstRows = new NativeArray<RowTrace>(count, Allocator.TempJob);
            var a = new TraceJob { Grid = reference.Grid, Count = count, Legacy = true, Evaluation = reference.Evaluation,
                Ground = reference.Ground, Motions = reference.Motions, Handles = reference.Handles, Info = reference.Info,
                Traces = managedTraces, Rows = managedRows };
            var b = a; b.Legacy = false; b.Traces = burstTraces; b.Rows = burstRows;
            a.Execute(); b.Schedule().Complete();
            var csv = new StringBuilder("source,profile,row,sequence,owner,sameHeight,deltaXBits,deltaYBits,squareBits,distanceBits,scaleBits,contributionXBits,contributionYBits,accumulatedXBits,accumulatedYBits\n");
            var rows = new StringBuilder("source,profile,row,backend,candidates,pushXBits,pushYBits,lengthBits,clampScaleBits,outputXBits,outputYBits,uninstrumentedXBits,uninstrumentedYBits\n");
            string firstDifference = null;
            for (int i = 0; i < count; i++) for (int source = 0; source < 2; source++)
            {
                var r = source == 0 ? managedRows[i] : burstRows[i]; var traces = source == 0 ? managedTraces : burstTraces;
                var output = source == 0 ? reference.Output[i] : candidate.Output[i];
                string name = source == 0 ? "production-managed" : "candidate-scheduled";
                rows.Append(name).Append(',').Append(reference.Evaluation).Append(',').Append(i).Append(',').Append(r.Backend).Append(',').Append(r.Count)
                    .Append(',').Append(math.asuint(r.Push.x)).Append(',').Append(math.asuint(r.Push.y)).Append(',').Append(math.asuint(r.Length))
                    .Append(',').Append(math.asuint(r.ClampScale)).Append(',').Append(math.asuint(r.Output.x)).Append(',').Append(math.asuint(r.Output.y))
                    .Append(',').Append(math.asuint(output.x)).Append(',').Append(math.asuint(output.y)).AppendLine();
                for (int j = 0; j < r.Count; j++)
                {
                    var t = traces[i * count + j];
                    csv.Append(name).Append(',').Append(reference.Evaluation).Append(',').Append(i).Append(',').Append(j).Append(',').Append(t.Owner)
                        .Append(',').Append(t.SameHeight).Append(',').Append(math.asuint(t.Delta.x)).Append(',').Append(math.asuint(t.Delta.y))
                        .Append(',').Append(math.asuint(t.Square)).Append(',').Append(math.asuint(t.Distance)).Append(',').Append(math.asuint(t.Scale))
                        .Append(',').Append(math.asuint(t.Contribution.x)).Append(',').Append(math.asuint(t.Contribution.y))
                        .Append(',').Append(math.asuint(t.Accumulated.x)).Append(',').Append(math.asuint(t.Accumulated.y)).AppendLine();
                    if (source == 1 && firstDifference == null)
                    {
                        var expected = managedTraces[i * count + j];
                        if (expected.Owner != t.Owner || expected.SameHeight != t.SameHeight ||
                            !math.all(math.asuint(expected.Delta) == math.asuint(t.Delta)) || math.asuint(expected.Square) != math.asuint(t.Square) ||
                            math.asuint(expected.Distance) != math.asuint(t.Distance) || math.asuint(expected.Scale) != math.asuint(t.Scale) ||
                            !math.all(math.asuint(expected.Contribution) == math.asuint(t.Contribution)) ||
                            !math.all(math.asuint(expected.Accumulated) == math.asuint(t.Accumulated))) firstDifference = "row=" + i + "; sequence=" + j + "; owner=" + t.Owner +
                            "; square=" + math.asuint(expected.Square) + "/" + math.asuint(t.Square) + "; distance=" + math.asuint(expected.Distance) + "/" + math.asuint(t.Distance) +
                            "; scale=" + math.asuint(expected.Scale) + "/" + math.asuint(t.Scale) + "; contribution=" + math.asuint(expected.Contribution) + "/" + math.asuint(t.Contribution) +
                            "; accumulated=" + math.asuint(expected.Accumulated) + "/" + math.asuint(t.Accumulated);
                    }
                }
            }
            string directory = ArtifactDirectory(); Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, "separation-arithmetic-" + count + "-" + label);
            File.WriteAllText(stem + "-contributions.csv", csv.ToString()); File.WriteAllText(stem + "-rows.csv", rows.ToString());
            TestContext.WriteLine("Arithmetic profile=" + reference.Evaluation + "; first differing trace=" + (firstDifference ?? "none") + "; raw bits=" + stem);
            Assert.IsNull(firstDifference, "per-contribution arithmetic parity: " + firstDifference);
            // Instrumentation is not allowed to change either result; artifacts precede every assertion.
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(math.asuint(reference.Output[i]), math.asuint(managedRows[i].Output), "reference trace perturbed row " + i);
                Assert.AreEqual(math.asuint(candidate.Output[i]), math.asuint(burstRows[i].Output), "candidate trace perturbed row " + i);
                Assert.AreEqual(reference.Counts[i], managedRows[i].Count);
                Assert.AreEqual(candidate.Counts[i], burstRows[i].Count);
#if !SPF_DOTNET_HARNESS
                Assert.AreEqual(1, burstRows[i].Backend, "diagnostic must execute through Burst");
#endif
            }
        }
    }
}
