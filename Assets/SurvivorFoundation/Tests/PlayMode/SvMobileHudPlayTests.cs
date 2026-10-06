using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using SPF.Shell.UI;
using SurvivorFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvMobileHudPlayTests
    {
        [UnityTest]
        public IEnumerator PortraitHudDrivesPulseAimedBlinkCancelAndRestart([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var config = SvConfig.CreateMobileCombatExample();
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            config.Settings.AnnularSkill.Enabled = false; config.Settings.XpBase = 100000;
            foreach (var enemy in config.Enemies) { enemy.Speed = 0; enemy.Hp = 100; enemy.Damage = 0; }
            var game = SvGameBootstrap.CreateMobileCombatExample(config);
            try
            {
                yield return null;
                UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5f);
                game.Session.Sync(); game.Session.ManualClock = true;
                var world = game.Session.World; world.ClearLevel();
                for (int i = 0; i < game.State.Upgrades.Length; i++) game.State.Upgrades[i] = 0;
                SvSpawner.SpawnEnemy(world, world.Resource(SvKeys.Config), 1, new float2(1, 0));
                var hud = game.Hud.MobileHud; hud.Refresh(); Assert.IsFalse(hud.PreferredLandscape);
                yield return null;
                Canvas.ForceUpdateCanvases(); // batchmode-safe layout flush; WaitForEndOfFrame never resumes there.
                foreach (var graphic in hud.SafeRoot.GetComponentsInChildren<Graphic>(true))
                {
                    Assert.IsNotNull(graphic.GetComponent<CanvasRenderer>(), graphic.name + " requires its own CanvasRenderer, even while hidden");
                    if (graphic.isActiveAndEnabled && (graphic is CombatControlGraphic || graphic is BufferText))
                        Assert.Greater(UIDriver.SubmittedVertexCount(graphic), 0, graphic.name + " must submit actual UI mesh geometry");
                }
                var safeBounds = UIDriver.ScreenRectOf(hud.SafeRoot.gameObject);
                Assert.AreSame(hud.Joystick.gameObject, UIDriver.FirstHitAtCenter(hud.Joystick.gameObject), "joystick touch area wins its raycast");
                var ring = hud.SafeRoot.Find("CombatControls").Find("JoystickRing").gameObject;
                var ringBounds = UIDriver.ScreenRectOf(ring);
                Assert.IsTrue(safeBounds.Contains(new Vector2(ringBounds.xMin + 1, ringBounds.yMin + 1)) && safeBounds.Contains(new Vector2(ringBounds.xMax - 1, ringBounds.yMax - 1)), "visible joystick remains inside safe area");
                foreach (var button in hud.Buttons)
                {
                    var bounds = UIDriver.ScreenRectOf(button.gameObject);
                    Assert.IsTrue(safeBounds.Contains(new Vector2(bounds.xMin + 1, bounds.yMin + 1)) && safeBounds.Contains(new Vector2(bounds.xMax - 1, bounds.yMax - 1)), "skill touch rectangle remains inside safe area");
                    Assert.AreSame(button.gameObject, UIDriver.FirstHitAtCenter(button.gameObject), "actual GraphicRaycaster reaches the skill, not an overlay or joystick");
                }
                UIDriver.Click(hud.Buttons[0].gameObject); yield return null; yield return null; game.Session.Step();
                Assert.AreEqual(72, world.Column(SvKeys.Info)[0].Hp, .001f);
                Assert.AreEqual(1, world.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
                var p = new PointerEventData(EventSystem.current) { pointerId = 5, position = new Vector2(400, 100) };
                hud.Refresh(); hud.Buttons[1].OnPointerDown(p); p.position = new Vector2(400, 180); hud.Buttons[1].OnDrag(p); hud.Buttons[1].OnPointerUp(p);
                yield return null; yield return null; game.Session.Step();
                Assert.AreEqual(new float2(0, 3), game.State.Hero);
                hud.Refresh(); p.position = new Vector2(400, 100); hud.Buttons[1].OnPointerDown(p);
                p.position = new Vector2(400, 100 + hud.Buttons[1].CancelRadius + 10); hud.Buttons[1].OnDrag(p); hud.Buttons[1].OnPointerUp(p);
                yield return null; yield return null; game.Session.Step();
                Assert.AreEqual(new float2(0, 3), game.State.Hero); Assert.AreEqual(1, world.Resource(SvMobileSkills.Key).GetSnapshot(1).Charges);
                // A different finger's cancel must preserve an already latched pulse.
                hud.Refresh(); UIDriver.Click(hud.Buttons[0].gameObject); yield return null; yield return null;
                Assert.AreEqual(1u, game.State.Input.Pressed & 1u);
                p.position = new Vector2(400, 100); hud.Buttons[1].OnPointerDown(p);
                hud.Buttons[1].OnCancel(new BaseEventData(EventSystem.current));
                Assert.AreEqual(1u, game.State.Input.Pressed & 1u, "local blink cancellation cannot erase pulse");
                game.Session.Step();
                Assert.AreEqual(0, world.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
                // Complete an aim immediately before scripted ownership takes over; it must never replay afterward.
                hud.Refresh(); hud.Buttons[1].OnPointerDown(p); p.position = new Vector2(400, 180); hud.Buttons[1].OnPointerUp(p);
                game.InputRouter.Scripted.Active = true; game.InputRouter.Scripted.Frame = default;
                yield return null; yield return null;
                Assert.IsFalse(hud.Buttons[1].gameObject.activeInHierarchy); Assert.AreEqual(0u, game.State.Input.Pressed);
                game.InputRouter.Scripted.Active = false; yield return null; yield return null; game.Session.Step();
                Assert.AreEqual(new float2(0, 3), game.State.Hero); Assert.AreEqual(1, world.Resource(SvMobileSkills.Key).GetSnapshot(1).Charges);
                byte[] before = game.Session.CaptureSnapshot();
                game.Governor.AdaptiveQuality = false; game.Governor.SetLevel(3); hud.Refresh();
                CollectionAssert.AreEqual(before, game.Session.CaptureSnapshot());
                game.State.Flow = SvFlow.Dead; game.State.Version++; yield return null;
                UIDriver.Click(game.Hud.RestartButton.gameObject); game.Session.Step(); yield return null;
                Assert.AreEqual(2, world.Resource(SvMobileSkills.Key).GetSnapshot(1).Charges);
                Assert.IsFalse(game.Hud.DeadPanel.gameObject.activeInHierarchy);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject); Object.Destroy(config);
            }
        }
    }
}
