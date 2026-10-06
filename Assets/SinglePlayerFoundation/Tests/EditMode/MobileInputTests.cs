using System.Reflection;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Shell.Input;
using SPF.Shell.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SPF.Tests.EditMode
{
    public class MobileInputTests
    {
        GameObject m_Object;
        VirtualJoystick m_Joystick;
        HoldButton m_Button;
        GestureInput m_Gestures;

        [SetUp]
        public void SetUp()
        {
            m_Object = new GameObject("MobileInputTest");
            m_Joystick = m_Object.AddComponent<VirtualJoystick>();
            m_Button = m_Object.AddComponent<HoldButton>();
            m_Gestures = m_Object.AddComponent<GestureInput>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Object);

        [Test]
        public void JoystickReturnsToNeutralInsideDeadZoneAndOnRelease()
        {
            m_Joystick.OnPointerDown(Pointer(7, 100, 100));
            for (int i = 0; i < 100; i++)
            {
                m_Joystick.OnDrag(Pointer(7, 160, 180));
                Assert.AreEqual(0.6f, m_Joystick.Direction.x, 1e-5f);
                Assert.AreEqual(0.8f, m_Joystick.Direction.y, 1e-5f);
                Assert.AreEqual(100f / 120f, m_Joystick.Magnitude, 1e-5f);
                m_Joystick.OnDrag(Pointer(7, 100 + m_Joystick.DeadZone, 100));
                AssertJoystickNeutral(pressed: true);
                m_Joystick.OnDrag(Pointer(7, 500, 100));
                Assert.AreEqual(1f, m_Joystick.Magnitude);
                m_Joystick.OnDrag(Pointer(7, 100, 100));
                AssertJoystickNeutral(pressed: true);
            }
            m_Joystick.OnDrag(Pointer(7, 0, 100));
            m_Joystick.OnPointerUp(Pointer(7));
            AssertJoystickNeutral();
            m_Joystick.OnDrag(Pointer(7, 500, 100));
            AssertJoystickNeutral();
        }

        [Test]
        public void JoystickOnlyAcceptsItsOwningPointer()
        {
            m_Joystick.OnDrag(Pointer(1, 60));
            AssertJoystickNeutral();
            m_Joystick.OnPointerDown(Pointer(1, 100));
            m_Joystick.OnDrag(Pointer(1, 160));
            m_Joystick.OnPointerDown(Pointer(2, 1000));
            m_Joystick.OnPointerDown(Pointer(1, 1000));
            m_Joystick.OnDrag(Pointer(2, -1000));
            m_Joystick.OnPointerUp(Pointer(2));
            Assert.IsTrue(m_Joystick.Pressed);
            Assert.AreEqual(new float2(1, 0), m_Joystick.Direction);
            Assert.AreEqual(0.5f, m_Joystick.Magnitude);
            m_Joystick.OnDrag(Pointer(1, 220));
            Assert.AreEqual(1f, m_Joystick.Magnitude, "duplicate downs must not move the origin");
            m_Joystick.OnPointerUp(Pointer(1));
            AssertJoystickNeutral();
            m_Joystick.OnPointerDown(Pointer(2, 1000));
            m_Joystick.OnDrag(Pointer(2, 940));
            Assert.AreEqual(new float2(-1, 0), m_Joystick.Direction);
            Assert.AreEqual(0.5f, m_Joystick.Magnitude);
        }

        [Test]
        public void HoldButtonIgnoresOtherPointersAndDuplicateDowns()
        {
            m_Button.OnPointerDown(Pointer(1));
            Assert.IsTrue(m_Button.ConsumePress());
            m_Button.OnPointerDown(Pointer(1));
            m_Button.OnPointerDown(Pointer(2));
            m_Button.OnPointerUp(Pointer(2));
            m_Button.OnPointerExit(Pointer(2));
            Assert.IsTrue(m_Button.Held);
            Assert.IsFalse(m_Button.ConsumePress(), "another finger is not another press");
            m_Button.OnPointerExit(Pointer(1));
            Assert.IsFalse(m_Button.Held);
            m_Button.OnPointerDown(Pointer(2));
            m_Button.OnPointerUp(Pointer(1));
            Assert.IsTrue(m_Button.Held, "a late up from the previous owner must not stop the new hold");
            Assert.IsTrue(m_Button.ConsumePress());
            m_Button.OnPointerUp(Pointer(2));
            Assert.IsFalse(m_Button.Held);
        }

        [Test]
        public void RepeatedShortTapsRemainOneShotAfterNormalUpOrExit()
        {
            var input = new TouchInputSource(m_Joystick).Hold(m_Button, 2);
            for (int i = 0; i < 100; i++)
            {
                m_Button.OnPointerDown(Pointer(i));
                if (i % 2 == 0) m_Button.OnPointerUp(Pointer(i));
                else m_Button.OnPointerExit(Pointer(i));
                var frame = default(InputFrame);
                Assert.IsTrue(input.TryRead(ref frame));
                Assert.AreEqual(1u << 2, frame.Pressed);
                Assert.AreEqual(0u, frame.Held);
                Assert.IsFalse(input.TryRead(ref frame));
                Assert.IsFalse(m_Button.ConsumePress());
            }
        }

        [TestCase("disable")]
        [TestCase("pause")]
        [TestCase("focus")]
        public void InterruptionsClearHeldInputAndQueuedActions(string reason)
        {
            m_Joystick.OnPointerDown(Pointer(1));
            m_Joystick.OnDrag(Pointer(1, 60));
            m_Button.OnPointerDown(Pointer(2));
            SeedGestureOutputs(m_Gestures.Tracker);
            SetField(m_Gestures, "m_MouseDown", true);
            SetField(m_Gestures, "m_HadTouches", true);

            SetInterrupted(reason, true);
            AssertJoystickNeutral();
            Assert.IsFalse(m_Button.Held);
            Assert.IsFalse(m_Button.ConsumePress());
            AssertTrackerEmpty(m_Gestures.Tracker);
            Assert.IsFalse((bool)GetField(m_Gestures, "m_MouseDown"));
            Assert.IsFalse((bool)GetField(m_Gestures, "m_HadTouches"));
            m_Joystick.OnPointerDown(Pointer(3));
            m_Button.OnPointerDown(Pointer(4));
            AssertJoystickNeutral();
            Assert.IsFalse(m_Button.Held);

            SetInterrupted(reason, false);
            m_Joystick.OnDrag(Pointer(1, 200));
            m_Joystick.OnPointerUp(Pointer(1));
            m_Button.OnPointerUp(Pointer(2));
            m_Gestures.Tracker.Up(3, new float2(500, 100), 0.2f);
            AssertJoystickNeutral();
            Assert.IsFalse(m_Button.ConsumePress());
            AssertTrackerEmpty(m_Gestures.Tracker);

            // A fresh gesture after resume must work with reused pointer IDs.
            m_Joystick.OnPointerDown(Pointer(1));
            m_Joystick.OnDrag(Pointer(1, 60));
            m_Button.OnPointerDown(Pointer(2));
            var frame = default(InputFrame);
            Assert.IsTrue(new TouchInputSource(m_Joystick).Hold(m_Button, 0).TryRead(ref frame));
            Assert.AreEqual(new float2(0.5f, 0), frame.Move);
            Assert.AreEqual(1u, frame.Held);
            Assert.AreEqual(1u, frame.Pressed);
            m_Gestures.Tracker.Down(3, float2.zero, 1f);
            m_Gestures.Tracker.Up(3, new float2(100, 0), 1.1f);
            Assert.AreEqual(1, m_Gestures.Tracker.Releases.Count);
            Assert.AreEqual(1, m_Gestures.Tracker.Swipes.Count);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InputStaysNeutralUntilBothFocusAndPauseHaveCleared(bool focusReturnsFirst)
        {
            SetInterrupted("pause", true);
            SetInterrupted("focus", true);
            SetInterrupted(focusReturnsFirst ? "focus" : "pause", false);
            m_Joystick.OnPointerDown(Pointer(1));
            m_Button.OnPointerDown(Pointer(2));
            AssertJoystickNeutral();
            Assert.IsFalse(m_Button.Held);
            Assert.IsFalse(m_Button.ConsumePress());
            SetInterrupted(focusReturnsFirst ? "pause" : "focus", false);
            m_Joystick.OnPointerDown(Pointer(1));
            m_Button.OnPointerDown(Pointer(2));
            Assert.IsTrue(m_Joystick.Pressed);
            Assert.IsTrue(m_Button.Held);
            Assert.IsTrue(m_Button.ConsumePress());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CanceledTouchesNeverBecomeTapsSwipesOrShots(bool dragging)
        {
            Touch(1, TouchPhase.Began, Vector2.zero, 0f);
            if (dragging) Touch(1, TouchPhase.Moved, new Vector2(80, 0), 0.1f);
            m_Gestures.Tracker.BeginFrame();
            Touch(1, TouchPhase.Canceled, new Vector2(dragging ? 100 : 0, 0), 0.2f);
            AssertTrackerEmpty(m_Gestures.Tracker);
            Touch(1, TouchPhase.Ended, new Vector2(100, 0), 0.25f);
            AssertTrackerEmpty(m_Gestures.Tracker);
            Touch(1, TouchPhase.Began, Vector2.zero, 1f);
            Touch(1, TouchPhase.Ended, new Vector2(100, 0), 1.1f);
            Assert.AreEqual(1, m_Gestures.Tracker.Releases.Count);
            Assert.AreEqual(1, m_Gestures.Tracker.Swipes.Count);
        }

        [Test]
        public void BlockedTouchesCannotJoinAnActiveGesture()
        {
            m_Gestures.Blocked = p => p.x > 100f;
            Touch(1, TouchPhase.Began, new Vector2(200, 0), 0f);
            Touch(1, TouchPhase.Moved, Vector2.zero, 0.1f);
            Touch(1, TouchPhase.Ended, Vector2.zero, 0.2f);
            AssertTrackerEmpty(m_Gestures.Tracker);
            Touch(2, TouchPhase.Began, Vector2.zero, 1f);
            Touch(3, TouchPhase.Began, new Vector2(200, 0), 1f);
            Touch(3, TouchPhase.Canceled, new Vector2(200, 0), 1.1f);
            Touch(2, TouchPhase.Ended, Vector2.zero, 1.1f);
            Assert.AreEqual(1, m_Gestures.Tracker.Taps.Count);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void MultiTouchCannotFinishAsASingleFingerAction(bool dragging, bool reverseRelease)
        {
            var tracker = new GestureTracker();
            tracker.Down(1, float2.zero, 0f);
            tracker.Down(2, new float2(100, 0), 0f);
            var first = new float2(dragging ? -100 : 0, 0);
            var second = new float2(dragging ? 200 : 100, 0);
            tracker.Move(1, first);
            tracker.Move(2, second);
            if (dragging) Assert.AreEqual(3f, tracker.Pinch, 1e-5f);
            tracker.Up(reverseRelease ? 2 : 1, reverseRelease ? second : first, 0.1f);
            tracker.Up(reverseRelease ? 1 : 2, reverseRelease ? first : second, 0.1f);
            Assert.AreEqual(0, tracker.Active);
            Assert.AreEqual(0, tracker.Taps.Count);
            Assert.AreEqual(0, tracker.Swipes.Count);
            Assert.AreEqual(0, tracker.Releases.Count);
        }

        [Test]
        public void CancelOnlyRemovesItsPointerAndRebasesPinchDistance()
        {
            var tracker = new GestureTracker();
            tracker.Down(1, float2.zero, 0f);
            tracker.Down(2, new float2(100, 0), 0f);
            tracker.Down(3, new float2(200, 0), 0f);
            tracker.Cancel(99);
            Assert.AreEqual(3, tracker.Active);
            tracker.Cancel(1);
            tracker.BeginFrame();
            tracker.Move(3, new float2(300, 0));
            Assert.AreEqual(2f, tracker.Pinch, 1e-5f);
            tracker.Cancel(2);
            tracker.BeginFrame();
            tracker.Move(3, new float2(320, 0));
            Assert.AreEqual(new float2(20, 0), tracker.Drag, "remaining finger can still pan");
            tracker.Up(3, new float2(320, 0), 0.2f);
            Assert.AreEqual(0, tracker.Releases.Count);
            Assert.AreEqual(0, tracker.Swipes.Count);
            Assert.AreEqual(0, tracker.Taps.Count);
        }

        [Test]
        public void DuplicatePointerDownDoesNotLeakOrChangeTheGestureOrigin()
        {
            var tracker = new GestureTracker();
            for (int i = 0; i < 100; i++)
            {
                tracker.BeginFrame();
                tracker.Down(1, float2.zero, i);
                tracker.Down(1, new float2(500, 0), i + 0.05f);
                Assert.AreEqual(1, tracker.Active);
                tracker.Up(1, new float2(100, 0), i + 0.1f);
                Assert.AreEqual(0, tracker.Active);
                Assert.AreEqual(1, tracker.Releases.Count);
                Assert.AreEqual(float2.zero, tracker.Releases[0].from);
                Assert.AreEqual(1, tracker.Swipes.Count);
            }
        }

        [Test]
        public void GestureResetDiscardsAllStateWithoutChangingConfiguration()
        {
            var tracker = new GestureTracker { TapSlop = 9f, TapMaxSeconds = 0.4f };
            SeedGestureOutputs(tracker);
            tracker.Reset();
            tracker.Reset();
            AssertTrackerEmpty(tracker);
            Assert.AreEqual(9f, tracker.TapSlop);
            Assert.AreEqual(0.4f, tracker.TapMaxSeconds);
            tracker.Down(1, float2.zero, 1f);
            tracker.Up(1, float2.zero, 1.1f);
            Assert.AreEqual(1, tracker.Taps.Count);
        }

        static void SeedGestureOutputs(GestureTracker tracker)
        {
            tracker.Down(1, float2.zero, 0f);
            tracker.Up(1, float2.zero, 0.1f);
            tracker.Down(2, float2.zero, 0f);
            tracker.Up(2, new float2(100, 0), 0.1f);
            tracker.Down(3, new float2(100, 100), 0f);
            tracker.Down(4, new float2(200, 100), 0f);
            tracker.Move(4, new float2(300, 100));
            Assert.AreEqual(1, tracker.Taps.Count);
            Assert.AreEqual(1, tracker.Swipes.Count);
            Assert.AreEqual(1, tracker.Releases.Count);
            Assert.AreEqual(2, tracker.Active);
            Assert.AreEqual(2f, tracker.Pinch);
        }

        static void AssertTrackerEmpty(GestureTracker tracker)
        {
            Assert.AreEqual(0, tracker.Active);
            Assert.IsFalse(tracker.TryGetPrimary(out _, out _));
            Assert.AreEqual(0, tracker.Taps.Count);
            Assert.AreEqual(0, tracker.Swipes.Count);
            Assert.AreEqual(0, tracker.Releases.Count);
            Assert.AreEqual(float2.zero, tracker.Drag);
            Assert.AreEqual(1f, tracker.Pinch);
            Assert.AreEqual(float2.zero, tracker.PinchCentre);
        }

        void AssertJoystickNeutral(bool pressed = false)
        {
            Assert.AreEqual(pressed, m_Joystick.Pressed);
            Assert.AreEqual(float2.zero, m_Joystick.Direction);
            Assert.AreEqual(0f, m_Joystick.Magnitude);
        }

        void SetInterrupted(string reason, bool interrupted)
        {
            foreach (var component in new MonoBehaviour[] { m_Joystick, m_Button, m_Gestures })
            {
                if (reason == "disable")
                {
                    component.enabled = !interrupted;
                    // The .NET harness does not dispatch Unity lifecycle callbacks.
                    if (interrupted) Invoke(component, "OnDisable");
                }
                else if (reason == "pause") Invoke(component, "OnApplicationPause", interrupted);
                else Invoke(component, "OnApplicationFocus", !interrupted);
            }
        }

        void Touch(int id, TouchPhase phase, Vector2 position, float time) =>
            Invoke(m_Gestures, "ProcessTouch", new Touch { fingerId = id, phase = phase, position = position }, time);

        static PointerEventData Pointer(int id, float x = 0, float y = 0) =>
            new PointerEventData(null) { pointerId = id, position = new Vector2(x, y) };

        static void Invoke(object target, string method, params object[] arguments) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);

        static object GetField(object target, string field) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
