using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.L2.AI;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace RpgFoundation.Tests
{
#if SPF_DOTNET_HARNESS
    [NonParallelizable]
#endif
    public class RpgAiWorkloadTests
    {
        static void Write(string name, string report)
        {
#if SPF_DOTNET_HARNESS
            const string runtime = "dotnet-stubs";
            string directory = Path.Combine(Path.GetTempPath(), "spf-artifacts");
#else
            const string runtime = "unity-native-editmode";
            string directory = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".", "Artifacts");
#endif
            report = "runtime=" + runtime + "\n" + report;
            TestContext.WriteLine(report); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + "-" + runtime + ".txt"), report);
        }
        static string Stats(double[] values)
        {
            double sum = 0; foreach (double v in values) sum += v; Array.Sort(values);
            return string.Format(CultureInfo.InvariantCulture, "mean={0:F5} p50={1:F5} p95={2:F5} worst={3:F5} ms", sum / values.Length, values[values.Length / 2], values[(int)(values.Length * .95)], values[values.Length - 1]);
        }
        static void Tick(RpgTestWorld t, int tick)
        {
            t.World.Resource(RpgKeys.Feedback).Clear();
            t.Input(new InputFrame { Move = new float2(tick % 90 < 45 ? .25f : -.25f, tick % 70 < 35 ? .1f : -.1f), Held = 1 });
            t.Step();
        }
        static void Populate(RpgTestWorld t, int count, bool clustered)
        {
            t.ClearMonsters();
            for (int i = 0; i < count; i++)
            {
                int kind = 1 + i % t.Runtime.Monsters.Length;
                var p = t.FreeSpotNearHero(clustered ? .8f + i % 3 * .15f : 2f + i % 12 * .5f);
                var handle = t.SpawnMonster(kind, p); Assert.IsFalse(handle.IsNull);
                int row = t.Row(handle); var brain = t.World.Column(RpgKeys.Brain)[row];
                brain.State = i % 4 == 0 ? AIState.Idle : AIState.Chase; brain.Provoked = i % 3;
                t.World.Column(RpgKeys.Brain).Set(row, brain);
            }
            t.Game.MonstersAlive = count;
        }
        [TestCase(32, false)] [TestCase(32, true)] [TestCase(256, false)] [TestCase(256, true)]
        [Category("Performance")]
        public void CompleteTickLegacyVersusTreeAbba(int monsters, bool clustered)
        {
            Action<RpgConfig> configure = c => { c.Hero.Health = 1000000; c.UseDecisionTree = true; foreach (var m in c.Monsters) m.Health = 1000000; };
            using var tree = new RpgTestWorld(seed: 71, runSeed: 91, tweak: configure);
            using var legacy = new RpgTestWorld(seed: 71, runSeed: 91, tweak: configure, legacyAi: true);
            Populate(tree, monsters, clustered); Populate(legacy, monsters, clustered);
            CollectionAssert.AreEqual(tree.Session.CaptureSnapshot(), legacy.Session.CaptureSnapshot(), "populated initial parity");
            for (int i = 0; i < 90; i++)
            {
                Tick(tree, i); Tick(legacy, i);
                for (int row = 0; row < tree.World.Table(RpgKeys.Actor).Count; row++)
                {
                    Assert.AreEqual(tree.World.Column(RpgKeys.Brain)[row], legacy.World.Column(RpgKeys.Brain)[row], "brain tick=" + i + " row=" + row);
                    Assert.AreEqual(tree.World.Column(RpgKeys.MoveIntent)[row], legacy.World.Column(RpgKeys.MoveIntent)[row], "intent tick=" + i + " row=" + row);
                    Assert.AreEqual(tree.World.Column(RpgKeys.Position)[row], legacy.World.Column(RpgKeys.Position)[row], "position tick=" + i + " row=" + row);
                    Assert.AreEqual(tree.World.Column(RpgKeys.Combat)[row], legacy.World.Column(RpgKeys.Combat)[row], "combat tick=" + i + " row=" + row);
                }
                CollectionAssert.AreEqual(tree.Session.CaptureSnapshot(), legacy.Session.CaptureSnapshot(), "full warmup tick=" + i);
            }
            var initial = tree.Session.CaptureSnapshot(); CollectionAssert.AreEqual(initial, legacy.Session.CaptureSnapshot(), "warmup legacy parity");
            const int samples = 120; var baselineMs = new double[samples * 2]; var treeMs = new double[samples * 2]; byte[] expected = null;
            for (int window = 0; window < 4; window++)
            {
                bool useTree = window == 1 || window == 2;
                var world = useTree ? tree : legacy; world.Session.RestoreSnapshot(initial);
                int offset = (window < 2 ? 0 : samples); var times = useTree ? treeMs : baselineMs;
                for (int i = 0; i < samples; i++)
                { long start = Stopwatch.GetTimestamp(); Tick(world, i); times[offset + i] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency; }
                var snapshot = world.Session.CaptureSnapshot();
                if (expected == null) expected = snapshot; else CollectionAssert.AreEqual(expected, snapshot, "complete ABBA window=" + window);
            }
            Write("perf-rpg-ai-tick-" + monsters + "-" + clustered,
                "RPG complete tick ABBA; A=frozen 1c9731a AI executor; B=bounded tree; both use corrected canonical queues. monsters=" + monsters + "; clustered=" + clustered + "; warmup=90; each window=120 ticks at 30Hz, restored same evolved snapshot.\n" +
                "Legacy: " + Stats(baselineMs) + "\nTree: " + Stats(treeMs) + "\nExact complete snapshots all four windows=PASS.\n" +
                "Includes original perception/line-of-sight, both NPC/player actions, navigation, collisions, damage, lifecycle, scheduler and completed jobs; feedback drained identically. All decision rates identical. No render/GPU/device evidence. Stub timings do not prove Burst; timing is report-only and tree reuse is not a speed claim.");
        }
        [BurstCompile(CompileSynchronously = true)]
        struct KernelJob : IJob
        {
            [ReadOnly] public NativeArray<DecisionNode> Nodes;
            [ReadOnly] public NativeArray<uint> Facts;
            [ReadOnly] public NativeArray<float2> Positions;
            public NativeArray<int> Output;
            public TileMapView Map;
            public int Mode, Repeats;
            public void Execute()
            {
                for (int repeat = 0; repeat < Repeats; repeat++)
                for (int i = 0; i < Facts.Length; i++)
                {
                    uint f = Facts[i] ^ (uint)(repeat & 31);
                    int value;
                    if (Mode == 2)
                    {
                        // Real tile LOS + original distance/visibility gates, isolated from selection.
                        float2 target = new float2(16.5f + (repeat & 1) * .25f, 16.5f); float distance = math.distance(Positions[i], target);
                        bool sees = distance < 12f * 1.6f && Map.LineOfSight(Positions[i], target);
                        f = RpgDecisions.Facts((i & 15) == 0 && sees && distance < 4f * .85f, (i & 1) != 0,
                            sees, distance <= 5f && (sees || distance < 1.5f), distance, 5f);
                        value = (int)f;
                    }
                    else if (Mode == 1) value = (int)RpgDecisions.Select(Nodes, f, out _);
                    else value = (int)RpgAiDecisionTests.Legacy((f & 1) != 0, (f & 2) != 0, (f & 16) != 0, (f & 8) != 0, (f & 4) != 0);
                    Output[i] = repeat == 0 ? value : unchecked(Output[i] * 31 + value);
                }
            }
        }
        [Test] [Category("Performance")]
        public void ScheduledSelectorAndPerceptionCostsAreReportedSeparately()
        {
            const int count = 4096, repeats = 16, samples = 12;
            using var nodes = RpgDecisions.CreateProgram();
            using var facts = new NativeArray<uint>(count, Allocator.TempJob);
            using var positions = new NativeArray<float2>(count, Allocator.TempJob);
            using var output = new NativeArray<int>(count, Allocator.TempJob);
            using var map = new TileMap(new int2(32), 1f);
            var writableFacts = facts; var writablePositions = positions;
            for (int i = 0; i < count; i++) { writableFacts[i] = (uint)(i % 32); writablePositions[i] = new float2(.5f + i % 32, .5f + i / 32 % 32); }
            for (int y = 2; y < 30; y++) if (y % 6 != 0) map[new int2(12, y)] = 1;
            var job = new KernelJob { Nodes = nodes, Facts = facts, Positions = positions, Output = output, Map = map.AsView(), Repeats = repeats };
            for (int mode = 0; mode < 3; mode++) { job.Mode = mode; job.Schedule().Complete(); }
            var baseline = new double[samples]; var tree = new double[samples]; var perception = new double[samples];
            for (int i = 0; i < samples; i++)
            {
                // Alternate selector order to limit systematic warm/cache order bias.
                for (int slot = 0; slot < 3; slot++)
                {
                    int mode = slot == 2 ? 2 : (i % 2 == 0 ? slot : 1 - slot); job.Mode = mode;
                    long start = Stopwatch.GetTimestamp(); job.Schedule().Complete();
                    (mode == 0 ? baseline : mode == 1 ? tree : perception)[i] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                    if (mode != 2) for (int k = 0; k < count; k++)
                    {
                        int expected = 0;
                        for (int repeat = 0; repeat < repeats; repeat++)
                        {
                            uint f = facts[k] ^ (uint)(repeat & 31);
                            int value = (int)RpgAiDecisionTests.Legacy((f & 1) != 0, (f & 2) != 0, (f & 16) != 0, (f & 8) != 0, (f & 4) != 0);
                            expected = repeat == 0 ? value : unchecked(expected * 31 + value);
                        }
                        Assert.AreEqual(expected, output[k]);
                    }
                }
            }
            Write("perf-rpg-ai-kernels", "Scheduled completed kernel; 4096 agents x 16 evaluations/sample; 12 samples, all 32 fact patterns balanced and varied each repeat; digest retains every result; identical decision work/cached facts for selectors.\n" +
                "Legacy branches: " + Stats(baseline) + "\nTree selection: " + Stats(tree) + "\nSeparate 32x32 tile LOS/distance/perception: " + Stats(perception) + "\n" +
                "Selection outputs exact=PASS. Perception is measured separately, not subtracted from tick timing. Includes schedule/Complete overhead; no gameplay cadence reduction. Stub scheduling is synchronous and is not native Burst evidence.");
        }
    }
}
