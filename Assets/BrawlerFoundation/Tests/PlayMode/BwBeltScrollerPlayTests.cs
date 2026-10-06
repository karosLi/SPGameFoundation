using System.Collections;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BrawlerFoundation.Tests.PlayMode
{
    /// <summary>Actual game/canvas/camera integration, rather than an isolated presentation demo.
    /// Central Unity runner executes this in both sprite backends and retains unmodified readbacks.</summary>
    public class BwBeltScrollerPlayTests
    {
        static void FeedHud(BwGameBootstrap game)
        {
            var frame = default(InputFrame); game.MobileHud.Input.TryRead(ref frame); game.InputRouter.Sink(frame); game.Session.Step();
        }
        static void Tap(BwGameBootstrap game, CanvasCapture capture, int slot)
        {
            game.MobileHud.Refresh(); var target = game.MobileHud.Buttons[slot].gameObject;
            Assert.AreSame(target, capture.FirstHit(target), "real GraphicRaycaster must reach skill " + slot);
            var pointer = capture.Pointer(target, 20 + slot);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler); ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
            FeedHud(game); game.State.Input = default;
        }
        static void Duel(BwGameBootstrap game, float depth)
        {
            var world = game.Session.World; world.ClearLevel();
            BwSpawner.Spawn(world, 0, new float2(-.8f, 0), 1, 0);
            BwSpawner.Spawn(world, 1, new float2(.1f, depth), -1, 0);
            BwSpawner.Spawn(world, 1, new float2(4, -1.8f), -1, 0);
            var f = world.Column(BwKeys.Info)[1]; f.Hp = f.MaxHp = 100; world.Column(BwKeys.Info).Set(1, f);
            game.State.Flow = BwFlow.Fighting; game.State.Input = default;
            game.Session.Step(); game.MobileHud.Refresh();
        }
        static void CheckControls(BwGameBootstrap game, CanvasCapture capture, Rect safe)
        {
            Assert.AreEqual(4, game.MobileHud.Buttons.Length); Assert.IsTrue(game.MobileHud.PreferredLandscape);
            foreach (var button in game.MobileHud.Buttons)
            {
                var bounds = capture.RectOf(button.gameObject);
                Assert.IsTrue(safe.Contains(new Vector2(bounds.xMin + 1, bounds.yMin + 1)) && safe.Contains(new Vector2(bounds.xMax - 1, bounds.yMax - 1)));
                Assert.AreSame(button.gameObject, capture.FirstHit(button.gameObject));
                Assert.Greater(capture.BrightPixels(bounds), 25, "actual icon/status geometry must be in the camera readback");
            }
            Assert.IsTrue(game.Renderer.NaturalCharacters); Assert.Greater(game.Renderer.PartsDrawn, 0);
            Assert.IsNotNull(game.Renderer.Characters); Assert.AreEqual(3, game.Renderer.Characters.Count);
        }
        [UnityTest]
        public IEnumerator CollectionIntervalKeepsJoystickAndReachesDistantLootAfterPause()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            var game = BwGameBootstrap.CreateBeltScroller(); CanvasCapture capture = null;
            try
            {
                yield return null; UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false;
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 1280, 720);
                game.MobileHud.SetPreviewViewport(1280, 720, new Rect(0, 0, 1280, 720));
                game.MobileHud.Refresh(); game.CameraRig.Snap(); yield return null; Canvas.ForceUpdateCanvases();
                var world = game.Session.World; var belt = world.Resource(BwBeltKeys.State);
                Tap(game, capture, 1); // establish an actual charged attack and incomplete recharge
                var skill = world.Resource(BwMobileSkills.Key).GetSnapshot(1);
                for (int i = 1; i < world.Table(BwKeys.Fighter).Count; i++)
                { var f = world.Column(BwKeys.Info)[i]; f.Hp = 0; f.State = FighterState.KO; world.Column(BwKeys.Info).Set(i, f); }
                game.State.Wave = belt.Config.Waves;
                belt.TryDrop(new float2(.2f, 0), BwBeltDropKind.Coin, 10);
                Assert.Greater(math.distance(world.Column(BwBeltKeys.Ground)[0], belt.Drops[0].Ground), 4f, "pickup starts outside attraction radius");
                game.Session.Step(); yield return null; game.MobileHud.Refresh();
                Assert.AreEqual(BwFlow.WaveClear, game.State.Flow); Assert.IsTrue(game.Joystick.gameObject.activeInHierarchy);
                foreach (var control in game.MobileHud.Buttons) Assert.IsFalse(control.Snapshot.Enabled, "collection keeps movement but disables all actions");
                var pointer = capture.Pointer(game.Joystick.gameObject, 91);
                Assert.AreSame(game.Joystick.gameObject, capture.FirstHit(game.Joystick.gameObject));
                game.Joystick.OnPointerDown(pointer); pointer.position += new Vector2(120, 0); game.Joystick.OnDrag(pointer);
                FeedHud(game); Assert.Greater(game.State.Input.Move.x, .9f);
                game.InputRouter.Sink(new InputFrame { Move = new float2(1, 0), Held = 15, Pressed = 15 });
                Assert.AreEqual(0u, game.State.Input.Held); Assert.AreEqual(0u, game.State.Input.Pressed, "keyboard/action bits cannot queue into next wave");

                // Pause/focus interruption still releases collection controls; resuming requires a new drag.
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                game.Host.GetType().GetMethod("OnApplicationFocus", flags).Invoke(game.Host, new object[] { false });
                game.MobileHud.GetType().GetMethod("OnApplicationFocus", flags).Invoke(game.MobileHud, new object[] { false });
                yield return null;
                Assert.IsFalse(game.Joystick.Pressed); Assert.AreEqual(float2.zero, game.State.Input.Move);
                game.Host.GetType().GetMethod("OnApplicationFocus", flags).Invoke(game.Host, new object[] { true });
                game.MobileHud.GetType().GetMethod("OnApplicationFocus", flags).Invoke(game.MobileHud, new object[] { true });
                yield return null; game.MobileHud.Refresh();
                Assert.IsFalse(game.Joystick.Pressed);
                pointer = capture.Pointer(game.Joystick.gameObject, 92); game.Joystick.OnPointerDown(pointer);
                pointer.position += new Vector2(120, 0); game.Joystick.OnDrag(pointer);
                for (int tick = 0; tick < 90; tick++) FeedHud(game);
                game.Joystick.OnPointerUp(pointer); game.State.Input = default;
                Assert.AreEqual(BwFlow.WaveClear, game.State.Flow);
                Assert.Greater(world.Column(BwBeltKeys.Ground)[0].x, -1f, "actual joystick crossed arena ground during collection");
                Assert.AreEqual(10, belt.Coins); Assert.AreEqual(0, belt.ActiveDrops);
                var after = world.Resource(BwMobileSkills.Key).GetSnapshot(1);
                Assert.AreEqual(skill.Charges, after.Charges); Assert.AreEqual(skill.RechargeTicks, after.RechargeTicks, "collection pauses recharge");
                for (int tick = 0; tick < 60; tick++) game.Session.Step(); yield return null;
                Assert.AreEqual(BwFlow.Won, game.State.Flow); Assert.IsFalse(game.Joystick.gameObject.activeInHierarchy);
                game.InputRouter.Sink(new InputFrame { Move = new float2(1, 0) }); Assert.AreEqual(float2.zero, game.State.Input.Move);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                capture?.Dispose();
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator LandscapeBeltDepthJumpLootRestartAndBudgetIsolation([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = BwGameBootstrap.CreateBeltScroller(); CanvasCapture capture = null;
            var safe = new Rect(24, 0, 1232, 720); string suffix = tier == RenderTier.GpuDriven ? "gpu" : "datatex";
            try
            {
                yield return null; UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false;
                game.Governor.AdaptiveQuality = false; game.Governor.SetLevel(0);
                Duel(game, 1.25f);
                var world = game.Session.World;
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 1280, 720);
                game.MobileHud.SetPreviewViewport(1280, 720, safe); game.MobileHud.Refresh(); game.CameraRig.Snap();
                yield return capture.Save("belt-landscape-depth-ready-" + suffix, safe, game.Joystick.gameObject,
                    game.MobileHud.Buttons[0].gameObject, game.MobileHud.Buttons[1].gameObject, game.MobileHud.Buttons[2].gameObject, game.MobileHud.Buttons[3].gameObject);
                CheckControls(game, capture, safe);
                Tap(game, capture, 0); for (int i = 0; i < 12; i++) game.Session.Step();
                Assert.AreEqual(100, world.Column(BwKeys.Info)[1].Hp, "near X but distant ground depth misses");
                Duel(game, 0); Tap(game, capture, 0); for (int i = 0; i < 5; i++) game.Session.Step();
                Assert.AreEqual(92, world.Column(BwKeys.Info)[1].Hp, .001f, "same-lane sampled strike lands once");
                Assert.AreEqual(FighterState.Hit, world.Column(BwKeys.Info)[1].State);
                yield return capture.Save("belt-landscape-hit-stagger-" + suffix, safe, game.MobileHud.Buttons[0].gameObject);
                for (int i = 0; i < 25; i++) game.Session.Step();

                Duel(game, 0); Tap(game, capture, 1);
                for (int i = 0; i < 12; i++) game.Session.Step();
                Assert.AreEqual(84, world.Column(BwKeys.Info)[1].Hp, .001f, "charged kick applies its actual 16-point hit");
                Assert.AreEqual(1, world.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges);
                Assert.Greater(world.Column(BwKeys.Info)[1].VelocityX, 0, "kick knocks back on the ground axis");
                Assert.Greater(world.Column(BwBeltKeys.Motion)[1].Height, .05f, "kick launches height independently of depth");
                yield return capture.Save("belt-landscape-kick-launch-" + suffix, safe, game.MobileHud.Buttons[1].gameObject);
                for (int i = 0; i < 25; i++) game.Session.Step();

                // Drag the real joystick and tap jump with an independently owned pointer.
                var stick = capture.Pointer(game.Joystick.gameObject, 71); game.Joystick.OnPointerDown(stick);
                stick.position += new Vector2(90, 90); game.Joystick.OnDrag(stick);
                var beforeGround = world.Column(BwBeltKeys.Ground)[0]; Tap(game, capture, 2);
                for (int i = 0; i < 9; i++) FeedHud(game);
                Assert.Greater(world.Column(BwBeltKeys.Ground)[0].x, beforeGround.x);
                Assert.Greater(world.Column(BwBeltKeys.Ground)[0].y, beforeGround.y);
                Assert.Greater(world.Column(BwBeltKeys.Motion)[0].Height, .5f);
                game.Joystick.OnPointerUp(stick); game.State.Input = default;
                yield return capture.Save("belt-landscape-jump-ground-shadow-" + suffix, safe, game.Joystick.gameObject, game.MobileHud.Buttons[2].gameObject);
                byte[] beforeQuality = game.Session.CaptureSnapshot();
                for (int level = 0; level < 4; level++) { game.Governor.SetLevel(level); game.Renderer.SetQualityLevel(level); game.Renderer.RenderFrame(); game.MobileHud.Refresh(); }
                CollectionAssert.AreEqual(beforeQuality, game.Session.CaptureSnapshot(), "view cadence, budget and HUD are read-only");
                game.Renderer.SetQualityLevel(0);
                for (int i = 0; i < 65; i++) game.Session.Step(); Assert.AreEqual(0, world.Column(BwBeltKeys.Motion)[0].Height);

                Duel(game, 0); var player = world.Column(BwKeys.Info)[0]; player.Hp = 50; world.Column(BwKeys.Info).Set(0, player);
                Tap(game, capture, 3); Assert.AreEqual(74, world.Column(BwKeys.Info)[0].Hp);
                var target = world.Column(BwKeys.Info)[1]; target.Hp = 1; world.Column(BwKeys.Info).Set(1, target); game.State.Kos = 1;
                Tap(game, capture, 0); for (int i = 0; i < 5; i++) game.Session.Step();
                Assert.AreEqual(2, world.Resource(BwBeltKeys.State).ActiveDrops);
                yield return capture.Save("belt-landscape-loot-heal-cooldown-" + suffix, safe, game.MobileHud.Buttons[3].gameObject);
                for (int i = 0; i < 40; i++) game.Session.Step();
                Assert.AreEqual(10, world.Resource(BwBeltKeys.State).Coins); Assert.AreEqual(1, world.Resource(BwBeltKeys.State).HealsCollected);
                Assert.AreEqual(92, world.Column(BwKeys.Info)[0].Hp);
                var oldPlayer = world.Table(BwKeys.Fighter).Handles[0]; player = world.Column(BwKeys.Info)[0]; player.Hp = 0; player.State = FighterState.KO; player.StateTime = 1;
                world.Column(BwKeys.Info).Set(0, player); game.Session.Step(); yield return null;
                Assert.AreEqual(BwFlow.Lost, game.State.Flow); Assert.IsTrue(game.EndPanel.gameObject.activeSelf);
                UIDriver.Click(game.AgainButton.gameObject); game.Session.Step(); yield return null;
                Assert.AreEqual(BwFlow.Fighting, game.State.Flow); Assert.IsFalse(world.Registry.TryResolve(oldPlayer, out _, out _));
                Assert.AreEqual(0, world.Resource(BwBeltKeys.State).ActiveDrops); Assert.AreEqual(0, world.Resource(BwBeltKeys.State).Coins);
                Assert.AreEqual(2, world.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges);
                Assert.AreEqual(1, world.Resource(BwMobileSkills.Key).GetSnapshot(3).Charges);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                capture?.Dispose(); RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
            }
        }
    }
}
