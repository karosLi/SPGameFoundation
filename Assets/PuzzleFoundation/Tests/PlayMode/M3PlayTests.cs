using System.Collections;
using System.IO;
using NUnit.Framework;
using PuzzleFoundation.Game;
using SPF.Presentation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PuzzleFoundation.Tests.PlayMode
{
    public class M3PlayTests
    {
        [UnityTest]
        public IEnumerator MovesAnimateBeforeTheNextAndUndoRestores([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = M3GameBootstrap.Create();
            var target = new RenderTexture(720, 1280, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(720, 1280, TextureFormat.RGBA32, false);
            try
            {
                yield return null;
                UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.Board.Flow == M3Flow.Playing && !game.InputLocked, 5f);
                yield return null;
                uint ticks = game.Session.Clock.NextTickIndex;
                yield return UIDriver.WaitSeconds(0.5f);
                Assert.AreEqual(ticks, game.Session.Clock.NextTickIndex, "no ticks without moves");

                game.Session.Sync();
                Assert.IsTrue(M3Rules.FindMove(game.Board, out var move));
                Assert.IsTrue(game.TrySwap(move.Cell, move.Direction));
                Assert.IsFalse(game.TrySwap(move.Cell, move.Direction), "input waits for the move");
                yield return UIDriver.WaitUntil(() => game.Renderer.Busy, 2f);
                Assert.IsTrue(game.Renderer.Busy, "the move animates");
                yield return UIDriver.WaitUntil(() => !game.InputLocked, 5f);
                yield return null;
                Assert.Greater(game.Board.Score, 0);
                Assert.AreEqual(64, game.Renderer.LiveGems, "a full board again after the cascade");

                game.CameraLetterbox(target);
                yield return null;
                yield return null;
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 720, 1280), 0, 0);
                read.Apply(false);
                RenderTexture.active = previous;
                game.Camera.targetTexture = null;
                string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"puzzle-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}.png"), read.EncodeToPNG());
                int bright = 0;
                var pixels = read.GetPixels32();
                for (int i = 0; i < pixels.Length; i += 7) if (math.max((int)pixels[i].r, math.max((int)pixels[i].g, (int)pixels[i].b)) > 180) bright++;
                Assert.Greater(bright, pixels.Length / 7 / 20, "gems are drawn");

                UIDriver.Click(game.UndoButton.gameObject);
                yield return null;
                game.Session.Sync();
                Assert.AreEqual(0, game.Board.Score, "undo restored the board");
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
