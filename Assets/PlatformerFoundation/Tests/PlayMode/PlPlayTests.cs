using System.Collections;
using System.IO;
using NUnit.Framework;
using PlatformerFoundation.Game;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Presentation.Animation;
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
        public IEnumerator LocomotionTransitionsPauseAndRebind([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = PlGameBootstrap.Create(ui: false);
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            var replacement = PlMode.Create(out var module);
            try
            {
                yield return null;
                game.StartGame();
                yield return UIDriver.WaitUntil(() => game.State.Flow == PlFlow.Playing && game.State.Motor.Grounded, 5f);
                Assert.AreEqual(PlFlow.Playing, game.State.Flow, "start timed out");
                Assert.IsTrue(game.State.Motor.Grounded, "landing timed out");
                game.CameraRig.Camera.targetTexture = target;
                yield return Capture("idle", GameplayLocomotionState.Idle);
                int uploads = game.Renderer.TileUploads;
                game.Script = () => new InputFrame { Move = new float2(0.3f, 0f) };
                yield return UIDriver.WaitUntil(() => game.Renderer.HeroLocomotion == GameplayLocomotionState.Walk, 3f);
                yield return Capture("walk", GameplayLocomotionState.Walk);
                game.Script = () => new InputFrame { Move = new float2(1f, 0f) };
                yield return UIDriver.WaitUntil(() => game.Renderer.HeroLocomotion == GameplayLocomotionState.Run, 3f);
                yield return Capture("run", GameplayLocomotionState.Run);
                game.Session.Pause();
                yield return null;
                float time = game.Renderer.AnimationTime, phase = game.Renderer.HeroStridePhase;
                int frame = game.Renderer.HeroFrame;
                var snapshot = game.Session.CaptureSnapshot();
                yield return UIDriver.WaitSeconds(0.15f);
                Assert.AreEqual(time, game.Renderer.AnimationTime);
                Assert.AreEqual(phase, game.Renderer.HeroStridePhase);
                Assert.AreEqual(frame, game.Renderer.HeroFrame);
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot(), "paused rendering cannot alter gameplay/snapshot state");
                game.Session.Resume();
                game.Script = () => new InputFrame { Held = 1u << PlButton.Jump, Pressed = 1u << PlButton.Jump };
                yield return UIDriver.WaitUntil(() => game.State.Motor.Velocity.y > 1f && !game.State.Motor.Grounded
                    && game.Renderer.HeroLocomotion == GameplayLocomotionState.Air
                    && game.Renderer.HeroFrame == game.Renderer.Art.HeroJump, 3f);
                yield return Capture("jump", GameplayLocomotionState.Air, 1);
                game.Script = () => default;
                yield return UIDriver.WaitUntil(() => game.State.Motor.Velocity.y < -1f && !game.State.Motor.Grounded
                    && game.Renderer.HeroLocomotion == GameplayLocomotionState.Air
                    && game.Renderer.HeroFrame == game.Renderer.Art.HeroFall, 3f);
                yield return Capture("fall", GameplayLocomotionState.Air, -1);
                Assert.AreEqual(uploads, game.Renderer.TileUploads, "actor transitions preserve the static tile upload contract");
                game.Script = () => default;
                game.Session.Restart();
                game.Session.Pause();
                yield return null; yield return null;
                Assert.AreEqual(PlFlow.Menu, game.State.Flow);
                Assert.AreEqual(0, game.Renderer.TileSprites, "restart cannot display the old map after simulation clears it");
                Assert.AreEqual(GameplayLocomotionState.Idle, game.Renderer.HeroLocomotion);
                Assert.AreEqual(0f, game.Renderer.HeroStridePhase);
                Assert.AreEqual(game.Renderer.Art.HeroIdle.First, game.Renderer.HeroFrame);
                game.Host.Initialize(replacement, 1, start: false);
                yield return null;
                Assert.AreEqual(0f, game.Renderer.AnimationTime, "new session resets presentation time");
                Assert.AreEqual(0f, game.Renderer.HeroStridePhase);
                Assert.AreEqual(GameplayLocomotionState.Idle, game.Renderer.HeroLocomotion);
            }
            finally
            {
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
                Object.Destroy(replacement); Object.Destroy(module);
                target.Release(); Object.Destroy(target); Object.Destroy(read);
            }

            IEnumerator Capture(string state, GameplayLocomotionState expected, int vertical = 0)
            {
                bool resume = game.Session.State == SPF.Runtime.Session.SessionState.Running;
                game.Session.Pause(); // Freeze the actual simulated transition while the camera captures it.
                yield return null;
                yield return null;
                // WaitUntil has a timeout without an assertion. Guard the settled state before
                // writing an artifact, including the tick Pause completed before freezing.
                Assert.AreEqual(PlFlow.Playing, game.State.Flow, state + " gameplay flow");
                Assert.AreEqual(expected, game.Renderer.HeroLocomotion, state + " settled locomotion");
                var art = game.Renderer.Art;
                int expectedFrame;
                if (vertical != 0)
                {
                    Assert.IsFalse(game.State.Motor.Grounded, state + " must be airborne");
                    Assert.Less(game.State.Riding, 0, state + " must not be riding a platform");
                    Assert.Greater(game.State.Motor.Velocity.y * vertical, 0f, state + " vertical velocity");
                    expectedFrame = vertical > 0 ? art.HeroJump : art.HeroFall;
                }
                else
                {
                    Assert.IsTrue(game.State.Motor.Grounded || game.State.Riding >= 0, state + " must be grounded");
                    expectedFrame = expected == GameplayLocomotionState.Run ? art.HeroRun.FrameAtProgress(game.Renderer.HeroStridePhase)
                        : expected == GameplayLocomotionState.Walk ? art.HeroWalk.FrameAtProgress(game.Renderer.HeroStridePhase)
                        : art.HeroIdle.FrameAt(game.Renderer.HeroAnimationTime);
                }
                Assert.AreEqual(expectedFrame, game.Renderer.HeroFrame, state + " exact settled sprite frame");
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); read.Apply(false);
                RenderTexture.active = previous;
                string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"platformer-motion-{state}-{tier}.png"), read.EncodeToPNG());
                if (resume) game.Session.Resume();
            }
        }

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
                game.Governor.ResetGcStats();
                for (int f = 0; f < 180; f++) { yield return null; }
                if (game.Governor.GcCounterValid)
                {
                    GcReport.Write($"platformer run ({tier})", game.Governor.FramesSinceReset, game.Governor.GcFramesSinceReset, game.Governor.GcBytesSinceReset);
                    Assert.LessOrEqual(game.Governor.GcFramesSinceReset, 2, "steady-state play allocates (almost) nothing per frame");
                }
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
    
        [UnityTest]
        public IEnumerator NightLevelIsLitByTorchesAndLantern([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = PlGameBootstrap.Create(ui: false);
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                yield return null;
                game.StartGame();
                yield return UIDriver.WaitUntil(() => game.State.Flow == PlFlow.Playing, 5f);
                game.Session.Sync();
                PlLoader.Load(game.Session.World, game.State, 2);
                for (int f = 0; f < 20; f++) yield return null;
                Assert.IsTrue(game.Renderer.Night);
                Assert.Greater(game.Renderer.Torches, 2);
                Assert.Greater(game.Renderer.LightsUsed, 1, "lantern and torches");

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
                File.WriteAllBytes(Path.Combine(dir, $"platformer-night-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}.png"), read.EncodeToPNG());

                // Lit night: mostly dark, with a bright pool somewhere (torch light on the ground, the lantern).
                var pixels = read.GetPixels32();
                int dark = 0, bright = 0, n = 0;
                for (int i = 0; i < pixels.Length; i += 5, n++)
                {
                    int lum = (pixels[i].r * 3 + pixels[i].g * 6 + pixels[i].b) / 10;
                    if (lum < 50) dark++;
                    if (lum > 150) bright++;
                }
                TestContext.WriteLine($"night ({tier}): dark {dark * 100 / n}%, bright {bright * 100 / n}%");
                Assert.Greater(dark, n / 3, "night: most of the frame is dark");
                Assert.Greater(bright, n / 400, "lights make bright spots");
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
