using System.Collections;
using System.Reflection;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using SPF.Shell.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BrawlerFoundation.Tests.PlayMode
{
    public class BwMobileHudPlayTests
    {
        static void Focus(object component, bool focused) => component.GetType().GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, new object[] { focused });
        [UnityTest]
        public IEnumerator SkillHudOwnsTwoFingersHitsAndFlushesOnPause([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = BwGameBootstrap.CreateMobileCombat();
            try
            {
                yield return null;
                UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5f);
                game.Session.Sync(); game.Session.ManualClock = true;
                var world = game.Session.World; world.ClearLevel();
                BwSpawner.Spawn(world, 0, float2.zero, 1, 0); BwSpawner.Spawn(world, 1, new float2(.9f, 0), -1, 0);
                var target = world.Table(BwKeys.Fighter).Handles[1];
                var f = world.Column(BwKeys.Info)[1]; f.Hp = f.MaxHp = 100; world.Column(BwKeys.Info).Set(1, f);
                game.State.Input = default; game.MobileHud.Refresh();
                Assert.IsTrue(game.MobileHud.PreferredLandscape);
                yield return null;
                Canvas.ForceUpdateCanvases(); // batchmode-safe layout flush; WaitForEndOfFrame never resumes there.
                foreach (var graphic in game.MobileHud.SafeRoot.GetComponentsInChildren<Graphic>(true))
                {
                    Assert.IsNotNull(graphic.GetComponent<CanvasRenderer>(), graphic.name + " requires its own CanvasRenderer, even while hidden");
                    if (graphic.isActiveAndEnabled && (graphic is CombatControlGraphic || graphic is BufferText))
                        Assert.Greater(UIDriver.SubmittedVertexCount(graphic), 0, graphic.name + " must submit actual UI mesh geometry");
                }
                var safeBounds = UIDriver.ScreenRectOf(game.MobileHud.SafeRoot.gameObject);
                Assert.AreSame(game.MobileHud.Joystick.gameObject, UIDriver.FirstHitAtCenter(game.MobileHud.Joystick.gameObject), "joystick touch area wins its raycast");
                var ring = game.MobileHud.SafeRoot.Find("CombatControls").Find("JoystickRing").gameObject;
                var ringBounds = UIDriver.ScreenRectOf(ring);
                Assert.IsTrue(safeBounds.Contains(new Vector2(ringBounds.xMin + 1, ringBounds.yMin + 1)) && safeBounds.Contains(new Vector2(ringBounds.xMax - 1, ringBounds.yMax - 1)), "visible joystick remains inside safe area");
                foreach (var button in game.MobileHud.Buttons)
                {
                    var bounds = UIDriver.ScreenRectOf(button.gameObject);
                    Assert.IsTrue(safeBounds.Contains(new Vector2(bounds.xMin + 1, bounds.yMin + 1)) && safeBounds.Contains(new Vector2(bounds.xMax - 1, bounds.yMax - 1)), "skill touch rectangle remains inside safe area");
                    Assert.AreSame(button.gameObject, UIDriver.FirstHitAtCenter(button.gameObject), "actual GraphicRaycaster reaches the skill, not an overlay or joystick");
                }
                var stickPointer = new PointerEventData(EventSystem.current) { pointerId = 41, position = new Vector2(100, 100) };
                var skillPointer = new PointerEventData(EventSystem.current) { pointerId = 42, position = new Vector2(600, 100) };
                game.Joystick.OnPointerDown(stickPointer); game.MobileHud.Buttons[0].OnPointerDown(skillPointer);
                game.Joystick.OnPointerUp(skillPointer); game.MobileHud.Buttons[0].OnPointerUp(stickPointer);
                Assert.IsTrue(game.Joystick.Pressed); Assert.IsTrue(game.MobileHud.Buttons[0].Pressed);
                yield return null; yield return null;
                for (int i = 0; i < 6; i++) game.Session.Step();
                Assert.AreEqual(92, world.Column(BwKeys.Info)[1].Hp, .001f);
                Assert.AreEqual(0, world.Resource(BwMobileSkills.Key).GetSnapshot(0).Charges);
                Focus(game.Host, false);
                Focus(game.MobileHud, false);
                yield return null;
                Assert.IsFalse(game.Joystick.Pressed); Assert.IsFalse(game.MobileHud.Buttons[0].Pressed); Assert.AreEqual(0u, game.State.Input.Pressed);
                Focus(game.Host, true); Focus(game.MobileHud, true);
                yield return null; yield return null;
                Assert.AreEqual(0u, game.State.Input.Held, "resume requires a new physical press");
                byte[] before = game.Session.CaptureSnapshot();
                game.MobileHud.Refresh(); game.Renderer.enabled = false; game.MobileHud.Refresh();
                CollectionAssert.AreEqual(before, game.Session.CaptureSnapshot(), "layout/render state never ticks skills");
                game.Renderer.enabled = true;
                game.State.Flow = BwFlow.Lost; game.State.Version++; yield return null;
                UIDriver.Click(game.AgainButton.gameObject); game.Session.Step(); yield return null;
                Assert.AreEqual(2, world.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges);
                Assert.IsFalse(world.Registry.TryResolve(target, out _, out _));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
            }
        }
    }
}
