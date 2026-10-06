using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.Presentation;
using SPF.Testing;
using SurvivorFoundation.Game;
using SurvivorFoundation.Presentation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvGuardPlayTests
    {
        static IEnumerator Portrait(SvGameBootstrap game, string name, bool expectFight)
        {
            var target = new RenderTexture(720, 1280, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(720, 1280, TextureFormat.RGBA32, false);
            var canvas = game.Hud.Canvas;
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
            try
            {
                game.CameraRig.Camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = game.CameraRig.Camera;
                canvas.planeDistance = 1f;
                yield return null; yield return null;
                var previous = RenderTexture.active; RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 720, 1280), 0, 0); read.Apply(false); RenderTexture.active = previous;
                string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, name), read.EncodeToPNG());
                if (expectFight)
                {
                    int cyan = 0, red = 0;
                    var pixels = read.GetPixels32();
                    foreach (var p in pixels)
                    {
                        if (p.g > 160 && p.b > 185 && p.r < 155) cyan++;
                        if (p.r > 140 && p.r > p.g * 1.35f && p.r > p.b * 1.3f) red++;
                    }
                    Assert.Greater(cyan, 200, "electric bands and cyan hero are rendered in portrait");
                    Assert.Greater(red, 50, "enemy health bars are rendered");
                }
            }
            finally
            {
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
                game.CameraRig.Camera.targetTexture = null;
                target.Release(); Object.Destroy(target); Object.Destroy(read);
            }
        }

        [UnityTest]
        public IEnumerator GuardPortraitRingsHealthQualityAndRestart([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var config = SvConfig.CreateGuardExample();
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0f;
            config.Settings.XpBase = 100000;
            foreach (var e in config.Enemies) { e.Speed = 0; e.Hp = 10000; }
            var game = SvGameBootstrap.CreateGuardExample(config, seed: 19);
            string suffix = tier == RenderTier.GpuDriven ? "gpu" : "datatex";
            try
            {
                yield return null;
                Assert.IsTrue(game.Hud.MenuPanel.gameObject.activeInHierarchy);
                yield return Portrait(game, "guard-menu-" + suffix + ".png", false);
                UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5f);
                game.Session.Sync();
                var world = game.Session.World; var runtime = world.Resource(SvKeys.Config);
                for (int i = 0; i < 70; i++)
                {
                    float2 p = new float2(-4.5f + i % 10, 2.8f + i / 10 * 0.65f);
                    SvSpawner.SpawnEnemy(world, runtime, 1 + i % runtime.EnemyKinds, p);
                }
                yield return UIDriver.WaitSeconds(0.3f);
                Assert.AreEqual(SvArtStyle.SmoothOutline, game.Renderer.ArtStyle);
                Assert.IsTrue(game.Renderer.StableTranslucentActors);
                Assert.AreEqual(FilterMode.Bilinear, game.Renderer.Art.Sheet.Texture.filterMode);
                Assert.AreEqual(2, game.Renderer.RingsDrawn);
                Assert.Greater(game.Renderer.EnemiesDrawn, 30);
                Assert.Greater(game.Renderer.HealthBarsDrawn, 30);
                Assert.Greater(game.Renderer.ShadowsDrawn, 30);
                yield return Portrait(game, "guard-portrait-" + suffix + ".png", true);
                game.Session.Sync(); game.Host.enabled = false;
                game.Governor.AdaptiveQuality = false;
                byte[] before = game.Session.CaptureSnapshot();
                game.Governor.SetLevel(3);
                CollectionAssert.AreEqual(before, game.Session.CaptureSnapshot(), "quality changes presentation only");
                yield return null;
                Assert.AreEqual(3, game.Renderer.QualityLevel);
                Assert.LessOrEqual(game.Renderer.ShadowsDrawn, 34, "32 enemy shadows plus hero/beacon");
                Assert.AreEqual(2, game.Renderer.RingsDrawn, "damage bands remain legible at low quality");
                Assert.Greater(game.Renderer.HealthBarsDrawn, 30, "health information is never dropped");
                yield return Portrait(game, "guard-low-quality-" + suffix + ".png", true);
                game.Host.enabled = true;
                game.Session.Sync(); world.Resource(SvKeys.HeroDamage).TryAdd(SvDamage.ToBeacon(1e6f));
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Dead, 5f);
                yield return null;
                Assert.AreEqual(SvLossReason.BeaconLost, game.State.LossReason);
                Assert.IsTrue(game.Hud.DeadPanel.gameObject.activeInHierarchy);
                Assert.IsTrue(game.Hud.DeadText.text.Contains("BEACON LOST"));
                yield return Portrait(game, "guard-loss-" + suffix + ".png", false);
                UIDriver.Click(game.Hud.RestartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5f);
                Assert.AreEqual(config.Settings.BeaconHp, game.State.BeaconHp);
                game.Session.Sync();
                game.State.RunTicks = runtime.Settings.GuardDurationTicks;
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Won, 5f);
                yield return null;
                Assert.IsTrue(game.Hud.DeadText.text.Contains("BEACON SAVED"));
                yield return Portrait(game, "guard-victory-" + suffix + ".png", false);
                UIDriver.Click(game.Hud.MenuButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Menu, 5f);
                yield return null;
                Assert.IsTrue(game.Hud.MenuPanel.gameObject.activeInHierarchy);
                UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5f);
                Assert.AreEqual(SvLossReason.None, game.State.LossReason);
                UIDriver.Click(game.Hud.HudMenuButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Menu, 5f);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                if (game != null) Object.Destroy(game.gameObject);
                Object.Destroy(config);
            }
        }
    }
}
