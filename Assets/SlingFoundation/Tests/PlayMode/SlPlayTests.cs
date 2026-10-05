using System.Collections;
using System.IO;
using NUnit.Framework;
using SlingFoundation.Game;
using SPF.Presentation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SlingFoundation.Tests.PlayMode
{
    public class SlPlayTests
    {
        [UnityTest]
        public IEnumerator LaunchToppleAndRender([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = SlGameBootstrap.Create();
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                yield return null;
                UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SlFlow.Aiming, 5f);
                yield return UIDriver.WaitSeconds(1f);
                int targets = game.State.TargetsLeft;
                Assert.Greater(targets, 0);

                game.Launch(new float2(-2.4f, -0.4f));
                yield return UIDriver.WaitUntil(() => game.State.Flow == SlFlow.Flying, 2f);
                yield return UIDriver.WaitSeconds(0.5f);
                game.Session.Sync();
                Assert.Greater(game.Renderer.BodiesDrawn, 5);

                // Mid-collapse: GC per frame while the physics job and the renderer run.
                game.Governor.ResetGcStats();
                for (int f = 0; f < 90; f++) yield return null;
                if (game.Governor.GcCounterValid)
                {
                    GcReport.Write($"sling collapse ({tier})", game.Governor.FramesSinceReset, game.Governor.GcFramesSinceReset, game.Governor.GcBytesSinceReset);
                    Assert.LessOrEqual(game.Governor.GcFramesSinceReset, 2, "physics + rendering allocate nothing per frame");
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
                File.WriteAllBytes(Path.Combine(dir, $"sling-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}.png"), read.EncodeToPNG());
                int wood = 0;
                var pixels = read.GetPixels32();
                for (int i = 0; i < pixels.Length; i += 5) if (pixels[i].r > 140 && pixels[i].g > 90 && pixels[i].g < 170 && pixels[i].b < 110) wood++;
                Assert.Greater(wood, 20, "wooden pieces are drawn");

                yield return UIDriver.WaitUntil(() => game.State.Flow != SlFlow.Flying, 15f);
                game.Session.Sync();
                TestContext.WriteLine($"sling ({tier}): targets {targets} -> {game.State.TargetsLeft}, score {game.State.Score}, flow {game.State.Flow}");
                Assert.Less(game.State.TargetsLeft, targets, "the shot downed a target");
                Assert.AreEqual(SlFlow.Aiming, game.State.Flow);
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
