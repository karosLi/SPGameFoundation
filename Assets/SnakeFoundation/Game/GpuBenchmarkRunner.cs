using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SPF.Presentation;
using SPF.Runtime.Diagnostics;
using UnityEngine;

namespace SnakeFoundation.Game
{
    /// <summary>
    /// GPU A/B benchmark for the render options: cycles through variants (all optimisations on, each one
    /// switched off in turn, 8-segment discs) in interleaved rounds so slow drift (thermal throttling,
    /// background load) hits every variant alike, and measures GPU / CPU frame time with
    /// <see cref="GpuFrameTimer"/>. Results are shown on screen, logged, and written to
    /// <c>persistentDataPath/gpu-benchmark.txt</c>.
    /// <para>
    /// On a device: menu SPF/Snake/Build GPU Benchmark (development build with SPF_GPU_BENCH, which adds
    /// this component at start). In the editor: PlayMode test GpuFrameTimeAB.
    /// </para>
    /// </summary>
    public sealed class GpuBenchmarkRunner : MonoBehaviour
    {
        public SnakeGameBootstrap Game;
        public float WarmupSeconds = 1.5f;
        public float MeasureSeconds = 4f;
        public int Rounds = 3;

        struct Variant
        {
            public string Name;
            public System.Action<SnakeGameBootstrap, bool> Apply;   // (game, on)
            public double GpuSum, CpuSum;
            public int Measured, GpuSamples, Frames;
        }

        readonly GpuFrameTimer m_Timer = new GpuFrameTimer();
        readonly List<Variant> m_Variants = new List<Variant>();
        readonly StringBuilder m_Report = new StringBuilder();
        string m_Status = "starting";
        bool m_Measuring;
        int m_Frames;

        public bool IsDone { get; private set; }
        public string Report => m_Report.ToString();

        IEnumerator Start()
        {
            if (Game == null) Game = GetComponent<SnakeGameBootstrap>();
            yield return null;
            BuildVariants();
            Game.StartGame();
            yield return Wait(3f);   // let the population gather around the player

            for (int round = 0; round < Rounds; round++)
                for (int v = 0; v < m_Variants.Count; v++)
                {
                    var variant = m_Variants[v];
                    variant.Apply(Game, true);
                    m_Status = $"round {round + 1}/{Rounds}: {variant.Name}";
                    yield return Wait(WarmupSeconds);
                    m_Timer.Reset();
                    m_Frames = 0;
                    m_Measuring = true;
                    yield return Wait(MeasureSeconds);
                    m_Measuring = false;
                    variant.GpuSum += m_Timer.GpuMs;
                    variant.CpuSum += m_Timer.CpuMs;
                    variant.GpuSamples += m_Timer.GpuSamples;
                    variant.Frames += m_Frames;
                    variant.Measured++;
                    variant.Apply(Game, false);
                    m_Variants[v] = variant;
                }

            WriteReport();
            m_Status = "done";
            IsDone = true;
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        void Update()
        {
            m_Timer.Sample();
            if (m_Measuring) m_Frames++;
        }

        void BuildVariants()
        {
            var tier = Game.WorldRenderer.Tier;
            m_Variants.Add(new Variant { Name = "all optimisations on", Apply = (g, on) => { } });
            if (tier == RenderTier.GpuDriven)
                m_Variants.Add(new Variant
                {
                    Name = "opaque node compaction off",
                    Apply = (g, on) => { if (g.WorldRenderer.Chains != null) g.WorldRenderer.Chains.CompactOpaqueNodes = !on; },
                });
            else
                m_Variants.Add(new Variant { Name = "page prefix submeshes off", Apply = (g, on) => CircleBatch.UsePrefixSubmeshes = !on });
            m_Variants.Add(new Variant
            {
                Name = "8-segment discs",
                Apply = (g, on) => g.Session.World.Resource(SnakeKeys.Quality).DiscSegments = on ? 8 : 16,
            });
        }

        void WriteReport()
        {
            var r = m_Report.Clear();
            r.AppendLine($"=== GPU benchmark ({Game.WorldRenderer.Tier}, {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}) ===");
            r.AppendLine($"device: {SystemInfo.deviceModel}, {Screen.width}x{Screen.height}, frame timing enabled: {GpuFrameTimer.Enabled}");
            r.AppendLine($"{Rounds} interleaved rounds x {MeasureSeconds:F1} s per variant; visible snakes {Game.WorldRenderer.LastVisibleSnakes}");
            double baseline = 0;
            for (int v = 0; v < m_Variants.Count; v++)
            {
                var x = m_Variants[v];
                double gpu = x.Measured > 0 ? x.GpuSum / x.Measured : 0, cpu = x.Measured > 0 ? x.CpuSum / x.Measured : 0;
                if (v == 0) baseline = gpu;
                // Below ~0.01 ms the platform is not really reporting GPU time (or the scene is trivial for it).
                string delta = v > 0 && baseline > 0.01 && x.GpuSamples > 0 ? $"  ({(gpu / baseline - 1.0):+0.0%;-0.0%} vs all on)" : "";
                r.AppendLine($"{x.Name,-28} GPU {(x.GpuSamples > 0 ? gpu.ToString("F3") + " ms" : "n/a"),-10} ({x.GpuSamples} samples)  CPU {cpu:F3} ms  frames {x.Frames}{delta}");
            }
            if (m_Variants.Count > 0 && m_Variants[0].GpuSamples == 0)
                r.AppendLine("GPU timings unavailable on this platform / build (enable Frame Timing Stats in Player settings).");
            Debug.Log(r.ToString());
            try { File.WriteAllText(Path.Combine(Application.persistentDataPath, "gpu-benchmark.txt"), r.ToString()); }
            catch (IOException) { }
        }

        GUIStyle m_Style;

        void OnGUI()
        {
            if (m_Style == null)
                m_Style = new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(14, Screen.height / 45), alignment = TextAnchor.UpperLeft };
            var style = m_Style;
            GUI.Box(new Rect(10, 10, Screen.width * 0.6f, Screen.height * (IsDone ? 0.4f : 0.08f)), IsDone ? Report : "GPU benchmark: " + m_Status, style);
        }
    }
}
