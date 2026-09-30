using NUnit.Framework;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Samples.DriftSmoke;
using UnityEngine;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SPF.Tests.EditMode
{
    public class SessionTests
    {
        DriftSmokeModule m_Module;
        SimSession m_Session;

        [SetUp]
        public void SetUp()
        {
            m_Module = DriftSmokeModule.CreateRuntime(count: 5000, worldSize: 100f, maxSpeed: 5f, recycleChance: 0.01f);
            m_Session = new SimSession(new IGameplayModule[] { m_Module }, SessionSettings.Default, seed: 99);
            m_Session.Start();
        }

        [TearDown]
        public void TearDown()
        {
            m_Session.Dispose();
            Object.DestroyImmediate(m_Module);
        }

        [Test]
        public void PopulationStaysAtTargetAndHandlesStayConsistent()
        {
            for (int i = 0; i < 120; i++)
                m_Session.Step();

            var world = m_Session.World;
            var table = world.Table(DriftKeys.Drifter);
            Assert.AreEqual(world.Registry.AliveCount, table.Count);
            Assert.Greater(table.Count, 4800, "recycled entities are respawned next tick");
            for (int row = 0; row < table.Count; row++)
            {
                Assert.IsTrue(world.Registry.TryResolve(table.Handles[row], out int tableIndex, out int resolvedRow));
                Assert.AreEqual(table.TableIndex, tableIndex);
                Assert.AreEqual(row, resolvedRow);
            }

            var snapshot = world.Resource(DriftKeys.Snapshot);
            Assert.Greater(snapshot.CurrentCount, 0);
            foreach (var p in snapshot.Current)
            {
                Assert.LessOrEqual(Mathf.Abs(p.x), 50f);
                Assert.LessOrEqual(Mathf.Abs(p.y), 50f);
            }
        }

        [Test]
        public void SameSeedIsDeterministic()
        {
            var other = new SimSession(new IGameplayModule[] { m_Module }, SessionSettings.Default, seed: 99);
            try
            {
                other.Start();
                for (int i = 0; i < 60; i++)
                {
                    m_Session.Step();
                    other.Step();
                }
                var a = m_Session.World.Resource(DriftKeys.Snapshot).Current;
                var b = other.World.Resource(DriftKeys.Snapshot).Current;
                Assert.AreEqual(a.Length, b.Length);
                // Tolerance: in the editor a job may run as managed code until Burst finishes compiling it.
                for (int i = 0; i < a.Length; i++)
                {
                    Assert.AreEqual(a[i].x, b[i].x, 1e-3f);
                    Assert.AreEqual(a[i].y, b[i].y, 1e-3f);
                }
            }
            finally
            {
                other.Dispose();
            }
        }

        [Test]
        public void RestartReusesMemoryAndRepopulates()
        {
            for (int i = 0; i < 10; i++)
                m_Session.Step();
            m_Session.Restart();
            Assert.AreEqual(0, m_Session.World.Table(DriftKeys.Drifter).Count);

            m_Session.Step();
            Assert.AreEqual(5000, m_Session.World.Table(DriftKeys.Drifter).Count);
            Assert.AreEqual(1, m_Session.Pipeline.Stats.TickCount);
        }

        [Test]
        public void UpdateAndSyncRunTicksFromFrameTime()
        {
            m_Session.Update(0.11f); // 3 ticks at 30 Hz, last one left in flight
            Assert.AreEqual(3, m_Session.TicksLastFrame);
            Assert.IsTrue(m_Session.Pipeline.HasPendingTick);
            m_Session.Sync();
            Assert.IsFalse(m_Session.Pipeline.HasPendingTick);
            Assert.AreEqual(3, m_Session.Pipeline.Stats.TickCount);
        }

        [Test]
        public void SteadyStateTicksDoNotAllocate()
        {
            // Warm up: job reflection data, Burst/JIT, first spawn wave.
            for (int i = 0; i < 30; i++)
                m_Session.Step();

            Assert.That(() =>
            {
                for (int i = 0; i < 300; i++)
                    m_Session.Step();
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
