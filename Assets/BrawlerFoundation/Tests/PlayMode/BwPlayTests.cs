using System.Collections;
using System.IO;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BrawlerFoundation.Tests.PlayMode
{
    public class BwPlayTests
    {
        [UnityTest]
        public IEnumerator FightThroughTheUiAndRenderSkeletons([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = BwGameBootstrap.Create();
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                yield return null;
                UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5f);
                // Walk right into the enemies, punching through the touch button.
                int frame = 0;
                game.Script = () => new InputFrame { Move = new float2(1f, 0f) };
                float end = Time.realtimeSinceStartup + 8f;
                while (game.State.Score == 0 && Time.realtimeSinceStartup < end)
                {
                    if (frame++ % 20 == 0) UIDriver.Click(game.PunchButton.gameObject);
                    yield return null;
                }
                game.Session.Sync();
                Assert.Greater(game.State.Score, 0, "punches landed");
                Assert.Greater(game.Renderer.PartsDrawn, 20, "skeletal parts drawn");

                // Holding still: GC per frame with three skeletal fighters animating.
                game.Script = () => default;
                game.Governor.ResetGcStats();
                for (int f = 0; f < 120; f++) yield return null;
                if (game.Governor.GcCounterValid)
                {
                    GcReport.Write($"brawler ({tier})", game.Governor.FramesSinceReset, game.Governor.GcFramesSinceReset, game.Governor.GcBytesSinceReset);
                    Assert.LessOrEqual(game.Governor.GcFramesSinceReset, 2, "skeletal animation and rendering allocate nothing per frame");
                }

                game.CameraRig.Camera.targetTexture = target;
                yield return null;
                yield return null;
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                read.Apply(false);
                RenderTexture.active = previous;
                game.CameraRig.Camera.targetTexture = null;
                string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"brawler-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}.png"), read.EncodeToPNG());
                int shirts = 0;
                var pixels = read.GetPixels32();
                for (int i = 0; i < pixels.Length; i += 3)
                {
                    var p = pixels[i];
                    if (p.b > 150 && p.r < 110) shirts++;   // the player's blue shirt
                    else if (p.r > 150 && p.g < 90) shirts++; // red enemies
                }
                Assert.Greater(shirts, 30, "fighters' parts are drawn");
            }
            finally
            {
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
                target.Release();
                Object.Destroy(target);
                Object.Destroy(read);
            }
        }
    }
}
