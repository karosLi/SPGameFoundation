using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    /// <summary>
    /// Bullet-heaven stress: thousands of enemies (a fifth of them spiral casters, so tens of thousands of
    /// pooled bullets), all hero weapons maxed, the hero invulnerable. Runs with and without the periodic
    /// Morton sort of the enemy table (A/B). Writes Artifacts/perf-survivor.txt.
    /// </summary>
    [Category("Performance")]
    public class SvPerformanceTests
    {
#if !SPF_DOTNET_HARNESS
        const int EnemyCount = 3000, Warmup = 150, Measured = 450, Serial = 150;
#else
        const int EnemyCount = 300, Warmup = 20, Measured = 30, Serial = 10;
#endif

        static string Run(int reorder, out double mean, out int bullets)
        {
            using var t = new SvTestWorld(seed: 21, tweak: c =>
            {
                c.Settings.SpawnPerSecond = 0f; c.Settings.SpawnGrowth = 0f; c.Settings.EliteEvery = 0f;
                c.Settings.HeroHp = 1e9f;
                c.Settings.ReorderInterval = reorder;
                c.Capacity.Enemies = EnemyCount + 64;
            });
            var g = t.Game;
            for (int u = 0; u < SvGameState.UpgradeCount; u++) g.Upgrades[u] = SvGameState.MaxLevel;
            g.MaxHp = g.Hp = 1e9f;
            int mage = 0;
            for (int k = 0; k < t.Runtime.EnemyKinds; k++) if (t.Runtime.Enemies[k].Shooter) mage = k + 1;
            var random = new Unity.Mathematics.Random(77);
            for (int i = 0; i < EnemyCount; i++)
            {
                int kind = i % 5 == 0 ? mage : 1 + random.NextInt(3);
                // A ring around the hero, inside the casters' range.
                float a = random.NextFloat(math.PI * 2f), r = random.NextFloat(6f, 24f);
                t.Spawn(kind, new float2(math.cos(a), math.sin(a)) * r);
            }
            // Make the horde durable so the population stays high during the measurement.
            var infos = t.World.Column(SvKeys.Info);
            for (int i = 0; i < t.Enemies; i++) { var e = infos[i]; e.Hp = e.MaxHp = 1e6f; infos[i] = e; }

            void Tick(int i)
            {
                t.Input(new float2(math.sin(i * 0.02f), math.cos(i * 0.017f)) * 0.5f);
                t.Step();
                if (g.Flow == SvFlow.LevelUp) g.Send(SvCommandKind.Choose, 0);
            }
            for (int i = 0; i < Warmup; i++) Tick(i);
            var samples = new double[Measured];
            var stats = t.Session.Pipeline.Stats;
            double schedule = 0, wait = 0;
            int peakBullets = 0;
            for (int i = 0; i < Measured; i++)
            {
                t.Input(new float2(math.sin(i * 0.02f), math.cos(i * 0.017f)) * 0.5f);
                long t0 = Stopwatch.GetTimestamp();
                t.Session.Step();
                samples[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                schedule += stats.ScheduleMs;
                wait += stats.SyncWaitMs;
                peakBullets = Math.Max(peakBullets, t.Bullets);
                if (g.Flow == SvFlow.LevelUp) g.Send(SvCommandKind.Choose, 0);
            }
            var pipeline = t.Session.Pipeline;
            pipeline.SerialProfiling = true;
            stats.ClearExecuteTimes();
            for (int i = 0; i < Serial; i++) Tick(i);
            pipeline.SerialProfiling = false;

            Array.Sort(samples);
            mean = 0;
            foreach (var s in samples) mean += s;
            mean /= samples.Length;
            bullets = peakBullets;
            var sb = new StringBuilder();
            sb.AppendLine($"--- enemy table Morton sort every {(reorder > 0 ? reorder + " ticks" : "never")} ---");
            sb.AppendLine($"enemies {t.Enemies}  bullets {t.Bullets} (peak {peakBullets})  gems {t.Gems}  kills {g.Kills}");
            sb.AppendLine($"pipelined tick ms  mean {mean:F3}  p50 {samples[samples.Length / 2]:F3}  p95 {samples[(int)(samples.Length * 0.95)]:F3}  max {samples[samples.Length - 1]:F3}");
            sb.AppendLine($"main-thread schedule ms {schedule / Measured:F3}  sync wait ms {wait / Measured:F3}");
            sb.AppendLine("per-system ms (serial profiling):");
            for (int i = 0; i < stats.SystemCount; i++)
                sb.AppendLine($"  {stats.SystemName(i),-22} {stats.SystemExecuteMs(i),8:F3}");
            return sb.ToString();
        }

        [Test]
        public void BulletHeavenBenchmark()
        {
            string withSort = Run(30, out double sorted, out int bullets);
            string without = Run(0, out double unsorted, out _);
            var sb = new StringBuilder();
            sb.AppendLine("=== Survivor bullet-heaven benchmark ===");
            sb.AppendLine($"bytes uploaded per bullet sprite: {SPF.Presentation.Sprites.PackedSprite.Stride} (packed)");
            sb.Append(withSort);
            sb.Append(without);
            sb.AppendLine($"Morton sort speed-up: {unsorted / Math.Max(sorted, 1e-6):F2}x");
            string report = sb.ToString();
            TestContext.WriteLine(report);
            Console.WriteLine(report);
            try
            {
#if !SPF_DOTNET_HARNESS
                string dir = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? ".", "Artifacts");
#else
                string dir = Path.Combine(Path.GetTempPath(), "spf-artifacts");
#endif
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "perf-survivor.txt"), report);
            }
            catch (IOException) { }
            Assert.Greater(bullets, EnemyCount, "a bullet storm was simulated");
        }
    }
}
