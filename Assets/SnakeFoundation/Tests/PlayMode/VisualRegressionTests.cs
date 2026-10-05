using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.Testing;
using SnakeFoundation.Game;
using SPF.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SnakeFoundation.Tests.PlayMode
{
    /// <summary>
    /// GPU A/B pixel tests: the game is frozen (time scale 0: no ticks, no camera motion, no animation),
    /// the camera renders into a RenderTexture, and the same frame is captured with a renderer option off
    /// and on. Every test first checks that two captures of the same variant are identical, so any
    /// difference is caused by the option. Captures are written to Artifacts/Screenshots (CI publishes
    /// them to the ci-screenshots/&lt;branch&gt; branch).
    /// </summary>
    public class VisualRegressionTests
    {
        const int Width = 960, Height = 540;

        SnakeGameBootstrap m_Game;
        SnakeConfig m_Config;
        RenderTexture m_Target;
        Texture2D m_Read;

        IEnumerator Setup(RenderTier tier)
        {
            RenderCapabilities.Override = tier;
            m_Config = SnakeConfig.CreateDefault();
            m_Config.AI.SnakesPerRegion = 60;
            m_Game = SnakeGameBootstrap.Create(m_Config, seed: 4242, ui: false);
            m_Target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            m_Target.Create();
            m_Read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            m_Game.CameraRig.Camera.targetTexture = m_Target;
            // Adaptive quality reacts to (slow, batch-mode) frame times and would change node stride,
            // translucency budget, disc detail and render scale between captures: pin level 0.
            if (m_Game.Quality != null)
            {
                m_Game.Quality.SetLevel(m_Game.Session, 0);
                m_Game.Quality.enabled = false;
            }
            m_Game.StartGame();
            yield return UIDriver.WaitSeconds(3f);      // AI gathers around the player
            Time.timeScale = 0f;                       // freeze simulation and animations
            // The camera follows with unscaled time and would keep creeping sub-pixel towards its target;
            // snapping puts it exactly on the (now constant) target, where it stays.
            m_Game.CameraRig.Snap();
            for (int i = 0; i < 5; i++) yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            RenderCapabilities.Override = null;
            if (m_Game != null)
            {
                if (m_Game.CameraRig != null) m_Game.CameraRig.Camera.targetTexture = null;
                Object.Destroy(m_Game.gameObject);
            }
            if (m_Target != null) { m_Target.Release(); Object.Destroy(m_Target); }
            if (m_Read != null) Object.Destroy(m_Read);
            if (m_Config != null) Object.Destroy(m_Config);
        }

        /// <summary>
        /// Reads back a frame rendered with the current options and, when named, writes it to
        /// Artifacts/Screenshots straight from the readback texture.
        /// </summary>
        IEnumerator Capture(Color32[][] slot, int index, string saveAs = null)
        {
            // WaitForEndOfFrame never resumes in batch mode. The camera renders into the target after
            // LateUpdate, so a frame later the target holds a frame rendered with the current options;
            // the scene is frozen, so waiting two frames is safe.
            m_Game.CameraRig.Snap();
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = m_Target;
            m_Read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            m_Read.Apply(false);
            RenderTexture.active = previous;
            slot[index] = m_Read.GetPixels32();
            if (saveAs != null)
            {
                string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, saveAs + ".png"), m_Read.EncodeToPNG());
            }
        }

        static (int differing, int maxDelta, RectInt box) Compare(Color32[] a, Color32[] b, int tolerance)
        {
            int differing = 0, maxDelta = 0;
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Mathf.Max(Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g)), Mathf.Abs(a[i].b - b[i].b));
                if (d > maxDelta) maxDelta = d;
                if (d > tolerance)
                {
                    differing++;
                    int x = i % Width, y = i / Width;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            }
            var box = differing > 0 ? new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1) : default;
            return (differing, maxDelta, box);
        }

        /// <summary>
        /// Long opaque node snakes whose heads sit just outside the left / right / top edge of the view, so
        /// most of their nodes are off-screen: the case compaction exists for.
        /// </summary>
        void SpawnSnakesCrossingTheViewEdges()
        {
            var session = m_Game.Session;
            session.Sync();
            var world = session.World;
            var runtime = world.Resource(SnakeKeys.Config);
            var game = world.Resource(SnakeKeys.Game);
            var view = m_Game.CameraRig.ViewRect;
            var center = (view.xy + view.zw) * 0.5f;
            var half = (view.zw - view.xy) * 0.5f;
            var random = new Unity.Mathematics.Random(9);
            // Skins 0 (Sky) and 2 (Candy, striped) are opaque node skins.
            var spawns = new[]
            {
                (center + new Unity.Mathematics.float2(half.x + 6f, half.y * 0.5f), new Unity.Mathematics.float2(1, 0), 0),
                (center + new Unity.Mathematics.float2(-half.x * 0.3f, half.y + 6f), new Unity.Mathematics.float2(0, 1), 2),
                (center + new Unity.Mathematics.float2(-half.x - 6f, -half.y * 0.5f), new Unity.Mathematics.float2(-1, 0), 0),
            };
            foreach (var (head, heading, skin) in spawns)
            {
                var handle = SnakeSpawner.SpawnSnake(world, runtime, game, game.ActiveRegion, head, heading, 400f, default, skin, ref random);
                Assert.IsFalse(handle.IsNull, "spawned a long snake across the view edge");
            }
        }

        /// <summary>Guards against vacuous passes: the capture must contain a rendered scene, not a cleared target.</summary>
        static void AssertHasContent(Color32[] pixels)
        {
            Assert.IsNotNull(pixels, "frame was captured");
            var first = pixels[0];
            int other = 0;
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].r != first.r || pixels[i].g != first.g || pixels[i].b != first.b) other++;
            Assert.Greater(other, pixels.Length / 20, "the captured frame shows the scene (camera rendered into the target)");
        }

        static void SkipWithoutGpu(RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (batch mode with -nographics)");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders)
                Assert.Ignore("No compute shader support");
        }

        /// <summary>
        /// Opaque node compaction drops off-screen nodes and changes their order, which must not change a
        /// single pixel: opaque nodes have unique depths, so the depth test decides visibility.
        /// </summary>
        [UnityTest]
        public IEnumerator OpaqueNodeCompactionIsPixelIdentical()
        {
            SkipWithoutGpu(RenderTier.GpuDriven);
            yield return Setup(RenderTier.GpuDriven);
            var chains = m_Game.WorldRenderer.Chains;
            if (!chains.CompactionAvailable)
                Assert.Ignore("Raw indirect arguments not supported on this graphics API");
            SpawnSnakesCrossingTheViewEdges();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.Greater(chains.NodeCount(BlendKind.Opaque), 0, "scene contains opaque node chains");

            var frames = new Color32[3][];
            chains.CompactOpaqueNodes = false;
            yield return Capture(frames, 0, "compaction-off");
            yield return Capture(frames, 1, "compaction-off-repeat");
            chains.CompactOpaqueNodes = true;
            yield return Capture(frames, 2, "compaction-on");

            AssertHasContent(frames[0]);
            var baseline = Compare(frames[0], frames[1], 0);
            Assert.AreEqual(0, baseline.differing, $"frozen scene renders identically twice (test is stable); differing box {baseline.box}, max delta {baseline.maxDelta}");
            var result = Compare(frames[0], frames[2], 0);
            TestContext.WriteLine($"compaction: differing pixels {result.differing}, max delta {result.maxDelta}");
            Assert.AreEqual(0, result.differing, $"compaction changed {result.differing} pixels (max delta {result.maxDelta})");
        }

        /// <summary>
        /// Frames without a tick reuse the culled / sorted chain data (and, on the GPU tier, the uploaded
        /// headers): the frozen frame must look exactly like one rebuilt from scratch.
        /// </summary>
        [UnityTest]
        public IEnumerator ReusingChainDataBetweenTicksIsPixelIdentical([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            SkipWithoutGpu(tier);
            yield return Setup(tier);
            var renderer = m_Game.WorldRenderer;
            string suffix = tier == RenderTier.GpuDriven ? "gpu" : "datatex";

            var frames = new Color32[3][];
            renderer.ReuseBetweenTicks = false;
            yield return Capture(frames, 0, "reuse-off-" + suffix);
            yield return Capture(frames, 1);
            renderer.ResetCpuStats();
            renderer.ReuseBetweenTicks = true;
            yield return Capture(frames, 2, "reuse-on-" + suffix);
            Assert.Greater(renderer.ChainReuses, 0, "frames without a tick reused chain data");
            Assert.AreEqual(0, renderer.ChainRebuilds, "no rebuild while frozen");

            AssertHasContent(frames[0]);
            var baseline = Compare(frames[0], frames[1], 0);
            Assert.AreEqual(0, baseline.differing, $"frozen scene renders identically twice; differing box {baseline.box}");
            var result = Compare(frames[0], frames[2], 0);
            TestContext.WriteLine($"reuse ({suffix}): differing pixels {result.differing}, max delta {result.maxDelta}");
            Assert.AreEqual(0, result.differing, $"reusing chain data changed {result.differing} pixels; box {result.box}");
        }

        /// <summary>
        /// 8-segment discs (adaptive quality level 2+) must look close to 16-segment ones: only disc
        /// silhouettes may move by a pixel or so. Both captures are published for visual review.
        /// </summary>
        [UnityTest]
        public IEnumerator EightSegmentDiscsStayCloseToSixteen([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            SkipWithoutGpu(tier);
            yield return Setup(tier);
            var quality = m_Game.Session.World.Resource(SnakeKeys.Quality);
            string suffix = tier == RenderTier.GpuDriven ? "gpu" : "datatex";

            var frames = new Color32[3][];
            quality.DiscSegments = 16;
            yield return Capture(frames, 0, "discs16-" + suffix);
            yield return Capture(frames, 1, "discs16-repeat-" + suffix);
            quality.DiscSegments = 8;
            yield return Capture(frames, 2, "discs8-" + suffix);
            quality.DiscSegments = 16;

            AssertHasContent(frames[0]);
            var baseline = Compare(frames[0], frames[1], 0);
            Assert.AreEqual(0, baseline.differing, $"frozen scene renders identically twice; differing box {baseline.box}, max delta {baseline.maxDelta}");
            var result = Compare(frames[0], frames[2], 24);
            float ratio = result.differing / (float)(Width * Height);
            TestContext.WriteLine($"discs 16 vs 8 ({suffix}): {result.differing} pixels differ by > 24/255 ({ratio:P2}), max delta {result.maxDelta}");
            Assert.Less(ratio, 0.04f, "8-segment discs differ from 16-segment ones only along silhouettes");
            Assert.Greater(result.differing, 0, "the segment count actually changed what was drawn");
        }

        /// <summary>
        /// Data-texture pages draw the smallest prefix submesh that covers the used discs instead of the
        /// whole page (unused discs have zero radius): the frame must not change.
        /// </summary>
        [UnityTest]
        public IEnumerator PagePrefixSubmeshesArePixelIdentical()
        {
            SkipWithoutGpu(RenderTier.DataTexture);
            yield return Setup(RenderTier.DataTexture);
            SpawnSnakesCrossingTheViewEdges();
            for (int i = 0; i < 3; i++) yield return null;

            var frames = new Color32[3][];
            try
            {
                CircleBatch.UsePrefixSubmeshes = false;
                yield return Capture(frames, 0, "prefix-off");
                yield return Capture(frames, 1);
                CircleBatch.UsePrefixSubmeshes = true;
                yield return Capture(frames, 2, "prefix-on");
            }
            finally
            {
                CircleBatch.UsePrefixSubmeshes = true;
            }

            AssertHasContent(frames[0]);
            var baseline = Compare(frames[0], frames[1], 0);
            Assert.AreEqual(0, baseline.differing, $"frozen scene renders identically twice; differing box {baseline.box}");
            var result = Compare(frames[0], frames[2], 0);
            TestContext.WriteLine($"page prefix submeshes: differing pixels {result.differing}, max delta {result.maxDelta}");
            Assert.AreEqual(0, result.differing, $"prefix submeshes changed {result.differing} pixels; box {result.box}");
        }
    }
}
