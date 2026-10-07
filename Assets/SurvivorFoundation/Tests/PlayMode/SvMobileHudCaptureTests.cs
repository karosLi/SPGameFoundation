using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using SurvivorFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvMobileHudCaptureTests
    {
        static void CheckViewport(SvGameBootstrap game, CanvasCapture capture, Rect safe, int expectedHeight = 1280)
        {
            var hud = game.Hud.MobileHud;
            Assert.AreEqual(720, hud.ViewportWidth); Assert.AreEqual(expectedHeight, hud.ViewportHeight);
            var actual = capture.RectOf(hud.SafeRoot.gameObject);
            Assert.AreEqual(safe.x, actual.x, 1); Assert.AreEqual(safe.y, actual.y, 1);
            Assert.AreEqual(safe.width, actual.width, 1); Assert.AreEqual(safe.height, actual.height, 1);
            Assert.AreSame(hud.Joystick.gameObject, capture.FirstHit(hud.Joystick.gameObject));
            var ring = capture.RectOf(hud.SafeRoot.Find("CombatControls").Find("JoystickRing").gameObject);
            Assert.IsTrue(safe.Contains(new Vector2(ring.xMin + 1, ring.yMin + 1)) && safe.Contains(new Vector2(ring.xMax - 1, ring.yMax - 1)));
            foreach (var button in hud.Buttons)
            {
                var bounds = capture.RectOf(button.gameObject);
                Assert.IsTrue(safe.Contains(new Vector2(bounds.xMin + 1, bounds.yMin + 1)) && safe.Contains(new Vector2(bounds.xMax - 1, bounds.yMax - 1)));
                Assert.AreSame(button.gameObject, capture.FirstHit(button.gameObject));
                Assert.Greater(capture.BrightPixels(bounds), 40, "real skill icon/status pixels appear in portrait");
            }
            var menuBounds = capture.RectOf(game.Hud.HudMenuButton.gameObject);
            var beacon = capture.RectOf(game.Hud.BeaconFill.transform.parent.gameObject);
            Assert.LessOrEqual(beacon.xMax, menuBounds.xMin, "beacon status bar never overlaps the menu hit target");
            Assert.AreSame(game.Hud.HudMenuButton.gameObject, capture.FirstHit(game.Hud.HudMenuButton.gameObject));
            Assert.Greater(capture.BrightPixels(capture.RectOf(game.Hud.StatsText.gameObject)), 50, "the real telemetry canvas is present");
            var cardObject = game.Hud.HudPanel.Find("MobileStatsBackdrop").gameObject;
            var card = capture.RectOf(cardObject);
            var status = capture.RectOf(game.Hud.StatsText.gameObject);
            Assert.IsFalse(game.Hud.ShowDebugTelemetry, "ordinary mobile play must not show engine counters");
            Assert.LessOrEqual(card.height, 112f, "two-row objective card replaces the 204px panel");
            Assert.AreEqual(safe.yMax - 20f, card.yMax, 1f, "header uses a fixed offset from the actual safe top");
            Assert.GreaterOrEqual(card.yMin, safe.yMax - 132f);
            Assert.GreaterOrEqual(status.yMin, card.yMin); Assert.LessOrEqual(status.yMax, card.yMax);
            Assert.LessOrEqual(status.xMax, menuBounds.xMin, "status text does not extend into the menu hit target");
            Assert.AreEqual(2, StatusLines(game.Hud.StatsText), "timer and objective remain visible without the debug line");
            Assert.IsFalse(StatusContains(game.Hud.StatsText, "Enemies"));
            Assert.IsFalse(StatusContains(game.Hud.StatsText, "Bullets"));
        }

        static int StatusLines(SPF.Shell.UI.BufferText text)
        {
            int lines = 1; for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
            return lines;
        }
        static bool StatusContains(SPF.Shell.UI.BufferText text, string token)
        {
            for (int i = 0; i <= text.Length - token.Length; i++)
            {
                bool same = true;
                for (int j = 0; j < token.Length && same; j++) same = text[i + j] == token[j];
                if (same) return true;
            }
            return false;
        }

        static void Pulse(SvGameBootstrap game, CanvasCapture capture)
        {
            var hud = game.Hud.MobileHud; hud.Refresh();
            var target = capture.FirstHit(hud.Buttons[0].gameObject); Assert.AreSame(hud.Buttons[0].gameObject, target);
            var pointer = capture.Pointer(target, 12);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler); ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
            var input = default(InputFrame); Assert.IsTrue(hud.Input.TryRead(ref input)); game.InputRouter.Sink(input); game.Session.Step();
            game.State.Input = default; hud.Refresh();
        }

        [UnityTest]
        public IEnumerator CapturePortraitChargesAimCancelAndSyntheticSafeArea([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var config = SvConfig.CreateMobileCombatExample();
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            config.Settings.XpBase = 100000;
            foreach (var enemy in config.Enemies) { enemy.Speed = 0; enemy.Hp = 1000; enemy.Damage = 0; }
            var game = SvGameBootstrap.CreateMobileCombatExample(config); CanvasCapture capture = null;
            string suffix = tier == RenderTier.GpuDriven ? "gpu" : "datatex";
            var safe = new Rect(0, 0, 720, 1280);
            var notched = new Rect(0, 48, 720, 1168); // Explicit synthetic 64px notch/top + 48px home-indicator/bottom inset.
            try
            {
                yield return null; UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false;
                game.Governor.AdaptiveQuality = false; game.Governor.SetLevel(0);
                var world = game.Session.World; world.ClearLevel(); var runtime = world.Resource(SvKeys.Config);
                for (int i = 0; i < game.State.Upgrades.Length; i++) game.State.Upgrades[i] = 0;
                for (int i = 0; i < 48; i++) SvSpawner.SpawnEnemy(world, runtime, 1 + i % runtime.EnemyKinds, new float2(-4.4f + i % 8 * 1.2f, 1.5f + i / 8 * .8f));
                game.State.Input = default; game.Session.Step();
                var hud = game.Hud.MobileHud;
                byte[] beforeViewport = game.Session.CaptureSnapshot();
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 720, 1280);
                hud.SetPreviewViewport(720, 1280, safe); hud.Refresh(); game.CameraRig.Snap();
                CollectionAssert.AreEqual(beforeViewport, game.Session.CaptureSnapshot()); Assert.Greater(capture.CanvasCount, 0);
                yield return capture.Save("survivor-portrait-ready-" + suffix, safe, hud.Joystick.gameObject, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject, game.Hud.HudMenuButton.gameObject);
                CheckViewport(game, capture, safe); Assert.Greater(game.Renderer.EnemiesDrawn, 30);
                var beforeDiagnostics = game.Session.CaptureSnapshot();
                game.Hud.ShowDebugTelemetry = true; yield return null;
                Assert.IsTrue(StatusContains(game.Hud.StatsText, "Enemies"));
                Assert.IsTrue(StatusContains(game.Hud.StatsText, "Bullets"));
                Assert.AreEqual(3, StatusLines(game.Hud.StatsText));
                CollectionAssert.AreEqual(beforeDiagnostics, game.Session.CaptureSnapshot(), "diagnostics are presentation-only");
                game.Hud.ShowDebugTelemetry = false; yield return null;
                yield return capture.Save("survivor-portrait-compact-restored-" + suffix, safe, hud.Joystick.gameObject, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject, game.Hud.HudMenuButton.gameObject);
                CheckViewport(game, capture, safe);
                Pulse(game, capture); Pulse(game, capture);
                var pulse = world.Resource(SvMobileSkills.Key).GetSnapshot(0);
                Assert.AreEqual(0, pulse.Charges); Assert.Greater(pulse.RechargeTicks, 0);
                yield return capture.Save("survivor-portrait-cooldown-" + suffix, safe, hud.Joystick.gameObject, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject, game.Hud.HudMenuButton.gameObject);
                CheckViewport(game, capture, safe);
                beforeViewport = game.Session.CaptureSnapshot(); hud.SetPreviewViewport(720, 1280, notched); hud.Refresh();
                CollectionAssert.AreEqual(beforeViewport, game.Session.CaptureSnapshot(), "synthetic safe area changes presentation only");
                yield return capture.Save("survivor-portrait-safearea-" + suffix, notched, hud.Joystick.gameObject, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject, game.Hud.HudMenuButton.gameObject);
                CheckViewport(game, capture, notched);
                var target = capture.FirstHit(hud.Buttons[1].gameObject); Assert.AreSame(hud.Buttons[1].gameObject, target);
                var pointer = capture.Pointer(target, 33); var start = pointer.position;
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
                pointer.position = start + new Vector2(0, 140); ExecuteEvents.Execute(target, pointer, ExecuteEvents.dragHandler); hud.Refresh();
                Assert.IsTrue(hud.Buttons[1].Pressed); Assert.IsFalse(hud.Buttons[1].AimingCanceled);
                yield return capture.Save("survivor-portrait-safearea-aim-" + suffix, notched, hud.Joystick.gameObject, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject, game.Hud.HudMenuButton.gameObject);
                CheckViewport(game, capture, notched);
                pointer.position = start + new Vector2(0, hud.Buttons[1].CancelRadius + 40); ExecuteEvents.Execute(target, pointer, ExecuteEvents.dragHandler); hud.Refresh();
                Assert.IsTrue(hud.Buttons[1].AimingCanceled);
                yield return capture.Save("survivor-portrait-safearea-cancel-" + suffix, notched, hud.Joystick.gameObject, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject, game.Hud.HudMenuButton.gameObject);
                CheckViewport(game, capture, notched);
                var hero = game.State.Hero; ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler); game.Session.Step();
                Assert.AreEqual(2, world.Resource(SvMobileSkills.Key).GetSnapshot(1).Charges); Assert.AreEqual(hero, game.State.Hero, "canceled preview never executes blink");
                // A taller mobile viewport must not pull status below the compact fixed-height card.
                beforeViewport = game.Session.CaptureSnapshot();
                capture.Dispose(); capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 720, 1600);
                var tallSafe = new Rect(0, 64, 720, 1472);
                hud.SetPreviewViewport(720, 1600, tallSafe); hud.Refresh();
                CollectionAssert.AreEqual(beforeViewport, game.Session.CaptureSnapshot());
                yield return capture.Save("survivor-portrait-compact-tall-" + suffix, tallSafe, hud.Joystick.gameObject, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject, game.Hud.HudMenuButton.gameObject);
                CheckViewport(game, capture, tallSafe, 1600);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                capture?.Dispose(); RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject); Object.Destroy(config);
            }
        }
    }
}
