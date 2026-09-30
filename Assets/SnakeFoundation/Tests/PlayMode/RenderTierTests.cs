using System.Collections;
using NUnit.Framework;
using SnakeFoundation.Game;
using SPF.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

namespace SnakeFoundation.Tests.PlayMode
{
    /// <summary>Both render tiers must run the full game without errors (GPU-driven only where compute is supported).</summary>
    public class RenderTierTests
    {
        [UnityTest]
        public IEnumerator DataTextureTierRuns() => Run(RenderTier.DataTexture);

        [UnityTest]
        public IEnumerator GpuDrivenTierRuns()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("No compute shader support on this device / in batch mode");
            return Run(RenderTier.GpuDriven);
        }

        static IEnumerator Run(RenderTier tier)
        {
            RenderCapabilities.Override = tier;
            var config = SnakeConfig.CreateDefault();
            config.AI.SnakesPerRegion = 60;
            var game = SnakeGameBootstrap.Create(config, seed: 77, ui: true);
            try
            {
                game.StartGame();
                for (int i = 0; i < 180; i++) yield return null;
                Assert.AreEqual(tier, game.WorldRenderer.Tier);
                Assert.Greater(game.WorldRenderer.LastVisibleSnakes, 0);
                Assert.Greater(game.WorldRenderer.LastFoodDrawn, 0);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
                Object.Destroy(config);
            }
        }
    }
}
