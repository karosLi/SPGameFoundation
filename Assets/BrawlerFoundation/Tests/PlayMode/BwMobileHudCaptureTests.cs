using System.Collections;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Shell.UI;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BrawlerFoundation.Tests.PlayMode
{
    public class BwMobileHudCaptureTests
    {
#if !SPF_DOTNET_HARNESS
        [Test]
        public void CaptureScopeRestoresCanvasScaleWithoutAScaler()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            var root = new GameObject("CanvasScaleRestore");
            try
            {
                var canvasObject = new GameObject("NoScalerCanvas", typeof(RectTransform), typeof(Canvas));
                canvasObject.transform.SetParent(root.transform, false);
                var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.scaleFactor = 1.25f;
                var cameraObject = new GameObject("CaptureCamera", typeof(Camera)); cameraObject.transform.SetParent(root.transform, false);
                var camera = cameraObject.GetComponent<Camera>();
                using (var capture = new CanvasCapture(root, camera, 128, 72)) canvas.scaleFactor = .75f;
                Assert.AreEqual(1.25f, canvas.scaleFactor, .0001f, "scope restores the Canvas itself, even without a CanvasScaler");
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode); Assert.IsNull(canvas.worldCamera); Assert.IsNull(camera.targetTexture);
            }
            finally { Object.DestroyImmediate(root); }
        }
#endif

        static void CheckViewport(BwGameBootstrap game, CanvasCapture capture, Rect safe)
        {
            var hud = game.MobileHud;
            Assert.AreEqual(1280, hud.ViewportWidth); Assert.AreEqual(720, hud.ViewportHeight);
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
                Assert.AreSame(button.gameObject, capture.FirstHit(button.gameObject), "real camera-space GraphicRaycaster finds each skill");
                Assert.Greater(capture.BrightPixels(bounds), 40, "actual icon/status pixels appear in the captured target");
            }
            Assert.Greater(capture.BrightPixels(capture.RectOf(game.StatsText.gameObject)), 50, "the stats canvas was captured too");
        }

        static void Kick(BwGameBootstrap game, CanvasCapture capture)
        {
            game.MobileHud.Refresh();
            var target = capture.FirstHit(game.MobileHud.Buttons[1].gameObject);
            Assert.AreSame(game.MobileHud.Buttons[1].gameObject, target);
            var pointer = capture.Pointer(target, 22);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
            var frame = default(InputFrame); Assert.IsTrue(game.MobileHud.Input.TryRead(ref frame));
            game.InputRouter.Sink(frame); game.Session.Step();
            for (int tick = 0; tick < 30; tick++) game.Session.Step();
            game.State.Input = default; game.MobileHud.Refresh();
        }

        [UnityTest]
        public IEnumerator CaptureLandscapeSkillChargesAndCooldown([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = BwGameBootstrap.CreateMobileCombat(); CanvasCapture capture = null;
            string suffix = tier == RenderTier.GpuDriven ? "gpu" : "datatex";
            var safe = new Rect(0, 0, 1280, 720);
            try
            {
                yield return null; UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false;
                game.Governor.AdaptiveQuality = false; game.Governor.SetLevel(0);
                var world = game.Session.World; world.ClearLevel();
                BwSpawner.Spawn(world, 0, new float2(-1, 0), 1, 0);
                BwSpawner.Spawn(world, 1, new float2(.15f, 0), -1, 0);
                BwSpawner.Spawn(world, 1, new float2(4, 0), -1, 0);
                var info = world.Column(BwKeys.Info); var target = info[1]; target.Hp = target.MaxHp = 1000; info[1] = target;
                game.State.Flow = BwFlow.Fighting; game.State.Input = default; game.Session.Step();
                byte[] beforeViewport = game.Session.CaptureSnapshot();
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 1280, 720);
                game.MobileHud.SetPreviewViewport(1280, 720, safe); game.MobileHud.Refresh(); game.CameraRig.Snap();
                CollectionAssert.AreEqual(beforeViewport, game.Session.CaptureSnapshot(), "viewport changes neither skill state nor simulation");
                Assert.Greater(capture.CanvasCount, 0);
                yield return capture.Save("brawler-landscape-ready-" + suffix, safe, game.Joystick.gameObject, game.MobileHud.Buttons[0].gameObject, game.MobileHud.Buttons[1].gameObject);
                CheckViewport(game, capture, safe); Assert.Greater(game.Renderer.PartsDrawn, 0);
                Assert.AreEqual(2, world.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges);
                Kick(game, capture); Kick(game, capture);
                var skill = world.Resource(BwMobileSkills.Key).GetSnapshot(1);
                Assert.AreEqual(0, skill.Charges); Assert.Greater(skill.RechargeTicks, 0);
                Assert.Less(world.Column(BwKeys.Info)[1].Hp, 1000, "the captured cooldown followed actual kick damage");
                yield return capture.Save("brawler-landscape-cooldown-" + suffix, safe, game.Joystick.gameObject, game.MobileHud.Buttons[0].gameObject, game.MobileHud.Buttons[1].gameObject);
                CheckViewport(game, capture, safe);
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
