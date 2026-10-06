using System.Collections;
using System.Reflection;
using NUnit.Framework;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Samples.DriftSmoke;
using UnityEngine;
using UnityEngine.TestTools;

namespace SPF.Tests.PlayMode
{
    /// <summary>Exercise Unity's actual Update/LateUpdate and enabled-component lifecycle.</summary>
    public class SessionHostPlayTests
    {
        GameObject m_Object;
        DriftSmokeModule m_Module;
        ModeDefinition m_Mode;
        SessionHost m_Host;

        [SetUp]
        public void SetUp()
        {
            m_Module = DriftSmokeModule.CreateRuntime(16, 20f, 1f, 0f);
            m_Mode = ModeDefinition.Create(new[] { m_Module }, SessionSettings.Default);
            m_Object = new GameObject("Session lifecycle test");
            m_Host = m_Object.AddComponent<SessionHost>();
            m_Host.Initialize(m_Mode, 123);
            m_Host.Session.ManualClock = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Object);
            Object.DestroyImmediate(m_Mode);
            Object.DestroyImmediate(m_Module);
        }

        [UnityTest]
        public IEnumerator DisabledHostStopsTicksAndResumesQueuedTurns([Values(false, true)] bool overlap)
        {
            m_Host.OverlapRendering = overlap;
            m_Host.Session.RequestTicks();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(1u, m_Host.Session.Clock.NextTickIndex);

            m_Host.enabled = false;
            m_Host.Session.RequestTicks(2);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.AreEqual(1u, m_Host.Session.Clock.NextTickIndex, "disabled hosts cannot tick through the launcher");
            Assert.AreEqual(2, m_Host.Session.PendingTicks, "interruption preserves queued player turns");
            Assert.AreEqual(SessionState.Paused, m_Host.Session.State);
            Assert.IsFalse(m_Object.GetComponent<SessionTickLauncher>().enabled);

            m_Host.enabled = true;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(3u, m_Host.Session.Clock.NextTickIndex);
            Assert.AreEqual(0, m_Host.Session.PendingTicks);
        }

        [UnityTest]
        public IEnumerator OverlappingInterruptionsNeverClearGameplayPause()
        {
            // Invoke only the callbacks; the Editor remains in control of its own focus.
            ApplicationCallback("OnApplicationFocus", false);
            ApplicationCallback("OnApplicationPause", true);
            m_Host.Session.RequestTicks();
            m_Host.Session.Pause(); // the player/menu pauses while the OS also has us suspended
            ApplicationCallback("OnApplicationPause", false);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(0u, m_Host.Session.Clock.NextTickIndex, "focus is still lost");

            ApplicationCallback("OnApplicationFocus", true);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(SessionState.Paused, m_Host.Session.State, "OS recovery cannot dismiss a gameplay pause");
            Assert.AreEqual(0u, m_Host.Session.Clock.NextTickIndex);
            m_Host.Session.Resume();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(1u, m_Host.Session.Clock.NextTickIndex);
        }

        [UnityTest]
        public IEnumerator DestroyingHostLeavesNoTickingLauncher()
        {
            var session = m_Host.Session;
            var launcher = m_Object.GetComponent<SessionTickLauncher>();
            Object.DestroyImmediate(m_Host);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(SessionState.Disposed, session.State);
            Assert.IsFalse(launcher.enabled);
        }

        void ApplicationCallback(string name, bool value)
        {
            var callback = typeof(SessionHost).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(callback);
            callback.Invoke(m_Host, new object[] { value });
        }
    }
}
