using System;
using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using SurvivorFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvFlyingSwordPlayTests
    {
        [UnityTest]
        public IEnumerator CaptureDenseSwordSwarmWithRealPortraitHudAndQualityInvariant([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var config = SvConfig.CreateFlyingSwordExample();
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            config.Settings.XpBase = 100000; config.Settings.FlyingSwords.BaseCount = 18;
            foreach (var e in config.Enemies) { e.Hp = 10000; e.Damage = 0; e.Speed = .2f; e.Shooter = false; }
            var game = SvGameBootstrap.CreateFlyingSwordExample(config, seed: 73);
            CanvasCapture capture = null; string suffix = tier == RenderTier.GpuDriven ? "gpu" : "datatex";
            try
            {
                yield return null; UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false;
                game.Governor.AdaptiveQuality = false; game.Governor.SetLevel(0);
                var world = game.Session.World; var runtime = world.Resource(SvKeys.Config);
                for (int i = 0; i < 160; i++)
                {
                    float angle = i * 2.399963f, radius = 3.3f + math.sqrt(i) * .28f;
                    SvSpawner.SpawnEnemy(world, runtime, i % runtime.EnemyKinds + 1, new float2(math.cos(angle), math.sin(angle)) * radius);
                }
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 720, 1280);
                var safe = new Rect(0, 48, 720, 1168); var hud = game.Hud.MobileHud;
                hud.SetPreviewViewport(720, 1280, safe); hud.Refresh(); game.CameraRig.Snap();
                for (int tick = 0; tick < 65; tick++) { game.Session.Step(); yield return null; }
                Assert.AreEqual(tier, game.Renderer.Tier); Assert.Greater(game.Renderer.EnemiesDrawn, 60);
                Assert.Greater(game.Swords.SwordsDrawn, 8); Assert.Greater(game.Swords.TrailSegmentsDrawn, 8);
                Assert.Greater(world.Resource(SvFlyingSwordState.Key).Counters[1], 10); Assert.Greater(game.Swords.AcceptedNumbers, 0);
                Assert.AreSame(hud.Buttons[0].gameObject, capture.FirstHit(hud.Buttons[0].gameObject));
                Assert.AreSame(hud.Buttons[1].gameObject, capture.FirstHit(hud.Buttons[1].gameObject));
                yield return capture.Save("survivor-sword-horde-portrait-" + suffix, safe, hud.Joystick.gameObject, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject, game.Hud.HudMenuButton.gameObject);
                // Probe the actual presentation calls after construction, atlas upload and pool warmup.
                Action frames = () => { for (int i = 0; i < 60; i++) game.Swords.RenderFrame(); };
                using (var probe = new ManagedAllocationProbe())
                {
                    var pre = probe.Calibrate(); var sample = probe.Measure(frames); var post = probe.Calibrate();
                    TestContext.WriteLine($"Sword presentation {tier}: 60 synchronous RenderFrame calls, allocation={sample.Value} {sample.Metric}; retained/empty calibration before={pre.RetainedArrays.Value}/{pre.Empty.Value}, after={post.RetainedArrays.Value}/{post.Empty.Value}. Excludes the full Unity frame and native/GPU allocations.");
                    Assert.AreEqual(0, sample.Value);
                }
                var snapshot = game.Session.CaptureSnapshot(); int fullSegments = game.Swords.TrailSegmentsDrawn;
                game.Governor.SetLevel(3); yield return null;
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot(), "quality tier and trails never mutate simulation");
                Assert.LessOrEqual(game.Swords.TrailSegmentsDrawn, fullSegments);
                yield return capture.Save("survivor-sword-horde-low-vfx-" + suffix, safe, hud.Joystick.gameObject, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject, game.Hud.HudMenuButton.gameObject);
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot());
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                capture?.Dispose(); RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject); Object.Destroy(config);
            }
        }

        [UnityTest]
        public IEnumerator PlayableSwordLoopKillsLevelsChoosesWinsDiesAndRestarts()
        {
            var config = SvConfig.CreateFlyingSwordExample();
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            config.Settings.FlyingSwords.WaveTicks = 100; config.Settings.FlyingSwords.OrbitTicks = 2;
            config.Settings.FlyingSwords.Speed = 80; config.Settings.FlyingSwords.Damage = 100;
            config.Settings.MagnetRadius = 10; config.Settings.GemSpeed = 40; config.Settings.XpBase = 1;
            foreach (var e in config.Enemies) { e.Speed = 0; e.Damage = 0; e.Hp = 10; e.Shooter = false; }
            var game = SvGameBootstrap.CreateFlyingSwordExample(config);
            try
            {
                yield return null; UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5);
                game.Session.Sync(); game.Session.ManualClock = true;
                var world = game.Session.World; SvSpawner.SpawnEnemy(world, world.Resource(SvKeys.Config), 1, new float2(3, 0));
                for (int i = 0; i < 80 && game.State.Flow != SvFlow.LevelUp; i++) game.Session.Step();
                Assert.AreEqual(SvFlow.LevelUp, game.State.Flow); Assert.AreEqual(1, game.State.Kills);
                yield return null; UIDriver.Click(game.Hud.ChoiceButtons[0].gameObject); game.Session.Step();
                Assert.AreEqual(SvFlow.Playing, game.State.Flow);
                for (int i = 0; i < 110 && game.State.Flow == SvFlow.Playing; i++) game.Session.Step();
                Assert.AreEqual(SvFlow.Won, game.State.Flow); yield return null;
                StringAssert.Contains("HORDE CLEARED", game.Hud.DeadText.text);
                UIDriver.Click(game.Hud.RestartButton.gameObject); game.Session.Step(); Assert.AreEqual(SvFlow.Playing, game.State.Flow);
                Assert.AreEqual(0, game.State.Kills); Assert.AreEqual(0, world.Resource(SvFlyingSwordState.Key).Counters[1]);
                world.Resource(SvKeys.HeroDamage).TryAdd(10000); game.Session.Step(); Assert.AreEqual(SvFlow.Dead, game.State.Flow);
                yield return null; UIDriver.Click(game.Hud.RestartButton.gameObject); game.Session.Step();
                Assert.AreEqual(SvFlow.Playing, game.State.Flow); Assert.Greater(game.State.Hp, 0);
                game.BackToMenu(); game.Session.Step(); Assert.AreEqual(SvFlow.Menu, game.State.Flow);
                Assert.AreEqual(0, world.Resource(SvFlyingSwordState.Key).ActiveCount);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject); Object.Destroy(config);
            }
        }
    }
}
