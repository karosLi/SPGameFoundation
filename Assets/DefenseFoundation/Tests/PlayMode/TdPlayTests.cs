using System.Collections;
using System.IO;
using NUnit.Framework;
using DefenseFoundation.Game;
using SPF.Presentation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace DefenseFoundation.Tests.PlayMode
{
    public class TdPlayTests
    {
        [UnityTest]
        public IEnumerator BuildThroughTheUiAndDefend([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = TdGameBootstrap.Create();
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                yield return null;
                UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == TdFlow.Building, 5f);
                yield return null;
                int routeBefore = game.Renderer.PathLength;
                Assert.Greater(routeBefore, 1, "route preview drawn");

                // Tap-select tiles and build through the build panel.
                foreach (var cell in new[] { new int2(8, 7), new int2(10, 6), new int2(12, 8) })
                {
                    game.Select(cell);
                    yield return null;
                    Assert.IsTrue(game.Hud.BuildPanel.gameObject.activeInHierarchy, "build panel for empty ground");
                    UIDriver.Click(game.Hud.BuildButtons[(int)TowerKind.Arrow].gameObject);
                    yield return UIDriver.WaitUntil(() => game.Session.World.Resource(TdKeys.Map)[cell] == TdTile.Tower, 2f);
                }
                Assert.AreEqual(3, game.Session.World.Table(TdKeys.Tower).Count);
                game.Select(new int2(8, 7));
                yield return null;
                Assert.IsTrue(game.Hud.TowerPanel.gameObject.activeInHierarchy, "tower panel for a tower");

                UIDriver.Click(game.Hud.NextWaveButton.gameObject);
                float end = Time.realtimeSinceStartup + 40f;
                while (game.State.Kills < 3 && Time.realtimeSinceStartup < end) yield return null;
                Assert.GreaterOrEqual(game.State.Kills, 3, "towers shot enemies");

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
                File.WriteAllBytes(Path.Combine(dir, $"defense-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}.png"), read.EncodeToPNG());
                int green = 0;
                var pixels = read.GetPixels32();
                for (int i = 0; i < pixels.Length; i += 7) if (pixels[i].g > 110 && pixels[i].r < 110) green++;
                Assert.Greater(green, pixels.Length / 7 / 10, "the grass board is drawn");
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
