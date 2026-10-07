using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using SPF.Runtime.World;

namespace SPF.Testing
{
    /// <summary>Bounded synchronous test export. Call after creating a real scenario. Formatting and
    /// file IO happen after measurement; never attach this to a player Update or a release HUD.</summary>
    public static class AcceptanceEvidence
    {
        public const int WarmupTicks = 16, MeasuredTicks = 64;
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public static void WriteIfRequested(GameplayAcceptanceCase scenario, string configuration, string orientation)
        {
            string json = Capture(scenario, configuration, orientation);
            string directory = Environment.GetEnvironmentVariable("SPF_ACCEPTANCE_OUT");
            if (string.IsNullOrWhiteSpace(directory)) return;
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SPF_ACCEPTANCE_COMMIT")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SPF_ACCEPTANCE_TREE")))
                throw new InvalidOperationException("Evidence export needs SPF_ACCEPTANCE_COMMIT and SPF_ACCEPTANCE_TREE.");
            foreach (char c in scenario.Name)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))
                    throw new ArgumentException("Evidence mode name must be a safe filename.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, scenario.Name + ".json"), json, new UTF8Encoding(false));
        }

        public static string Capture(GameplayAcceptanceCase a, string configuration, string orientation)
        {
            var session = a.Session; var world = session.World;
            var tables = world.Tables; var peak = new int[tables.Count];
            for (int i = 0; i < WarmupTicks; i++) a.Tick(i);
            var durations = new double[MeasuredTicks];
            uint first = session.Clock.NextTickIndex;
            string started = DateTime.UtcNow.ToString("o", Invariant);
            for (int i = 0; i < MeasuredTicks; i++)
            {
                long begin = Stopwatch.GetTimestamp(); a.Tick(WarmupTicks + i);
                durations[i] = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
                for (int t = 0; t < peak.Length; t++) peak[t] = Math.Max(peak[t], tables[t].Count);
            }
            uint last = session.Clock.NextTickIndex;
            var stats = session.Pipeline.Stats;
            // PipelineStats is an EMA with history since session construction, not a percentile/window mean.
            float schedule = stats.ScheduleMs, wait = stats.SyncWaitMs, wall = stats.TickWallMs;
            long pipelineTicks = stats.TickCount;
            int input = WarmupTicks + MeasuredTicks;
            Action allocatedWindow = () => { for (int i = 0; i < MeasuredTicks; i++) a.Tick(input + i); };
            ManagedAllocationCalibration before, after; ManagedAllocationSample allocation;
            uint allocationFirst = session.Clock.NextTickIndex;
            using (var probe = new ManagedAllocationProbe())
            {
                before = probe.Calibrate(); allocation = probe.Measure(allocatedWindow); after = probe.Calibrate();
            }
            if (allocation.Value < 0) throw new InvalidOperationException("Invalid negative allocation counter.");
            var text = new StringBuilder(4096); text.Append('{');
            Number(text, "schema_version", 1); String(text, "kind", "simulation-probe");
            String(text, "mode", a.Name); String(text, "configuration", configuration); String(text, "orientation_intent", orientation);
            String(text, "session", a.Name + "/seed-" + world.Seed.ToString(Invariant));
            String(text, "backend", "simulation-only/no-renderer");
#if SPF_DOTNET_HARNESS
            String(text, "runtime", "dotnet-unity-stubs");
#else
            String(text, "runtime", "unity-native-editmode");
#endif
            String(text, "platform", Environment.OSVersion.ToString()); String(text, "started_utc", started);
            String(text, "code_commit", Environment.GetEnvironmentVariable("SPF_ACCEPTANCE_COMMIT") ?? "unrecorded");
            String(text, "code_tree", Environment.GetEnvironmentVariable("SPF_ACCEPTANCE_TREE") ?? "unrecorded");
            Number(text, "seed", world.Seed); Number(text, "tick_rate", session.Clock.TickRate);
            Number(text, "warmup_ticks", WarmupTicks); Number(text, "first_tick", first); Number(text, "end_tick_exclusive", last);
            String(text, "timing_source", "Stopwatch around fixed input + SimSession.Step; input and sync included; no render or frame pacing");
            text.Append("\"tick_ms\":["); for (int i = 0; i < durations.Length; i++) { if (i > 0) text.Append(','); text.Append(durations[i].ToString("R", Invariant)); } text.Append("],");
            text.Append("\"pipeline\":{");
            String(text, "source", "PipelineStats, EMA alpha 0.1 since construction; ordinary pipelined execution, not worker CPU time");
            Number(text, "completed_ticks", pipelineTicks); Number(text, "schedule_ema_ms", schedule); Number(text, "sync_wait_ema_ms", wait); Number(text, "tick_wall_ema_ms", wall, false); text.Append("},");
            text.Append("\"allocation\":{"); String(text, "source", "ManagedAllocationProbe; current creating thread; synchronous fixed input + Step window; controls outside measured window");
            String(text, "metric", allocation.Metric.ToString()); Number(text, "thread_id", Thread.CurrentThread.ManagedThreadId);
            Number(text, "first_tick", allocationFirst); Number(text, "end_tick_exclusive", session.Clock.NextTickIndex);
            Number(text, "value", allocation.Value); Number(text, "gen0_process_collections", allocation.Collections);
            Number(text, "before_retained", before.RetainedArrays.Value); Number(text, "before_empty", before.Empty.Value);
            Number(text, "after_retained", after.RetainedArrays.Value); Number(text, "after_empty", after.Empty.Value, false); text.Append("},");
            text.Append("\"tables\":[");
            for (int t = 0; t < tables.Count; t++)
            {
                if (t > 0) text.Append(','); text.Append('{'); String(text, "name", tables[t].Key.Name);
                Number(text, "capacity", tables[t].Capacity); Number(text, "peak_after_tick", peak[t]); Number(text, "final_count", tables[t].Count, false); text.Append('}');
            }
            text.Append("],"); Number(text, "world_create_failures", world.CreateFailures);
            Number(text, "destroy_queue_overflow", world.Resource(SimWorld.DestroyQueueKey).TotalOverflow);
            String(text, "counter_window", "Cumulative since factory construction, read after timing and allocation windows; peak_after_tick is timing window only, not sub-tick pool high-water");
            String(text, "presentation_scope", a.PresentationScope); String(text, "unsupported_views", a.UnsupportedViews, false);
            return text.Append('}').ToString();
        }
        static void Number(StringBuilder b, string key, double value, bool comma = true)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("Non-finite measurement: " + key);
            Quote(b, key); b.Append(':').Append(value.ToString("R", Invariant)); if (comma) b.Append(',');
        }
        static void String(StringBuilder b, string key, string value, bool comma = true)
        { Quote(b, key); b.Append(':'); Quote(b, value); if (comma) b.Append(','); }
        static void Quote(StringBuilder b, string value)
        {
            b.Append('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4", Invariant));
                else b.Append(c);
            }
            b.Append('"');
        }
    }
}
