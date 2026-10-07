using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Jobs;

namespace SPF.Tests.EditMode
{
    public class CompositionRollbackTests
    {
        static readonly TableKey Things = new TableKey("Rollback.Things");
        static readonly ColumnKey<int> Values = new ColumnKey<int>(Things, "Values");
        static readonly ResourceKey<ResourceProbe> First = new ResourceKey<ResourceProbe>("Rollback.First");
        static readonly ResourceKey<ResourceProbe> Second = new ResourceKey<ResourceProbe>("Rollback.Second");
        readonly List<string> m_Events = new List<string>();
        readonly List<ResourceProbe> m_Resources = new List<ResourceProbe>();
        SimWorld m_CapturedWorld;
        SimTable m_CapturedTable;

        [TearDown]
        public void CleanFailedBaseline()
        {
            // The red run intentionally exercises leaking baseline constructors. This fallback
            // runs only after assertions; it does not hide the lifecycle failures under test.
            foreach (var resource in m_Resources) resource.CleanupFailure = null;
            m_CapturedWorld?.Dispose();
            if (m_CapturedTable != null && m_CapturedTable.Handles.IsCreated) m_CapturedTable.Dispose();
            foreach (var resource in m_Resources) if (resource.Disposals == 0) resource.Dispose();
            m_CapturedWorld = null; m_CapturedTable = null; m_Resources.Clear(); m_Events.Clear();
        }
        ResourceProbe Resource(string name, Exception failure = null)
        {
            var resource = new ResourceProbe(name, m_Events) { CleanupFailure = failure };
            m_Resources.Add(resource); return resource;
        }
        ModuleProbe Module(string id = "first", Action<WorldLayout> declare = null,
            Action<SystemRegistry> register = null) => new ModuleProbe(id, declare, register);

        [TestCase(false), TestCase(true)]
        public void LaterDeclarationFailureReleasesAcceptedResourcesAndPreservesCause(bool cleanupThrows)
        {
            var cause = new InvalidOperationException("declare failed");
            var cleanup = cleanupThrows ? new Exception("resource cleanup failed") : null;
            var first = Resource("first", cleanup); var second = Resource("second");
            var error = Assert.Catch(() => WorldComposer.BuildWorld(new[] {
                Module(declare: layout => { layout.Resource(First, first); layout.Resource(Second, second); }),
                Module("later", layout => throw cause)
            }, SessionSettings.Default, 1));
            Assert.AreSame(cause, error); Assert.AreEqual(1, first.Disposals); Assert.AreEqual(1, second.Disposals);
            if (cleanupThrows) AssertCleanupContains(error, cleanup);
        }

        [Test]
        public void RejectedDuplicateResourceStaysWithCaller()
        {
            var owned = Resource("owned"); var rejected = Resource("rejected");
            Assert.Throws<InvalidOperationException>(() => WorldComposer.BuildWorld(new[] {
                Module(declare: layout => { layout.Resource(First, owned); layout.Resource(First, rejected); })
            }, SessionSettings.Default, 1));
            Assert.AreEqual(1, owned.Disposals); Assert.AreEqual(0, rejected.Disposals);
        }

        [Test]
        public void RejectedLevelResourceStaysWithCaller()
        {
            var owned = Resource("owned"); var rejected = Resource("rejected");
            Assert.Throws<ArgumentException>(() => WorldComposer.BuildWorld(new[] {
                Module(declare: layout => { layout.Resource(First, owned); layout.Resource(Second, rejected, true); })
            }, SessionSettings.Default, 1));
            Assert.AreEqual(1, owned.Disposals); Assert.AreEqual(0, rejected.Disposals);
        }

