using NUnit.Framework;
using SPF.Presentation.Animation;
using SPF.Runtime.Session;
using SPF.Contracts;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class TurnToolsTests
    {
        EmptyModule m_Module;

        [TearDown]
        public void TearDown()
        {
            if (m_Module != null) UnityEngine.Object.DestroyImmediate(m_Module);
        }

        [Test]
        public void EasingsStartAndEndRight()
        {
            foreach (Ease e in System.Enum.GetValues(typeof(Ease)))
            {
                Assert.AreEqual(0f, Easing.Apply(e, 0f), 1e-4f, e.ToString());
                Assert.AreEqual(1f, Easing.Apply(e, 1f), 1e-4f, e.ToString());
            }
            Assert.Greater(Easing.Apply(Ease.OutBack, 0.8f), 1f, "overshoots");
            Assert.Less(Easing.Apply(Ease.InQuad, 0.5f), 0.5f);
        }

        [Test]
        public void TweensChainWithDelaysAndHoldTheirEnd()
        {
            var p = new TweenPlayer();
            p.Set(1, 0, new float2(0f, 0f));
            p.To(1, 0, new float2(10f, 0f), 1f, 0f, Ease.Linear);
            p.To(1, 0, new float2(10f, 5f), 0.5f, 1.25f, Ease.Linear);   // after a short pause
            Assert.IsTrue(p.Busy);
            p.Advance(0.5f);
            Assert.AreEqual(new float2(5f, 0f), p.Get(1, 0, default));
            p.Advance(0.6f);
            Assert.AreEqual(new float2(10f, 0f), p.Get(1, 0, default), "held during the pause");
            p.Advance(0.4f);
            Assert.AreEqual(10f, p.Get(1, 0, default).x, 1e-4f);
            Assert.AreEqual(2.5f, p.Get(1, 0, default).y, 1e-3f, "halfway through the second tween");
            Assert.IsTrue(p.Busy);
            p.Finish();
            Assert.IsFalse(p.Busy);
            Assert.AreEqual(new float2(10f, 5f), p.Get(1, 0, default));
        }

        [Test]
        public void ManualClockRunsOnlyRequestedTicks()
        {
            using var session = TestSession();
            session.ManualClock = true;
            session.Start();
            session.Update(10f);
            Assert.AreEqual(0, session.TicksLastFrame, "time alone does not tick");
            session.RequestTicks(2);
            session.Update(0f);
            Assert.AreEqual(2, session.TicksLastFrame);
            session.Update(5f);
            Assert.AreEqual(0, session.TicksLastFrame);
        }

        [Test]
        public void RequestedTicksSaturateAndDrainWithinTheFrameBudget()
        {
            using var session = TestSession();
            session.ManualClock = true;
            session.Start();
            session.RequestTicks(int.MaxValue - 1);
            session.RequestTicks(10);
            session.RequestTicks(int.MaxValue);
            session.RequestTicks(0);
            session.RequestTicks(int.MinValue);
            Assert.AreEqual(int.MaxValue, session.PendingTicks);

            session.Update(0f);
            Assert.AreEqual(session.Clock.MaxTicksPerFrame, session.TicksLastFrame);
            Assert.AreEqual(int.MaxValue - session.Clock.MaxTicksPerFrame, session.PendingTicks);
        }

        [Test]
        public void ManualPauseRetainsRequestedTicksUntilResume()
        {
            using var session = TestSession();
            session.ManualClock = true;
            session.Start();
            session.Pause();
            session.RequestTicks(2);
            session.Update(10f);
            Assert.AreEqual(0, session.TicksLastFrame);
            Assert.AreEqual(2, session.PendingTicks);
            session.Resume();
            session.Update(0f);
            Assert.AreEqual(2, session.TicksLastFrame);
            Assert.AreEqual(0, session.PendingTicks);
        }

        [Test]
        public void RestartDiscardsRequestedTicksAndPreviousFrameStats()
        {
            using var session = TestSession();
            session.ManualClock = true;
            session.Start();
            session.RequestTicks(10);
            session.Update(0f);
            Assert.Greater(session.PendingTicks, 0);
            Assert.Greater(session.TicksLastFrame, 0);

            session.Restart();
            Assert.AreEqual(0, session.PendingTicks);
            Assert.AreEqual(0, session.TicksLastFrame);
            Assert.AreEqual(0u, session.Clock.NextTickIndex);
            session.Update(10f);
            Assert.AreEqual(0, session.TicksLastFrame);
        }

        [Test]
        public void RestoreDiscardsRequestsFromThePreviousTimelineAndKeepsManualPause()
        {
            using var session = TestSession();
            session.ManualClock = true;
            session.Start();
            session.Step();
            var snapshot = session.CaptureSnapshot();
            session.RequestTicks(10);
            session.Update(0f);

            session.RestoreSnapshot(snapshot);
            Assert.AreEqual(0, session.PendingTicks);
            Assert.AreEqual(0, session.TicksLastFrame);
            Assert.AreEqual(1u, session.Clock.NextTickIndex);
            session.Update(10f);
            Assert.AreEqual(0, session.TicksLastFrame);

            session.Pause();
            session.RequestTicks(2);
            session.RestoreSnapshot(snapshot);
            Assert.AreEqual(SessionState.Paused, session.State);
            Assert.AreEqual(0, session.PendingTicks);
            session.Resume();
            session.Update(0f);
            Assert.AreEqual(0, session.TicksLastFrame);
        }

        [Test]
        public void InvalidRestoreDiscardsRequestedTicksAndKeepsManualPause()
        {
            using var session = TestSession();
            session.ManualClock = true;
            session.Start();
            session.RequestTicks(10);
            session.Update(0f);
            session.Pause();
            Assert.Throws<System.IO.EndOfStreamException>(() => session.RestoreSnapshot(new byte[0]));
            Assert.AreEqual(SessionState.Paused, session.State);
            Assert.AreEqual(0, session.PendingTicks);
            Assert.AreEqual(0, session.TicksLastFrame);
            session.Resume();
            session.Update(10f);
            Assert.AreEqual(0, session.TicksLastFrame);
        }

        [Test]
        public void DisposedSessionRejectsTickRequests()
        {
            using var session = TestSession();
            session.RequestTicks(1);
            session.Dispose();
            Assert.AreEqual(0, session.PendingTicks);
            Assert.Throws<System.ObjectDisposedException>(() => session.RequestTicks());
        }

        SimSession TestSession()
        {
            m_Module = UnityEngine.ScriptableObject.CreateInstance<EmptyModule>();
            return new SimSession(new SPF.Runtime.Composition.IGameplayModule[] { m_Module },
                SPF.Runtime.Composition.SessionSettings.Default, 1);
        }

        sealed class EmptyModule : SPF.Runtime.Composition.GameplayModuleAsset
        {
            public override void DeclareData(WorldLayout layout) => layout.Table(TestKeys.Item, 8).Column(TestKeys.Value).Column(TestKeys.Weight);
            public override void RegisterSystems(SPF.Runtime.Composition.SystemRegistry registry) { }
        }

        [Test]
        public void SnapshotHistoryUndoesAndRedoes()
        {
            using var session = TestSession();
            session.Start();
            var world = session.World;
            var history = new SnapshotHistory(session, capacity: 3);
            history.Record();   // empty
            for (int i = 1; i <= 4; i++)
            {
                world.CreateEntity(TestKeys.Item, out int row);
                world.Column(TestKeys.Value).Set(row, i);
                history.Record();
            }
            Assert.AreEqual(3, history.Count, "capped");
            Assert.IsTrue(history.Undo());
            Assert.AreEqual(3, world.Table(TestKeys.Item).Count);
            Assert.IsTrue(history.Undo());
            Assert.AreEqual(2, world.Table(TestKeys.Item).Count);
            Assert.IsFalse(history.Undo(), "older states were dropped");
            Assert.IsTrue(history.Redo());
            Assert.AreEqual(3, world.Table(TestKeys.Item).Count);
            // A new move after an undo drops the redo branch.
            world.CreateEntity(TestKeys.Item, out int r2);
            world.Column(TestKeys.Value).Set(r2, 99);
            history.Record();
            Assert.IsFalse(history.CanRedo);
            Assert.IsTrue(history.Undo());
            Assert.AreEqual(3, world.Table(TestKeys.Item).Count);
            Assert.Greater(history.Bytes, 0);
        }
    }
}
