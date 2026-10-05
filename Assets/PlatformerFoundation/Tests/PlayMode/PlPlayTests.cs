using System.Collections;
using System.IO;
using NUnit.Framework;
using PlatformerFoundation.Game;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PlatformerFoundation.Tests.PlayMode
{
    public class PlPlayTests
    {
        [UnityTest]
        public IEnumerator PlayRunJumpAndStaticTiles([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = PlGameBootstrap.Create(ui: true);
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                yield return null;
                UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == PlFlow.Playing, 5f);
                yield return null;
                yield return null;
                int uploads = game.Renderer.TileUploads;   // the menu's empty map, then the level
                float x0 = game.State.Hero.x;
                // Run right, jumping now and then (jump button through the touch UI).
                float end = Time.realtimeSinceStartup + 3f;
                int frame = 0;
                game.Script = () => new InputFrame { Move = new float2(1f, 0f) };
                while (Time.realtimeSinceStartup < end)
                {
                    if (frame % 40 == 0) UIDriver.Press(game.Hud.JumpButton.gameObject);
                    if (frame % 40 == 15) UIDriver.Release(game.Hud.JumpButton.gameObject);
                    frame++;
                    yield return null;
                }
                game.Session.Sync();
                Assert.Greater(game.State.Hero.x, x0 + 3f, "ran right");
                Assert.AreEqual(uploads, game.Renderer.TileUploads, "the static tile layer is not re-uploaded while playing");
                Assert.Greater(game.Renderer.TileSprites, 200);

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
                File.WriteAllBytes(Path.Combine(dir, $"platformer-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}.png"), read.EncodeToPNG());
                var pixels = read.GetPixels32();
                int ground = 0;
                for (int i = 0; i < pixels.Length; i += 7)
                {
                    var p = pixels[i];
                    if (p.r > 90 && p.g < 100 && p.b < 70) ground++;   // brown earth tiles
                }
                Assert.Greater(ground, pixels.Length / 7 / 50, "tiles are drawn");
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
