using System.Collections;
using System.IO;
using NUnit.Framework;
using SnakeFoundation.Game;
using SPF.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SnakeFoundation.Tests.PlayMode
{
    /// <summary>
    /// Runs <see cref="GpuBenchmarkRunner"/> briefly on each tier with the late-game big-snake scene and
    /// writes Artifacts/perf-gpu-&lt;tier&gt;.txt. GPU timings depend on the platform reporting them; the test
    /// only asserts that the run completed.
    /// </summary>
    [Category("Performance")]
    public class GpuBenchmarkTests
    {
        [UnityTest]
        public IEnumerator GpuFrameTimeAB([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders)
                Assert.Ignore("No compute shader support");

            int previousVSync = QualitySettings.vSyncCount;
            RenderCapabilities.Override = tier;
            var config = SnakeConfig.CreateDefault();
            config.AI.SnakesPerRegion = 60;
            config.AI.StartMassMin = 800f;
            config.AI.StartMassMax = 3000f;
            config.AI.MaxMass = 3000f;
            config.AI.SpawnNearFocusRatio = 0.8f;
            var game = SnakeGameBootstrap.Create(config, seed: 77, ui: false);
            try
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                if (game.Quality != null)
                {
                    game.Quality.SetLevel(game.Session, 0);
                    game.Quality.enabled = false;
                }
                var runner = game.gameObject.AddComponent<GpuBenchmarkRunner>();
                runner.Game = game;
                runner.WarmupSeconds = 1f;
                runner.MeasureSeconds = 2f;
                runner.Rounds = 2;
                float timeout = Time.realtimeSinceStartup + 120f;
                while (!runner.IsDone && Time.realtimeSinceStartup < timeout) yield return null;
                Assert.IsTrue(runner.IsDone, "benchmark finished");
                TestContext.WriteLine(runner.Report);
                string dir = Path.Combine(Application.dataPath, "..", "Artifacts");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"perf-gpu-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}.txt"), runner.Report);
            }
            finally
            {
                QualitySettings.vSyncCount = previousVSync;
                RenderCapabilities.Override = null;
                CircleBatch.UsePrefixSubmeshes = true;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
                Object.Destroy(config);
            }
        }
    }
}
