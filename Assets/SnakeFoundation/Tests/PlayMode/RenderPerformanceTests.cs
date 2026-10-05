using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using SnakeFoundation.Game;
using SPF.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SnakeFoundation.Tests.PlayMode
{
    /// <summary>
    /// Main-thread cost of the world renderer with chain-data reuse off / on, on both tiers, with the
    /// production population at 60 fps (two frames per 30 Hz tick, as on a phone). Writes
    /// Artifacts/perf-render-&lt;tier&gt;.txt; asserts only sanity, timings depend on the machine.
    /// </summary>
    [Category("Performance")]
    public class RenderPerformanceTests
    {
        const float MeasureSeconds = 4f;

        [UnityTest]
        public IEnumerator RendererCpuWithAndWithoutReuse([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier) =>
            Measure(tier, null, $"perf-render-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}.txt", "production population");

        /// <summary>
        /// Late-game scene: 60 thick AI snakes (mass 800-3000) mostly near the player, with fixed 0.4 m trail
        /// spacing vs spacing scaled with radius (fewer trail points to sample, upload and strip).
        /// </summary>
        [UnityTest]
        public IEnumerator BigSnakeRendererCpu([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier, [Values(0f, 0.25f)] float spacingPerRadius) =>
            Measure(tier, c =>
                {
                    c.AI.SnakesPerRegion = 60;
                    c.AI.StartMassMin = 800f;
                    c.AI.StartMassMax = 3000f;
                    c.AI.MaxMass = 3000f;
                    c.AI.SpawnNearFocusRatio = 0.8f;
                    c.Body.TrailSpacingPerRadius = spacingPerRadius;
                },
                $"perf-render-bigsnakes-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}-spacing{spacingPerRadius * 100:0}.txt",
                $"60 big snakes, trail spacing per radius {spacingPerRadius}");

        static IEnumerator Measure(RenderTier tier, System.Action<SnakeConfig> tweak, string fileName, string scene)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders)
                Assert.Ignore("No compute shader support");

            int previousRate = Application.targetFrameRate;
            int previousVSync = QualitySettings.vSyncCount;
            RenderCapabilities.Override = tier;
            var config = SnakeConfig.CreateDefault();
            tweak?.Invoke(config);
            var game = SnakeGameBootstrap.Create(config, seed: 99, ui: false);
            try
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                if (game.Quality != null)
                {
                    game.Quality.SetLevel(game.Session, 0);
                    game.Quality.enabled = false;
                }
                game.StartGame();
                yield return UIDriver.WaitSeconds(2f);

                var renderer = game.WorldRenderer;
                var report = new StringBuilder();
                report.AppendLine($"=== renderer CPU ({tier}), {scene} ===");
                double[] perFrame = new double[2];
                for (int pass = 0; pass < 2; pass++)
                {
                    bool reuse = pass == 1;
                    renderer.ReuseBetweenTicks = reuse;
                    yield return null;
                    renderer.ResetCpuStats();
                    yield return UIDriver.WaitSeconds(MeasureSeconds);
                    perFrame[pass] = renderer.CpuMsTotal / System.Math.Max(renderer.CpuFrames, 1);
                    report.AppendLine($"reuse {(reuse ? "on " : "off")}: {perFrame[pass]:F3} ms/frame over {renderer.CpuFrames} frames " +
                                      $"(rebuilds {renderer.ChainRebuilds}, reuses {renderer.ChainReuses}), visible snakes {renderer.LastVisibleSnakes}");
                    Assert.Greater(renderer.CpuFrames, 10);
                    if (reuse) Assert.Greater(renderer.ChainReuses, 0, "frames without a tick reused chain data");
                }
                report.AppendLine($"saving: {(1.0 - perFrame[1] / System.Math.Max(perFrame[0], 1e-9)):P1}");
                TestContext.WriteLine(report.ToString());
                string dir = Path.Combine(Application.dataPath, "..", "Artifacts");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, fileName), report.ToString());
            }
            finally
            {
                Application.targetFrameRate = previousRate;
                QualitySettings.vSyncCount = previousVSync;
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
                Object.Destroy(config);
            }
        }
    }
}
