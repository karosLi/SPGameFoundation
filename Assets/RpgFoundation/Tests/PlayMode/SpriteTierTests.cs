using System.Collections;
using System.IO;
using NUnit.Framework;
using RpgFoundation.Presentation;
using RpgFoundation.Game;
using SPF.Contracts;
using SPF.Testing;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace RpgFoundation.Tests.PlayMode
{
    /// <summary>
    /// The sprite batch must draw the same image on both tiers (structured buffer vs data texture): a sheet
    /// of characters, weapons (rotated, mirrored), tinted and flashing sprites, rendered by each tier into a
    /// render texture and compared. Both captures go to Artifacts/Screenshots for review.
    /// </summary>
    public class SpriteTierTests
    {
        const int Width = 640, Height = 360;

        [UnityTest]
        public IEnumerator GameplayWalkRunCastPauseAndRebind([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("GPU tier unavailable");
            RenderCapabilities.Override = tier;
            string save = Path.Combine(Path.GetTempPath(), "rpg-sprite-motion-" + System.Guid.NewGuid().ToString("N"));
            RpgGameBootstrap.SaveDirectoryOverride = save;
            var config = RpgConfig.CreateDefault();
            var game = RpgGameBootstrap.Create(config, seed: 21, ui: false);
            var replacement = RpgMode.Create(config, out var modules);
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                yield return null;
                game.NewGame(21);
                yield return UIDriver.WaitUntil(() => game.State.Flow == RpgFlow.Playing, 5f);
                Assert.AreEqual(RpgFlow.Playing, game.State.Flow, "start timed out");
                game.Session.Sync();
                var world = game.Session.World;
                var handles = world.Table(RpgKeys.Actor).Handles;
                for (int i = world.Table(RpgKeys.Actor).Count - 1; i >= 0; i--)
                    if (world.Column(RpgKeys.Info)[i].Team == Team.Monsters) world.DestroyEntity(handles[i]);
                game.State.MonstersAlive = 0; game.State.BossAlive = false;
                // An obstacle-free fixture lets real analog input demonstrate sustained velocity.
                world.Resource(RpgKeys.Map).Fill(0);
                float move = 0f;
                game.InputRouter.Sink = f => game.State.Input = InputFrame.Latch(game.State.Input,
                    new InputFrame { Move = new float2(move, 0f) });
                game.CameraRig.Camera.targetTexture = target;
                yield return UIDriver.WaitUntil(() => CurrentClip() == CharacterClip.Idle, 3f);
                yield return Capture("idle", CharacterClip.Idle);
                move = 0.35f;
                yield return UIDriver.WaitUntil(() => CurrentClip() == CharacterClip.Walk, 3f);
                yield return Capture("walk", CharacterClip.Walk);
                move = 1f;
                yield return UIDriver.WaitUntil(() => CurrentClip() == CharacterClip.Run, 3f);
                yield return Capture("run", CharacterClip.Run);
                game.Session.Pause();
                game.InputRouter.enabled = false;
                yield return null;
                Assert.IsTrue(game.WorldRenderer.TryGetAnimation(game.State.Hero, out var before));
                float time = game.WorldRenderer.AnimationTime;
                var snapshot = game.Session.CaptureSnapshot();
                yield return UIDriver.WaitSeconds(0.15f);
                Assert.IsTrue(game.WorldRenderer.TryGetAnimation(game.State.Hero, out var after));
                Assert.AreEqual(time, game.WorldRenderer.AnimationTime);
                Assert.AreEqual(before.StridePhase, after.StridePhase);
                Assert.AreEqual(before.Frame, after.Frame);
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot(), "rendering must not write gameplay snapshots");
                // Keep automatic ticks/input paused. Submit the real skill input and step the
                // simulation to mid-cast, so a slow render frame cannot skip the short cast.
                Assert.IsTrue(world.Registry.TryResolve(game.State.Hero, out _, out int heroRow));
                game.State.Input = new InputFrame { Pressed = 1u << RpgButton.Skill1 };
                game.Session.Step();
                var casting = world.Column(RpgKeys.Combat)[heroRow];
                Assert.AreEqual(ActionPhase.Cast, casting.Phase, "skill input must start a real cast");
                int maxCastTicks = 1 + (int)math.ceil(casting.PhaseDuration / (float)game.Session.Clock.StepSeconds);
                for (int tick = 0; tick < maxCastTicks && casting.Phase == ActionPhase.Cast && casting.PhaseProgress < 0.45f; tick++)
                {
                    game.Session.Step();
                    casting = world.Column(RpgKeys.Combat)[heroRow];
                }
                Assert.AreEqual(ActionPhase.Cast, casting.Phase, "mid-cast fixture must remain active");
                Assert.That(casting.PhaseProgress, Is.InRange(0.45f, 0.99f));
                yield return Capture("projectile-cast", CharacterClip.Cast);
                game.Host.Initialize(replacement, 21, start: false);
                yield return null;
                Assert.AreEqual(0f, game.WorldRenderer.AnimationTime);
                Assert.IsFalse(game.WorldRenderer.TryGetAnimation(game.State.Hero, out _));
            }
            finally
            {
                RenderCapabilities.Override = null; RpgGameBootstrap.SaveDirectoryOverride = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject); Object.Destroy(config); Object.Destroy(replacement);
                foreach (var module in modules) Object.Destroy(module);
                target.Release(); Object.Destroy(target); Object.Destroy(read);
                if (Directory.Exists(save)) Directory.Delete(save, true);
            }

            CharacterClip CurrentClip() => game.WorldRenderer.TryGetAnimation(game.State.Hero, out var a) ? a.Clip : CharacterClip.Death;
            IEnumerator Capture(string state, CharacterClip expected)
            {
                bool resume = game.Session.State == SPF.Runtime.Session.SessionState.Running;
                game.Session.Pause(); // Freeze the actual simulated transition while the camera captures it.
                yield return null;
                yield return null;
                // Validate after Pause has completed the last tick and the renderer has settled.
                // A timed-out predicate or an already-finished action must fail before capture.
                Assert.AreEqual(RpgFlow.Playing, game.State.Flow, state + " gameplay flow");
                Assert.IsTrue(game.WorldRenderer.TryGetAnimation(game.State.Hero, out var animation), state + " actor must be drawn");
                Assert.AreEqual(expected, animation.Clip, state + " settled clip");
                var clip = game.WorldRenderer.Art.Hero.Clip(expected);
                int expectedFrame;
                if (expected == CharacterClip.Cast)
                {
                    Assert.IsTrue(game.Session.World.Registry.TryResolve(game.State.Hero, out _, out int row));
                    var action = game.Session.World.Column(RpgKeys.Combat)[row];
                    Assert.AreEqual(ActionPhase.Cast, action.Phase, "captured cast must still be authoritative");
                    Assert.Greater(action.PhaseSkill, 0);
                    Assert.AreEqual(SkillKind.Projectile, game.Runtime.Skills[action.PhaseSkill - 1].Kind);
                    Assert.That(action.PhaseProgress, Is.InRange(0.45f, 0.99f));
                    expectedFrame = clip.FrameAtProgress(action.PhaseProgress);
                }
                else expectedFrame = expected == CharacterClip.Idle ? clip.FrameAt(animation.Time)
                    : clip.FrameAtProgress(animation.StridePhase);
                Assert.AreEqual(expectedFrame, animation.Frame, state + " exact settled sprite frame");
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); read.Apply(false);
                RenderTexture.active = previous;
                string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"rpg-motion-{state}-{tier}.png"), read.EncodeToPNG());
                if (resume) game.Session.Resume();
            }
        }

        [UnityTest]
        public IEnumerator SpritesLookTheSameOnBothTiers()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU tier unavailable");
            var art = RpgArt.Build(4, k => new Color(0.3f + 0.2f * k, 0.8f - 0.15f * k, 0.4f, 1f));
            var cameraObject = new GameObject("SpriteTestCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
            camera.transform.position = new Vector3(0f, 0f, -50f);
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 100f;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var frames = new Color32[2][];
            try
            {
                // GPU tier (structured buffer) and data-texture tier (RGBA8 words), both with packed instances.
                for (int pass = 0; pass < 2; pass++)
                {
                    var tier = pass == 0 ? RenderTier.GpuDriven : RenderTier.DataTexture;
                    using var batch = new SpriteBatch(tier, art.Sheet.Texture, BlendKind.Opaque, 512);
                    for (int f = 0; f < 4; f++)
                    {
                        Fill(batch, art);
                        batch.Draw(new Bounds(Vector3.zero, new Vector3(1000, 1000, 1000)));
                        yield return null;
                    }
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    read.Apply(false);
                    RenderTexture.active = previous;
                    frames[pass] = read.GetPixels32();
                    string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(Path.Combine(dir, $"sprites-{(pass == 0 ? "gpu" : "datatex")}.png"), read.EncodeToPNG());
                }

                int lit = 0;
                foreach (var a in frames[0]) if (a.r + a.g + a.b > 60) lit++;
                Assert.Greater(lit, frames[0].Length / 50, "sprites were drawn");
                for (int other = 1; other < 2; other++)
                {
                    int differing = 0;
                    for (int i = 0; i < frames[0].Length; i++)
                    {
                        var a = frames[0][i];
                        var b = frames[other][i];
                        int d = math.max(math.abs(a.r - b.r), math.max(math.abs(a.g - b.g), math.abs(a.b - b.b)));
                        if (d > 24) differing++;
                    }
                    TestContext.WriteLine($"sprites: {lit} lit pixels, {differing} differ from the GPU tier (data texture)");
                    Assert.Less(differing, frames[0].Length / 200, "every path draws the same image");
                }
            }
            finally
            {
                camera.targetTexture = null;
                Object.Destroy(cameraObject);
                target.Release();
                Object.Destroy(target);
                Object.Destroy(read);
                art.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator SpritePrefixTextureTransitionsMatchGpuPixels()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (!SystemInfo.supportsComputeShaders || SystemInfo.maxComputeBufferInputsVertex < 4)
                Assert.Ignore("GPU tier unavailable");
            const int Size = 128;
            var atlas = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            atlas.SetPixel(0, 0, Color.white);
            atlas.Apply(false);
            var cameras = new Camera[2];
            var targets = new RenderTexture[2];
            var batches = new SpriteBatch[2];
            var frames = new Color32[2][];
            var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            // Almost all slots are collapsed; distinct visible sentinels straddle each texture/page boundary.
            // They make an incorrect texel-size binding, source offset, or stale tail visible in the image.
            int[] sentinels = { 0, 1, 255, 256, 511, 512, 4095, 4096 };
            int[] counts = { 1, 256, 257, 512, 513, 4097, 257, 1, 0, 4097, 1 };
            var bounds = new Bounds(Vector3.zero, new Vector3(100f, 100f, 100f));
            try
            {
                for (int tier = 0; tier < 2; tier++)
                {
                    var go = new GameObject("SpritePrefixCamera" + tier, typeof(Camera));
                    var camera = cameras[tier] = go.GetComponent<Camera>();
                    camera.orthographic = true;
                    camera.orthographicSize = 4f;
                    camera.aspect = 1f;
                    camera.allowMSAA = false;
                    camera.allowHDR = false;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.transform.position = new Vector3(0f, 0f, -50f);
                    camera.nearClipPlane = 1f;
                    camera.farClipPlane = 100f;
                    // Isolate both tiers from each other and any remaining game objects in the test scene.
                    camera.cullingMask = 1 << (30 + tier);
                    targets[tier] = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                    targets[tier].Create();
                    camera.targetTexture = targets[tier];
                    batches[tier] = new SpriteBatch(tier == 0 ? RenderTier.GpuDriven : RenderTier.DataTexture,
                        atlas, BlendKind.Opaque, SpriteBatch.PageSize * 2);
                    batches[tier].Warmup(4097);
                    var instances = batches[tier].Instances;
                    for (int i = 0; i < sentinels.Length; i++)
                    {
                        float2 center = new float2(-2.4f + (i % 4) * 1.6f, i < 4 ? 1f : -1f);
                        instances[sentinels[i]] = PackedSprite.Pack(center, new float2(i % 2 == 0 ? 0.8f : -0.8f, 0.8f),
                            new float4(0f, 0f, 1f, 1f), 0f, new float4(0.6f + i * 0.05f, 1f - i * 0.07f, 0.4f, 1f));
                    }
                }
                foreach (int count in counts)
                {
                    for (int frame = 0; frame < 2; frame++)
                    {
                        for (int tier = 0; tier < 2; tier++)
                        {
                            batches[tier].Count = count;
                            batches[tier].Draw(bounds, layer: 30 + tier, dirty: frame == 0);
                        }
                        yield return null;
                    }
                    var previous = RenderTexture.active;
                    try
                    {
                        for (int tier = 0; tier < 2; tier++)
                        {
                            RenderTexture.active = targets[tier];
                            read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                            read.Apply(false);
                            frames[tier] = read.GetPixels32();
                            // Save only the page crossing and restored small frame, keeping artifacts compact.
                            if (count == 4097 || count == 1)
                            {
                                string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                                Directory.CreateDirectory(dir);
                                File.WriteAllBytes(Path.Combine(dir, $"sprite-prefix-{count}-{(tier == 0 ? "gpu" : "datatex")}.png"), read.EncodeToPNG());
                            }
                        }
                    }
                    finally { RenderTexture.active = previous; }
                    int differing = 0;
                    for (int i = 0; i < frames[0].Length; i++)
                    {
                        var a = frames[0][i];
                        var b = frames[1][i];
                        int delta = math.max(math.abs(a.r - b.r), math.max(math.abs(a.g - b.g), math.abs(a.b - b.b)));
                        if (delta > 1) differing++;
                    }
                    Assert.AreEqual(0, differing, $"count {count}: prefix/page changes must retain pixel parity");
                    for (int i = 0; i < sentinels.Length; i++)
                    {
                        int x = (int)((-2.4f + (i % 4) * 1.6f + 4f) * Size / 8f);
                        int y = (i < 4 ? 5 : 3) * Size / 8;
                        var pixel = frames[0][y * Size + x];
                        bool visible = pixel.r + pixel.g + pixel.b > 100;
                        Assert.AreEqual(sentinels[i] < count, visible, $"count {count}: sentinel {sentinels[i]} visibility");
                    }
                }
            }
            finally
            {
                for (int tier = 0; tier < 2; tier++)
                {
                    if (cameras[tier] != null)
                    {
                        cameras[tier].targetTexture = null;
                        Object.Destroy(cameras[tier].gameObject);
                    }
                    batches[tier]?.Dispose();
                    if (targets[tier] != null) { targets[tier].Release(); Object.Destroy(targets[tier]); }
                }
                Object.Destroy(read);
                Object.Destroy(atlas);
            }
        }

        static void Fill(SpriteBatch batch, RpgArt art)
        {
            batch.Clear();
            var sheet = art.Sheet;
            int n = 0;
            void Character(CharacterArt c, int clip, int frame)
            {
                var sc = c.Clips[clip];
                float2 p = new float2(-6f + (n % 8) * 1.6f, 2.5f - (n / 8) * 1.8f);
                float side = n % 2 == 0 ? 1f : -1f;
                batch.Add(p, new float2(c.Size.x * side, c.Size.y), sheet[sc.First + frame % sc.Count].Uv, 1f + n * 0.01f, n % 3 == 0 ? new float4(1f) : new float4(1f, 0.8f, 0.8f, 1f), 0f, n % 5 == 0 ? 0.6f : 0f);
                n++;
            }
            for (int clip = 0; clip < 6; clip++) Character(art.Hero, clip, clip);
            foreach (var m in art.Monsters) for (int clip = 0; clip < 6; clip += 2) Character(m, clip, 1);
            for (int w = 0; w < art.Weapons.Length; w++)
            {
                var weapon = art.Weapons[w];
                if (weapon == null) continue;
                batch.Add(new float2(-6f + w * 1.8f, -3f), weapon.Size * 1.5f, sheet[weapon.Frame].Uv, 0.5f, new float4(1f), w * 0.7f);
            }
        }
    }
}
