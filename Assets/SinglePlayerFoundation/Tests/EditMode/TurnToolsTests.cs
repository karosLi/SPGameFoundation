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
            using var world = TestKeys.CreateWorld(4);
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

        static SimSession TestSession()
        {
            var module = UnityEngine.ScriptableObject.CreateInstance<EmptyModule>();
            var mode = SPF.Runtime.Composition.ModeDefinition.Create(new[] { (SPF.Runtime.Composition.GameplayModuleAsset)module }, SPF.Runtime.Composition.SessionSettings.Default);
            return SimSession.Create(mode, 1);
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