        [Test]
        public void ColumnFactoryFailureReleasesPartialTableAndAcceptedResources()
        {
            var owned = Resource("owned"); var cause = new Exception("column allocation failed");
            Assert.AreSame(cause, Assert.Catch(() => WorldComposer.BuildWorld(new[] {
                Module(declare: layout => {
                    layout.Resource(First, owned);
                    var spec = layout.Table(Things, 4).Column(Values);
                    // Deterministic throw after a real column allocation. No OOM or production test hook.
                    var factories = (List<Action<SimTable>>)typeof(WorldLayout.TableSpec)
                        .GetField("ColumnFactories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(spec);
                    factories.Add(table => { m_CapturedTable = table; throw cause; });
                })
            }, SessionSettings.Default, 1)));
            Assert.IsFalse(m_CapturedTable.Handles.IsCreated); Assert.AreEqual(1, owned.Disposals);
        }

        [TestCase(false), TestCase(true)]
        public void RegistrationFailureReleasesWorldWithoutDestroyingUninitializedSystems(bool cleanupThrows)
        {
            var cause = new Exception("registration failed");
            var cleanup = cleanupThrows ? new Exception("resource cleanup failed") : null;
            var owned = Resource("owned", cleanup); var later = Resource("later");
            var system = new SystemProbe("uninitialized", m_Events);
            var error = Assert.Catch(() => new SimSession(new[] {
                Module(declare: layout => { layout.Resource(First, owned); layout.Resource(Second, later); },
                    register: registry => registry.Add(system)),
                Module("later", register: registry => throw cause)
            }, SessionSettings.Default, 1));
            Assert.AreSame(cause, error); Assert.AreEqual(1, owned.Disposals); Assert.AreEqual(1, later.Disposals);
            Assert.AreEqual(0, system.Creates); Assert.AreEqual(0, system.Destroys);
            if (cleanupThrows) AssertCleanupContains(error, cleanup);
        }

        [TestCase(0, false), TestCase(1, false), TestCase(2, false), TestCase(2, true)]
        public void FailedInitializerRollsBackOnlyCompletedSystemsInReverseOrder(int failedIndex, bool cleanupThrows)
        {
            var cause = new Exception("initializer failed");
            var cleanup = cleanupThrows ? new Exception("destroy failed") : null;
            var owned = Resource("owned");
            var systems = new[] { new SystemProbe("a", m_Events), new SystemProbe("b", m_Events), new SystemProbe("c", m_Events) };
            systems[failedIndex].CreateFailure = cause; systems[1].DestroyFailure = cleanup;
            systems[0].Capture = world => m_CapturedWorld = world;
            var error = Assert.Catch(() => new SimSession(new[] {
                Module(declare: layout => layout.Resource(First, owned), register: registry => {
                    foreach (var system in systems) registry.Add(system);
                })
            }, SessionSettings.Default, 1));
            Assert.AreSame(cause, error);
            for (int i = 0; i < systems.Length; i++)
            {
                Assert.AreEqual(i <= failedIndex ? 1 : 0, systems[i].Creates);
                Assert.AreEqual(i < failedIndex ? 1 : 0, systems[i].Destroys,
                    "failed initializer self-cleans; never-created systems must not receive OnDestroy");
                Assert.AreEqual(0, systems[i].LiveTemporaryResources);
            }
            Assert.AreEqual(1, owned.Disposals);
            if (failedIndex == 2) Assert.Less(m_Events.IndexOf("destroy:b"), m_Events.IndexOf("destroy:a"));
            if (cleanupThrows) AssertCleanupContains(error, cleanup);
        }

        [TestCase(0, 3, 4096), TestCase(-1, 3, 4096), TestCase(30, 0, 4096), TestCase(30, -1, 4096), TestCase(30, 3, -1)]
        public void InvalidSettingsFailBeforeAnyModuleAllocation(int tickRate, int maxTicks, int destroyCapacity)
        {
            int declarations = 0; var owned = Resource("owned");
            var system = new SystemProbe("a", m_Events) { Capture = world => m_CapturedWorld = world };
            var settings = new SessionSettings { TickRate = tickRate, MaxTicksPerFrame = maxTicks, DestroyQueueCapacity = destroyCapacity };
            Assert.Catch<ArgumentOutOfRangeException>(() => new SimSession(new[] {
                Module(declare: layout => { declarations++; layout.Resource(First, owned); }, register: registry => registry.Add(system))
            }, settings, 1));
            Assert.AreEqual(0, declarations); Assert.AreEqual(0, system.Creates);
            Assert.AreEqual(0, owned.Disposals, "not accepted by the layout; remains the caller's");
        }

        [TestCase(1, 1, 0), TestCase(120, 12, 1), TestCase(int.MaxValue, int.MaxValue, 0)]
        public void HistoricallyValidSettingsRemainValid(int rate, int maxTicks, int capacity)
        {
            using var session = new SimSession(Array.Empty<IGameplayModule>(), new SessionSettings {
                TickRate = rate, MaxTicksPerFrame = maxTicks, DestroyQueueCapacity = capacity
            }, 1);
            Assert.AreEqual(rate, session.Clock.TickRate); Assert.AreEqual(maxTicks, session.Clock.MaxTicksPerFrame);
            session.World.Resource(SimWorld.DestroyQueueKey).Request(new EntityHandle(0, 1)); session.Step();
            Assert.AreEqual(capacity == 0 ? 1 : 0, session.World.Resource(SimWorld.DestroyQueueKey).TotalOverflow);
        }

        [Test]
        public void SuccessfulInitializationRetainsSortedOrderAndResourceLifetime()
        {
            var owned = Resource("owned"); var late = new SystemProbe("late", m_Events) { SortOrder = 10 };
            var first = new SystemProbe("first", m_Events); var tied = new SystemProbe("tied", m_Events);
            var session = new SimSession(new[] { Module(declare: layout => layout.Resource(First, owned),
                register: registry => registry.Add(late).Add(first).Add(tied)) }, SessionSettings.Default, 1);
            m_CapturedWorld = session.World;
            CollectionAssert.AreEqual(new[] { "create:first", "create:tied", "create:late" }, m_Events);
            Assert.AreEqual(0, owned.Disposals); session.Dispose();
            CollectionAssert.AreEqual(new[] { "create:first", "create:tied", "create:late", "destroy:late", "destroy:tied", "destroy:first", "dispose:owned" }, m_Events);
            session.Dispose(); Assert.AreEqual(1, owned.Disposals);
        }

        [Test]
        public void NormalCleanupFailureStillDisposesEveryOwnerExactlyOnce()
        {
            var systemFailure = new Exception("destroy failed"); var resourceFailure = new Exception("dispose failed");
            var owned = Resource("owned", resourceFailure); var later = Resource("later");
            var a = new SystemProbe("a", m_Events); var b = new SystemProbe("b", m_Events) { DestroyFailure = systemFailure };
            var session = new SimSession(new[] { Module(declare: layout => {
                layout.Table(Things, 4).Column(Values); layout.Resource(First, owned); layout.Resource(Second, later);
            }, register: registry => registry.Add(a).Add(b)) }, SessionSettings.Default, 1);
            m_CapturedWorld = session.World; m_CapturedTable = session.World.Table(Things);
            var error = Assert.Catch(() => session.Dispose());
            Assert.AreSame(systemFailure, error); AssertCleanupContains(error, resourceFailure);
            Assert.AreEqual(1, a.Destroys); Assert.AreEqual(1, b.Destroys);
            Assert.AreEqual(1, owned.Disposals); Assert.AreEqual(1, later.Disposals);
            Assert.IsFalse(m_CapturedTable.Handles.IsCreated); Assert.AreEqual(SessionState.Disposed, session.State);
            Assert.DoesNotThrow(() => session.Dispose()); Assert.DoesNotThrow(() => session.Pipeline.Dispose());
            Assert.DoesNotThrow(() => session.World.Dispose()); Assert.AreEqual(1, a.Destroys); Assert.AreEqual(1, owned.Disposals);
        }

        [Test]
        public void SuccessfulTransferLeavesLayoutDisposalHarmlessAndAliasesDisposeOnce()
        {
            var resource = Resource("aliased");
            using var layout = new WorldLayout();
            layout.Resource(First, resource); layout.Resource(Second, resource);
            using var world = new SimWorld(layout, 1);
            layout.Dispose();
            Assert.AreEqual(0, resource.Disposals);
            Assert.AreSame(resource, world.Resource(First)); Assert.AreSame(resource, world.Resource(Second));
            Assert.Throws<InvalidOperationException>(() => new SimWorld(layout, 2));
            world.Dispose(); world.Dispose();
            Assert.AreEqual(1, resource.Disposals);
        }

        [Test]
        public void LayoutRollbackDisposesAliasesOnceEvenWhenTheirDisposerThrows()
        {
            var cause = new Exception("declare failed"); var cleanup = new Exception("dispose failed");
            var resource = Resource("aliased", cleanup);
            var error = Assert.Catch(() => WorldComposer.BuildWorld(new[] { Module(declare: layout => {
                layout.Resource(First, resource); layout.Resource(Second, resource); throw cause;
            }) }, SessionSettings.Default, 1));
            Assert.AreSame(cause, error); AssertCleanupContains(error, cleanup); Assert.AreEqual(1, resource.Disposals);
        }

        [TestCase(false), TestCase(true)]
        public void InvalidLaterModuleReleasesEarlierOwnership(bool duplicate)
        {
            var resource = Resource("owned");
            Assert.Throws<ArgumentException>(() => WorldComposer.BuildWorld(new IGameplayModule[] {
                Module(declare: layout => layout.Resource(First, resource)), duplicate ? Module() : null
            }, SessionSettings.Default, 1));
            Assert.AreEqual(1, resource.Disposals);
        }

        [Test]
        public void RejectedScopeDoesNotChangePreviouslyAcceptedResourceScope()
        {
            var owned = Resource("owned"); var rejected = new ResetProbe();
            using var layout = new WorldLayout(); layout.Resource(First, owned);
            // Same typed key, valid IResettableResource candidate, rejected only because key is owned.
            var key = new ResourceKey<object>("Rollback.Scope");
            layout.Resource(key, owned);
            Assert.Throws<InvalidOperationException>(() => layout.Resource(key, rejected, true));
            using var world = new SimWorld(layout, 1);
            Assert.DoesNotThrow(() => world.ClearLevel());
            Assert.AreEqual(0, rejected.Resets);
        }

        [Test]
        public void FailedDirectPipelineDoesNotDisposeItsBorrowedWorld()
        {
            var resource = Resource("owned");
            using var layout = new WorldLayout(); layout.Resource(First, resource);
            using var world = new SimWorld(layout, 1);
            var a = new SystemProbe("a", m_Events); var b = new SystemProbe("b", m_Events) { CreateFailure = new Exception("failed") };
            Assert.Catch(() => new TickPipeline(world, new ISimSystem[] { a, b }));
            Assert.AreEqual(1, a.Destroys); Assert.AreEqual(0, b.Destroys);
            Assert.AreEqual(0, resource.Disposals);
            using var retry = new TickPipeline(world, new ISimSystem[] { new SystemProbe("retry", m_Events) });
            Assert.AreSame(resource, world.Resource(First));
        }

        [Test]
        public void DeclarationFailureBeforeOnCreateDoesNotDestroyRegisteredSystems()
        {
            var cause = new Exception("access declaration failed"); var resource = Resource("owned");
            var a = new SystemProbe("a", m_Events); var b = new SystemProbe("b", m_Events) { DeclareFailure = cause };
            Assert.AreSame(cause, Assert.Catch(() => new SimSession(new[] { Module(
                declare: layout => layout.Resource(First, resource), register: registry => registry.Add(a).Add(b))
            }, SessionSettings.Default, 1)));
            Assert.AreEqual(0, a.Creates); Assert.AreEqual(0, a.Destroys); Assert.AreEqual(0, b.Destroys);
            Assert.AreEqual(1, resource.Disposals);
        }

        [Test]
        public void RepeatedFailedThenSuccessfulCreationDoesNotKeepPriorOwners()
        {
            int attempts = 0; var resources = new List<ResourceProbe>(); var systems = new List<SystemProbe>();
            var module = Module(declare: layout => {
                var resource = Resource("attempt:" + attempts); resources.Add(resource); layout.Resource(First, resource);
            }, register: registry => {
                var a = new SystemProbe("a:" + attempts, m_Events); systems.Add(a); registry.Add(a);
                registry.Add(new SystemProbe("b:" + attempts, m_Events) {
                    CreateFailure = ++attempts < 4 ? new Exception("retry") : null
                });
            });
            for (int i = 0; i < 3; i++)
            {
                Assert.Catch(() => new SimSession(new[] { module }, SessionSettings.Default, 1));
                Assert.AreEqual(1, resources[i].Disposals); Assert.AreEqual(1, systems[i].Destroys);
            }
            using var session = new SimSession(new[] { module }, SessionSettings.Default, 1);
            Assert.AreEqual(0, resources[3].Disposals); session.Step(); session.Dispose();
            foreach (var resource in resources) Assert.AreEqual(1, resource.Disposals);
            foreach (var system in systems) Assert.AreEqual(1, system.Destroys);
        }

        [Test]
        public void SyncCallbackFailureDuringDisposalDoesNotAbandonCompletedWorld()
        {
            var cause = new Exception("sync failed"); var sync = new SyncProbe { Failure = cause };
            var resource = Resource("owned"); var system = new SystemProbe("a", m_Events);
            var key = new ResourceKey<SyncProbe>("Rollback.Sync");
            var session = new SimSession(new[] { Module(declare: layout => {
                layout.Resource(key, sync); layout.Resource(First, resource);
            }, register: registry => registry.Add(system)) }, SessionSettings.Default, 1);
            m_CapturedWorld = session.World; session.Start(); session.Update(1f / 30f);
            Assert.IsTrue(session.Pipeline.HasPendingTick);
            Assert.AreSame(cause, Assert.Catch(() => session.Dispose()));
            Assert.IsFalse(session.Pipeline.HasPendingTick);
            Assert.AreEqual(1, system.Destroys); Assert.AreEqual(1, resource.Disposals); Assert.AreEqual(1, sync.Disposals);
            Assert.AreEqual(SessionState.Disposed, session.State); Assert.DoesNotThrow(() => session.Dispose());
        }

        [TestCase(false), TestCase(true)]
        public void DestroyCallbackCannotScheduleNewTick(bool throughSession)
        {
            var resource = Resource("owned");
            var first = new SystemProbe("first", m_Events);
            var callback = new SystemProbe("callback", m_Events);
            var session = new SimSession(new[] { Module(declare: layout => {
                layout.Table(Things, 4).Column(Values); layout.Resource(First, resource);
            }, register: registry => registry.Add(first).Add(callback)) }, SessionSettings.Default, 1);
            m_CapturedWorld = session.World; m_CapturedTable = session.World.Table(Things);
            callback.DestroyCallback = () => {
                if (throughSession) session.Step();
                else session.Pipeline.BeginTick(new TickTime(0, 1f / 30f, 0));
            };

            Assert.Catch<ObjectDisposedException>(() => session.Dispose());
            Assert.IsFalse(session.Pipeline.HasPendingTick, "cleanup must never schedule new work before freeing storage");
            Assert.AreEqual(1, first.Destroys); Assert.AreEqual(1, callback.Destroys);
            Assert.AreEqual(1, resource.Disposals); Assert.IsFalse(m_CapturedTable.Handles.IsCreated);
            Assert.AreEqual(SessionState.Disposed, session.State);
            Assert.DoesNotThrow(() => session.Dispose());
            Assert.AreEqual(1, first.Destroys); Assert.AreEqual(1, callback.Destroys); Assert.AreEqual(1, resource.Disposals);
        }

        [TestCase(false), TestCase(true)]
        public void SyncCallbackDuringDisposalCannotScheduleNewTick(bool throughSession)
        {
            var sync = new SyncProbe(); var resource = Resource("owned");
            var first = new SystemProbe("first", m_Events); var second = new SystemProbe("second", m_Events);
            var key = new ResourceKey<SyncProbe>("Rollback.SyncReentry");
            var session = new SimSession(new[] { Module(declare: layout => {
                layout.Table(Things, 4).Column(Values); layout.Resource(key, sync); layout.Resource(First, resource);
            }, register: registry => registry.Add(first).Add(second)) }, SessionSettings.Default, 1);
            m_CapturedWorld = session.World; m_CapturedTable = session.World.Table(Things);
            sync.Callback = () => {
                if (throughSession) session.Step();
                else session.Pipeline.BeginTick(new TickTime(1, 1f / 30f, 1.0 / 30));
            };
            session.Start(); session.Update(1f / 30f);
            Assert.IsTrue(session.Pipeline.HasPendingTick);

            Assert.Catch<ObjectDisposedException>(() => session.Dispose());
            Assert.IsFalse(session.Pipeline.HasPendingTick, "OnSync must not start a replacement tick during disposal");
            Assert.AreEqual(1, first.Destroys); Assert.AreEqual(1, second.Destroys);
            Assert.AreEqual(1, sync.Disposals); Assert.AreEqual(1, resource.Disposals);
            Assert.IsFalse(m_CapturedTable.Handles.IsCreated); Assert.AreEqual(SessionState.Disposed, session.State);
            Assert.DoesNotThrow(() => session.Dispose());
        }

        [Test]
        public void DirectPipelineDisposalRejectsUpwardSessionDisposalUntilAllSystemsExit()
        {
            var resource = Resource("owned"); var first = new SystemProbe("first", m_Events);
            var callback = new SystemProbe("callback", m_Events);
            var session = new SimSession(new[] { Module(declare: layout => {
                layout.Table(Things, 4).Column(Values); layout.Resource(First, resource);
            }, register: registry => registry.Add(first).Add(callback)) }, SessionSettings.Default, 1);
            m_CapturedWorld = session.World; m_CapturedTable = session.World.Table(Things);
            bool worldWasAliveForRemainingSystem = false;
            first.DestroyCallback = () => worldWasAliveForRemainingSystem =
                session.World.HasResource(First) && m_CapturedTable.Handles.IsCreated && resource.Disposals == 0;
            callback.DestroyCallback = () => session.Dispose();

            Assert.Catch<InvalidOperationException>(() => session.Pipeline.Dispose());
            Assert.IsTrue(worldWasAliveForRemainingSystem, "the session must not release a world while child teardown is still using it");
            Assert.AreEqual(1, first.Destroys); Assert.AreEqual(1, callback.Destroys);
            Assert.AreEqual(0, resource.Disposals); Assert.IsTrue(m_CapturedTable.Handles.IsCreated);
            Assert.AreNotEqual(SessionState.Disposed, session.State);

            Assert.DoesNotThrow(() => session.Dispose());
            Assert.AreEqual(SessionState.Disposed, session.State); Assert.IsFalse(m_CapturedTable.Handles.IsCreated);
            Assert.AreEqual(1, resource.Disposals); Assert.AreEqual(1, first.Destroys); Assert.AreEqual(1, callback.Destroys);
            Assert.DoesNotThrow(() => session.Dispose()); Assert.AreEqual(1, resource.Disposals);
        }

        sealed class ResetProbe : IResettableResource
        {
            public int Resets;
            public void OnReset() => Resets++;
        }
        sealed class SyncProbe : ISyncResource, IDisposable
        {
            public Exception Failure; public int Disposals; public Action Callback;
            public void OnSync() { Callback?.Invoke(); if (Failure != null) throw Failure; }
            public void Dispose() => Disposals++;
        }

        static void AssertCleanupContains(Exception error, Exception cleanup)
        {
            var recorded = error.Data["SPF.CleanupFailures"] as AggregateException;
            Assert.IsNotNull(recorded, "secondary cleanup failures must be retained without replacing the cause");
            CollectionAssert.Contains(recorded.Flatten().InnerExceptions, cleanup);
        }
        sealed class ModuleProbe : IGameplayModule
        {
            readonly Action<WorldLayout> m_Declare; readonly Action<SystemRegistry> m_Register;
            public string Id { get; }
            public ModuleProbe(string id, Action<WorldLayout> declare, Action<SystemRegistry> register)
            { Id = id; m_Declare = declare; m_Register = register; }
            public void DeclareData(WorldLayout layout) => m_Declare?.Invoke(layout);
            public void RegisterSystems(SystemRegistry registry) => m_Register?.Invoke(registry);
        }
        sealed class ResourceProbe : IDisposable
        {
            readonly string m_Name; readonly List<string> m_Events;
            public int Disposals; public Exception CleanupFailure;
            public ResourceProbe(string name, List<string> events) { m_Name = name; m_Events = events; }
            public void Dispose()
            { Disposals++; m_Events.Add("dispose:" + m_Name); if (CleanupFailure != null) throw CleanupFailure; }
        }
        sealed class SystemProbe : SimSystemBase
        {
            readonly string m_Name; readonly List<string> m_Events;
            public int Creates, Destroys, LiveTemporaryResources, SortOrder;
            public Exception CreateFailure, DestroyFailure, DeclareFailure; public Action<SimWorld> Capture;
            public Action DestroyCallback;
            public SystemProbe(string name, List<string> events) { m_Name = name; m_Events = events; }
            public override SimPhase Phase => SimPhase.Move;
            public override int Order => SortOrder;
            public override void Declare(AccessDeclaration access) { if (DeclareFailure != null) throw DeclareFailure; }
            public override void OnCreate(SimWorld world)
            {
                Creates++; Capture?.Invoke(world); m_Events.Add("create:" + m_Name); LiveTemporaryResources++;
                try { if (CreateFailure != null) throw CreateFailure; }
                catch { LiveTemporaryResources--; throw; } // Explicit partial-initializer ownership.
            }
            public override JobHandle OnTick(in SimContext context, JobHandle dependency) => dependency;
            public override void OnDestroy(SimWorld world)
            {
                Destroys++; LiveTemporaryResources--; m_Events.Add("destroy:" + m_Name);
                DestroyCallback?.Invoke();
                if (DestroyFailure != null) throw DestroyFailure;
            }
        }
    }
}
