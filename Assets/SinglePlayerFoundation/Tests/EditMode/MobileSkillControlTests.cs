using System.Reflection;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Shell.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SPF.Tests.EditMode
{
    public class MobileSkillControlTests
    {
        GameObject m_Object;
        SkillControl m_Control;
        [SetUp] public void SetUp() { m_Object = new GameObject("SkillControlTest"); m_Control = m_Object.AddComponent<SkillControl>(); }
        [TearDown] public void TearDown() => Object.DestroyImmediate(m_Object);
        static PointerEventData Pointer(int id, float x = 0, float y = 0) => new PointerEventData(null) { pointerId = id, position = new Vector2(x, y) };
        void Snapshot(SkillActivation activation, int charges = 1, bool enabled = true) => m_Control.SetSnapshot(new SkillSlotSnapshot(new SkillSlotDefinition(1, 0, activation, 30), charges, charges > 0 ? 0 : 30, enabled));
        InputFrame Read() { var f = default(InputFrame); m_Control.Read(ref f, 0); return f; }
        void Lifecycle(string name, object value) => typeof(SkillControl).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(m_Control, new[] { value });

        [Test]
        public void HoldRetainsOwnerAcrossCooldownButForeignUpCannotRelease()
        {
            Snapshot(SkillActivation.Hold); m_Control.OnPointerDown(Pointer(4));
            Assert.AreEqual(1u, Read().Pressed); Assert.AreEqual(1u, Read().Held);
            m_Control.OnPointerDown(Pointer(8)); m_Control.OnPointerUp(Pointer(8));
            Assert.IsTrue(m_Control.Pressed); Assert.AreEqual(0u, Read().Pressed);
            Snapshot(SkillActivation.Hold, charges: 0); Assert.AreEqual(1u, Read().Held, "hold is an intent, not another UI cooldown");
            m_Control.OnPointerUp(Pointer(4)); Assert.AreEqual(0u, Read().Held);
        }

        [Test]
        public void ShortTapSurvivesOneRenderPollAndCannotRepeat()
        {
            Snapshot(SkillActivation.Tap); m_Control.OnPointerDown(Pointer(1)); m_Control.OnPointerUp(Pointer(1));
            Assert.AreEqual(1u, Read().Pressed); Assert.AreEqual(0u, Read().Pressed); Assert.AreEqual(0u, Read().Held);
        }

        [Test]
        public void AimedReleaseUsesOwnerDirectionAndNotForeignFinger()
        {
            Snapshot(SkillActivation.AimRelease); m_Control.OnPointerDown(Pointer(10));
            m_Control.OnDrag(Pointer(10, 0, 100)); m_Control.OnDrag(Pointer(11, -200, 0)); m_Control.OnPointerUp(Pointer(11));
            Assert.AreEqual(0u, Read().Pressed); Assert.IsTrue(m_Control.Pressed);
            m_Control.OnPointerUp(Pointer(10, 0, 100));
            var f = Read(); Assert.AreEqual(1u, f.Pressed); Assert.AreEqual(new float2(0, 1), f.Aim); Assert.AreEqual(0u, Read().Pressed);
        }

        [Test]
        public void DragCancelAndExplicitCancelNeverCast()
        {
            Snapshot(SkillActivation.AimRelease); m_Control.OnPointerDown(Pointer(1)); m_Control.OnDrag(Pointer(1, 400));
            Assert.IsTrue(m_Control.AimingCanceled); m_Control.OnPointerUp(Pointer(1, 400)); Assert.AreEqual(0u, Read().Pressed);
            m_Control.OnPointerDown(Pointer(2)); m_Control.OnDrag(Pointer(2, 80)); m_Control.OnCancel(new BaseEventData(null));
            m_Control.OnPointerUp(Pointer(2, 80)); Assert.AreEqual(0u, Read().Pressed);
        }

        [Test]
        public void DisabledSnapshotRejectsNewPointerAndChangingOwnerSkillCancels()
        {
            Snapshot(SkillActivation.Tap, enabled: false); m_Control.OnPointerDown(Pointer(1)); Assert.AreEqual(0u, Read().Pressed);
            Snapshot(SkillActivation.Hold); m_Control.OnPointerDown(Pointer(1));
            m_Control.SetSnapshot(new SkillSlotSnapshot(new SkillSlotDefinition(2, 1, SkillActivation.Hold, 30), 1, 0, true));
            Assert.AreEqual(0u, Read().Pressed); Assert.IsFalse(m_Control.Pressed);
        }

        [Test]
        public void FocusPauseAndDisableFlushPendingPressBeforeResume()
        {
            Snapshot(SkillActivation.Hold); int canceled = 0; m_Control.Canceled = () => canceled++;
            m_Control.OnPointerDown(Pointer(1)); Lifecycle("OnApplicationPause", true);
            Lifecycle("OnApplicationFocus", false); Lifecycle("OnApplicationPause", false);
            m_Control.OnPointerDown(Pointer(2)); Assert.AreEqual(0u, Read().Pressed);
            Lifecycle("OnApplicationFocus", true); Assert.AreEqual(0u, Read().Held);
            m_Control.OnPointerDown(Pointer(3));
            typeof(SkillControl).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(m_Control, null);
            Assert.AreEqual(0u, Read().Pressed); Assert.GreaterOrEqual(canceled, 3);
        }

        [Test]
        public void SimultaneousJoystickAndSkillKeepSeparatePointerOwners()
        {
            var joystick = m_Object.AddComponent<VirtualJoystick>(); Snapshot(SkillActivation.Hold);
            joystick.OnPointerDown(Pointer(1)); joystick.OnDrag(Pointer(1, 60)); m_Control.OnPointerDown(Pointer(2));
            joystick.OnPointerUp(Pointer(2)); m_Control.OnPointerUp(Pointer(1));
            Assert.IsTrue(joystick.Pressed); Assert.IsTrue(m_Control.Pressed); Assert.AreEqual(1u, Read().Held);
            joystick.CancelInput(); m_Control.CancelInput(); Assert.IsFalse(joystick.Pressed); Assert.IsFalse(m_Control.Pressed);
        }

#if !SPF_DOTNET_HARNESS
        sealed class AssetSource : IMobileCombatHudSource
        {
            public int SlotCount => 1;
            public int TickRate => 30;
            public bool Playing => true;
            public SkillSlotSnapshot ReadSlot(int slot) => new SkillSlotSnapshot(new SkillSlotDefinition(7, 3, SkillActivation.AimRelease, int.MaxValue, 2), 1, int.MaxValue, true);
            public string SlotLabel(int slot) => "AIM";
        }

        [Test]
        public void CustomSpriteSharesAimRotationAndLargeCooldownTextStaysPositive()
        {
            var canvas = UIFactory.CreateCanvas(m_Object.transform);
            var hud = m_Object.AddComponent<MobileCombatHud>();
            var texture = new Texture2D(4, 4);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
            try
            {
                hud.IconResolver = _ => sprite; hud.Build(canvas.transform, new AssetSource(), false);
                var button = hud.Buttons[0]; button.OnPointerDown(Pointer(3)); button.OnDrag(Pointer(3, 0, 100)); hud.Refresh();
                var asset = button.transform.Find("AssetIcon").GetComponent<UnityEngine.UI.Image>();
                var fallback = button.transform.Find("Icon").GetComponent<CombatControlGraphic>();
                Assert.IsTrue(asset.enabled); Assert.IsFalse(fallback.enabled);
                Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 0, 45), asset.transform.localRotation), .01f);
                Assert.Less(Quaternion.Angle(fallback.transform.localRotation, asset.transform.localRotation), .01f);
                button.CancelInput(); hud.Refresh();
                var text = button.transform.Find("SkillState").GetComponent<BufferText>();
                const string expected = "71582788.3s  1/2";
                Assert.AreEqual(expected.Length, text.Length, "cooldown formatting widens before multiplying ticks");
                for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], text[i]);
            }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }
#endif

        [TestCase(720, 1280, 0, 40, 720, 1200)]
        [TestCase(1280, 720, 80, 20, 1120, 680)]
        public void SafeAreaMapsBothOrientationsWithinBounds(int w, int h, int x, int y, int sw, int sh)
        {
            MobileSafeArea.Anchors(new Rect(x, y, sw, sh), w, h, out var min, out var max);
            Assert.AreEqual(x / (float)w, min.x, .0001f); Assert.AreEqual(y / (float)h, min.y, .0001f);
            Assert.AreEqual((x + sw) / (float)w, max.x, .0001f); Assert.AreEqual((y + sh) / (float)h, max.y, .0001f);
            MobileSafeArea.Anchors(new Rect(float.NaN, 0, 20, 20), w, h, out min, out max);
            Assert.AreEqual(Vector2.zero, min); Assert.AreEqual(Vector2.one, max);
        }
    }
}
