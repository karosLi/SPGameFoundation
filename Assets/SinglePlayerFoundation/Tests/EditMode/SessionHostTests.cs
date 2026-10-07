using System.Reflection;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class SessionHostTests
    {
        GameObject m_Object;
        EmptyModule m_Module;
        ModeDefinition m_Mode;
        SessionHost m_Host;
        SimSession Session => m_Host.Session;
        SessionTickLauncher Launcher => m_Object.GetComponent<SessionTickLauncher>();

        [SetUp]
        public void SetUp()
        {
            m_Module = ScriptableObject.CreateInstance<EmptyModule>();
            m_Mode = ModeDefinition.Create(new GameplayModuleAsset[] { m_Module }, SessionSettings.Default);
            m_Object = new GameObject("SessionHostTest");
            m_Host = m_Object.AddComponent<SessionHost>();
            m_Host.Initialize(m_Mode, 1);
            Session.ManualClock = true;
        }

        [TearDown]
        public void TearDown()
        {
            // The .NET harness does not dispatch Unity lifecycle messages.
            Invoke(m_Host, "OnDestroy");
            Object.DestroyImmediate(m_Object);
            Object.DestroyImmediate(m_Mode);
            Object.DestroyImmediate(m_Module);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DisabledHostCompletesPendingWorkAndStopsBothSchedulingPaths(bool overlap)
        {
            m_Host.OverlapRendering = overlap;
            Session.RequestTicks();
            Session.Update(0f);
            Assert.IsTrue(Session.Pipeline.HasPendingTick);

            SetEnabled(false);
            Assert.AreEqual(SessionState.Paused, Session.State);
            Assert.IsFalse(Session.Pipeline.HasPendingTick);
            Assert.IsFalse(Launcher.enabled);
            Assert.AreEqual(0, Session.TicksLastFrame);

            Session.RequestTicks();
            Invoke(m_Host, "Update");
            Invoke(Launcher, "LateUpdate");
            Session.Update(60f);
            Assert.AreEqual(1u, Session.Clock.NextTickIndex);
            Assert.AreEqual(1, Session.PendingTicks);

            // Changing the scheduling mode must not revive a disabled launcher.
            m_Host.OverlapRendering = true;
            Assert.IsFalse(Launcher.enabled);
            m_Host.OverlapRendering = overlap;
            SetEnabled(true);
            Assert.AreEqual(SessionState.Running, Session.State);
            Assert.AreEqual(overlap, Launcher.enabled);
            Invoke(m_Host, overlap ? "LaunchTicks" : "Update");
            Assert.AreEqual(2u, Session.Clock.NextTickIndex);
            Assert.AreEqual(0, Session.PendingTicks);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FocusAndBackgroundReasonsMustBothClear(bool focusReturnsFirst)
        {
            Session.RequestTicks();
            Session.Update(0f);
            Invoke(m_Host, "OnApplicationFocus", false);
            Assert.IsFalse(Session.Pipeline.HasPendingTick);
            Invoke(m_Host, "OnApplicationPause", true);
            Session.RequestTicks();

            Invoke(m_Host, focusReturnsFirst ? "OnApplicationFocus" : "OnApplicationPause", focusReturnsFirst);
            Assert.AreEqual(SessionState.Paused, Session.State);
            Assert.IsFalse(Launcher.enabled);
            Session.Update(60f);
            Assert.AreEqual(1, Session.PendingTicks);

            Invoke(m_Host, focusReturnsFirst ? "OnApplicationPause" : "OnApplicationFocus", !focusReturnsFirst);
            Assert.AreEqual(SessionState.Running, Session.State);
            Assert.IsTrue(Launcher.enabled);
            Session.Update(0f);
            Assert.AreEqual(0, Session.PendingTicks);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GameplayPauseSurvivesInterruption(bool pauseBeforeInterruption)
        {
            if (pauseBeforeInterruption) Session.Pause();
            Invoke(m_Host, "OnApplicationPause", true);
            Invoke(m_Host, "OnApplicationFocus", false);
            if (!pauseBeforeInterruption) Session.Pause();

            Invoke(m_Host, "OnApplicationPause", false);
            Invoke(m_Host, "OnApplicationFocus", true);
            Assert.AreEqual(SessionState.Paused, Session.State);
            Session.RequestTicks();
            Session.Update(60f);
            Assert.AreEqual(1, Session.PendingTicks);
            Session.Resume();
            Session.Update(0f);
            Assert.AreEqual(0, Session.PendingTicks);
        }

        [Test]
        public void GameplayResumeCannotOverrideActiveHostSuspension()
        {
            Session.Pause();
            Invoke(m_Host, "OnApplicationPause", true);
            Session.Resume();
            Assert.AreEqual(SessionState.Paused, Session.State);
            Invoke(m_Host, "OnApplicationPause", false);
            Assert.AreEqual(SessionState.Running, Session.State);
        }

        [Test]
        public void ReenableDoesNotOverrideBackgroundOrGameplayPause()
        {
            SetEnabled(false);
            Invoke(m_Host, "OnApplicationPause", true);
            SetEnabled(true);
            Assert.AreEqual(SessionState.Paused, Session.State);
            Assert.IsFalse(Launcher.enabled);
            Session.Pause();
            Invoke(m_Host, "OnApplicationPause", false);
            Assert.AreEqual(SessionState.Paused, Session.State);
        }

        [Test]
        public void FirstResumedFrameDiscardsBackgroundTime()
        {
            Session.ManualClock = false;
            Session.Update(0.01f);
            float alpha = Session.InterpolationAlpha;
            Invoke(m_Host, "OnApplicationPause", true);
            Invoke(m_Host, "OnApplicationPause", false);
            Session.Update(60f);
            Assert.AreEqual(0, Session.TicksLastFrame);
            Assert.AreEqual(0u, Session.Clock.NextTickIndex);
            Assert.AreEqual(alpha, Session.InterpolationAlpha);
            Assert.AreEqual(0, Session.Clock.DroppedTicks);
            // Duplicate foreground notifications must not discard another frame.
            Invoke(m_Host, "OnApplicationPause", false);
            Invoke(m_Host, "OnApplicationFocus", true);
            Session.Update((float)Session.Clock.StepSeconds);
            Assert.AreEqual(1, Session.TicksLastFrame);
        }

        [Test]
        public void BackgroundTimeIsDiscardedAfterALaterGameplayResume()
        {
            Session.ManualClock = false;
            Session.Pause();
            Invoke(m_Host, "OnApplicationPause", true);
            Invoke(m_Host, "OnApplicationPause", false);
            Session.Update(60f);
            Session.Resume();
            Session.Update(60f);
            Assert.AreEqual(0, Session.TicksLastFrame);
            Assert.AreEqual(0, Session.Clock.DroppedTicks);
            Session.Update((float)Session.Clock.StepSeconds);
            Assert.AreEqual(1, Session.TicksLastFrame);
        }

        [Test]
        public void ReinitializeWhileBackgroundedSuspendsTheNewSession()
        {
            Invoke(m_Host, "OnApplicationPause", true);
            var oldSession = Session;
            m_Host.Initialize(m_Mode, 2);
            Assert.AreEqual(SessionState.Disposed, oldSession.State);
            Assert.AreEqual(SessionState.Paused, Session.State);
            Assert.IsFalse(Launcher.enabled);
            Invoke(m_Host, "OnApplicationPause", false);
            Assert.AreEqual(SessionState.Running, Session.State);
        }

        [Test]
        public void LifecycleCallbacksDoNotStartAnUnstartedSession()
        {
            m_Host.Initialize(m_Mode, 2, start: false);
            Invoke(m_Host, "OnApplicationFocus", false);
            Assert.AreEqual(SessionState.Created, Session.State);
            Invoke(m_Host, "OnApplicationFocus", true);
            Assert.AreEqual(SessionState.Created, Session.State);
            Invoke(m_Host, "OnApplicationFocus", false);
            Session.Start();
            Assert.AreEqual(SessionState.Paused, Session.State);
            Invoke(m_Host, "OnApplicationFocus", true);
            Assert.AreEqual(SessionState.Running, Session.State);
        }

        [Test]
        public void RestartCannotClearHostSuspension()
        {
            Invoke(m_Host, "OnApplicationPause", true);
            Session.RequestTicks(2);
            Session.Restart();
            Assert.AreEqual(SessionState.Paused, Session.State);
            Assert.AreEqual(0, Session.PendingTicks);
            Invoke(m_Host, "OnApplicationPause", false);
            Assert.AreEqual(SessionState.Running, Session.State);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SnapshotRestoreKeepsHostSuspensionWithoutCreatingAManualPause(bool invalid)
        {
            var snapshot = invalid ? new byte[0] : Session.CaptureSnapshot();
            Invoke(m_Host, "OnApplicationPause", true);
            Session.RequestTicks(2);
            if (invalid)
                Assert.Throws<System.IO.EndOfStreamException>(() => Session.RestoreSnapshot(snapshot));
            else
                Session.RestoreSnapshot(snapshot);
            Assert.AreEqual(SessionState.Paused, Session.State);
            Assert.AreEqual(0, Session.PendingTicks);
            Invoke(m_Host, "OnApplicationPause", false);
            Assert.AreEqual(SessionState.Running, Session.State);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FailedReinitializeClearsPublishedSessionAndStopsLauncherUntilRetry(bool overlap)
        {
            m_Host.OverlapRendering = overlap;
            var previous = Session;
            var launcher = Launcher;
            int notifications = 0;
            m_Host.SessionCreated += _ => notifications++;
            var failing = ScriptableObject.CreateInstance<FailingModule>();
            var invalidMode = ModeDefinition.Create(new GameplayModuleAsset[] { failing }, SessionSettings.Default);
            try
            {
                Assert.Throws<System.InvalidOperationException>(() => m_Host.Initialize(invalidMode, 2));
                Assert.AreEqual(SessionState.Disposed, previous.State);
                Assert.IsNull(Session, "failed replacement must not publish the disposed previous session");
                Assert.IsFalse(launcher.enabled, "there is no session left to schedule");
                Assert.AreEqual(0, notifications, "only successful creation is published");
                SetEnabled(false);
                SetEnabled(true);
                m_Host.OverlapRendering = true;
                Assert.IsFalse(launcher.enabled, "lifecycle and scheduling changes cannot revive an empty host");
                Assert.DoesNotThrow(() => Invoke(m_Host, "LateUpdate"));
                Assert.DoesNotThrow(() => Invoke(launcher, "LateUpdate"));

                m_Host.OverlapRendering = overlap;
                m_Host.Initialize(m_Mode, 3);
                Assert.AreSame(launcher, Launcher, "retry reuses the single launcher");
                Assert.AreEqual(1, notifications);
                Assert.AreEqual(SessionState.Running, Session.State);
                Assert.AreEqual(overlap, Launcher.enabled);
                Session.ManualClock = true;
                Session.RequestTicks();
                Invoke(m_Host, overlap ? "LaunchTicks" : "Update");
                Assert.AreEqual(1u, Session.Clock.NextTickIndex);
            }
            finally
            {
                Object.DestroyImmediate(invalidMode);
                Object.DestroyImmediate(failing);
            }
        }

        [Test]
        public void DestroyingHostDetachesEvenWhenSessionCleanupThrows()
        {
            var module = ScriptableObject.CreateInstance<ThrowingCleanupModule>();
            var mode = ModeDefinition.Create(new GameplayModuleAsset[] { module }, SessionSettings.Default);
            SimSession session = null;
            try
            {
                session = m_Host.Initialize(mode, 2);
                Assert.Throws<TargetInvocationException>(() => Invoke(m_Host, "OnDestroy"));
                Assert.IsNull(Session, "destroy must unpublish the session even when owner cleanup reports an error");
                Assert.IsFalse(Launcher.enabled);
                Assert.DoesNotThrow(() => Invoke(Launcher, "LateUpdate"));
                Assert.DoesNotThrow(() => Invoke(m_Host, "OnDestroy"), "host destruction is idempotent after a cleanup error");
                Assert.AreEqual(1, module.Resource.DisposeCalls);
            }
            finally
            {
                // Keep fixture cleanup independent of the host's error path. The injected error is one-shot.
                session?.Dispose();
                Object.DestroyImmediate(mode);
                Object.DestroyImmediate(module);
            }
        }

        [Test]
        public void DestroyingHostDetachesAndDisablesLauncher()
        {
            var session = Session;
            Invoke(m_Host, "OnDestroy");
            Assert.AreEqual(SessionState.Disposed, session.State);
            Assert.IsNull(Session);
            Assert.IsFalse(Launcher.enabled);
            Assert.DoesNotThrow(() => Invoke(Launcher, "LateUpdate"));
        }

        void SetEnabled(bool enabled)
        {
            m_Host.enabled = enabled;
            // Explicit calls are idempotent in Unity and also exercise the callbacks in the harness.
            Invoke(m_Host, enabled ? "OnEnable" : "OnDisable");
        }

        static void Invoke(object target, string method, params object[] arguments) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);

        sealed class ThrowingCleanupModule : GameplayModuleAsset
        {
            static readonly ResourceKey<ThrowingResource> Key = new ResourceKey<ThrowingResource>("Test.HostCleanup");
            public ThrowingResource Resource { get; private set; }
            public override void DeclareData(WorldLayout layout) => layout.Resource(Key, Resource = new ThrowingResource());
            public override void RegisterSystems(SystemRegistry registry) { }
        }

        sealed class ThrowingResource : System.IDisposable
        {
            public int DisposeCalls { get; private set; }
            public void Dispose()
            {
                if (++DisposeCalls == 1) throw new System.InvalidOperationException("injected cleanup failure");
            }
        }

        sealed class FailingModule : GameplayModuleAsset
        {
            public override void DeclareData(WorldLayout layout) => throw new System.InvalidOperationException("injected initialization failure");
            public override void RegisterSystems(SystemRegistry registry) { }
        }

        sealed class EmptyModule : GameplayModuleAsset
        {
            public override void DeclareData(WorldLayout layout) { }
            public override void RegisterSystems(SystemRegistry registry) { }
        }
    }
}
