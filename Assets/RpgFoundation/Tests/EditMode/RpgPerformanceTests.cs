using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using SPF.Runtime.Scheduling;

namespace RpgFoundation.Tests
{
    /// <summary>
    /// Horde benchmark: a large dense floor (several hundred monsters), the bot playing. Pipelined tick
    /// time plus per-system cost (serial profiling). Writes Artifacts/perf-rpg.txt.
    /// </summary>
    [Category("Performance")]
    public class RpgPerformanceTests
    {
#if !SPF_DOTNET_HARNESS
        const int Warmup = 150, Measured = 600, Serial = 300;
#else
        const int Warmup = 20, Measured = 40, Serial = 20;
#endif

        [Test]
        public void HordeBenchmark()
        {
            using var t = new RpgTestWorld(seed: 3, runSeed: 9, tweak: c =>
            {
                c.Dungeon.Width = c.Dungeon.Height = 112;
                c.Dungeon.RoomsMin = 18;
                c.Dungeon.RoomsMax = 22;
                c.Dungeon.RoomSizeMax = 14;
                c.Dungeon.MonsterDensity = 0.2f;
                c.Hero.Health = 100000f;   // the bot must survive the whole benchmark
            });
            using var bot = new RpgBot();
            void Tick() { t.Input(bot.Think(t.World)); t.Step(); }
            for (int i = 0; i < Warmup; i++) Tick();

            var samples = new double[Measured];
            var stats = t.Session.Pipeline.Stats;
            double schedule = 0, wait = 0;
            for (int i = 0; i < Measured; i++)
            {
                t.Input(bot.Think(t.World));
                long t0 = Stopwatch.GetTimestamp();
                t.Session.Step();
                samples[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                schedule += stats.ScheduleMs;
                wait += stats.SyncWaitMs;
            }
            var pipeline = t.Session.Pipeline;
            pipeline.SerialProfiling = true;
            stats.ClearExecuteTimes();
            for (int i = 0; i < Serial; i++) Tick();
            pipeline.SerialProfiling = false;

            Array.Sort(samples);
            double mean = 0;
            foreach (var s in samples) mean += s;
            mean /= samples.Length;
            var sb = new StringBuilder();
            sb.AppendLine("=== RPG horde benchmark ===");
            sb.AppendLine($"actors: {t.World.Table(RpgKeys.Actor).Count}  monsters alive: {t.Game.MonstersAlive}/{t.Game.FloorMonsters}  projectiles: {t.World.Table(RpgKeys.Projectile).Count}  items: {t.World.Table(RpgKeys.Item).Count}");
            sb.AppendLine($"bot: kills {t.Game.Profile.Kills}, level {t.Game.Profile.Level}");
            sb.AppendLine($"pipelined tick ms  mean {mean:F3}  p50 {samples[samples.Length / 2]:F3}  p95 {samples[(int)(samples.Length * 0.95)]:F3}  max {samples[samples.Length - 1]:F3}");
            sb.AppendLine($"main-thread schedule ms {schedule / Measured:F3}  sync wait ms {wait / Measured:F3}");
            sb.AppendLine("per-system ms (serial profiling, includes scheduling):");
            double total = 0;
            for (int i = 0; i < stats.SystemCount; i++)
            {
                total += stats.SystemExecuteMs(i);
                sb.AppendLine($"  {stats.SystemName(i),-28} {stats.SystemExecuteMs(i),8:F3}");
            }
            sb.AppendLine($"  {"TOTAL (serial)",-28} {total,8:F3}");
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
                File.WriteAllText(Path.Combine(dir, "perf-rpg.txt"), report);
            }
            catch (IOException) { }
            Assert.Greater(t.Game.FloorMonsters, 250, "a horde was spawned");
        }
    }
}
