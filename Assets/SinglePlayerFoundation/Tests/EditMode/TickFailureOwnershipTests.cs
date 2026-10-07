using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Jobs;

namespace SPF.Tests.EditMode
{
    public class TickFailureOwnershipTests
    {
        static readonly TableKey Things = new TableKey("TickFailure.Things");
        static readonly ColumnKey<int> Values = new ColumnKey<int>(Things, "Values");
        static readonly ColumnKey<int> Undeclared = new ColumnKey<int>(Things, "Undeclared");
        static readonly ResourceKey<SyncProbe> SyncKey = new ResourceKey<SyncProbe>("TickFailure.Sync");
        static TickTime Time => new TickTime(0, 1f / 30f, 0);

        [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
        public void SchedulingFailureKeepsOriginalAndCompletesEarlierReturnedJob(bool serial, bool syncThrows)
        {
            var cause = new InvalidOperationException("OnTick failure");
            var secondary = new Exception("OnSync failure");
            var writer = new WriterSystem(); var fail = new CallbackSystem { Callback = () => throw cause };
            var sync = new SyncProbe();
            using var session = Create(sync, writer, fail);
            session.Pipeline.SerialProfiling = serial;
            sync.Callback = () => {
                Assert.AreEqual(37, session.World.Column(Values)[0], "returned job completed before notification");
                if (syncThrows) throw secondary;
            };
            var observed = Assert.Catch(() => session.Pipeline.BeginTick(Time));
            Assert.AreSame(cause, observed);
            StringAssert.Contains(nameof(CallbackSystem.OnTick), observed.StackTrace);
            if (syncThrows) AssertSecondary(observed, secondary);
            Assert.IsFalse(session.Pipeline.HasPendingTick);
            Assert.DoesNotThrow(() => session.World.Column(Values), "the throwing OnTick must leave AccessGuard");
            Assert.AreEqual(1, sync.Calls);
            fail.Callback = null; sync.Callback = null;
            session.Step();
            Assert.AreEqual(2, sync.Calls, "recovered pipeline supports a later successful tick");
        }

        [Test]
        public void ThrowingSystemSelfCompletesItsUnreturnedPrivateJob()
        {
            var cause = new Exception("failure after private schedule");
            var system = new PrivateJobSystem { Failure = cause };
            var sync = new SyncProbe();
            using var session = Create(sync, system);
            sync.Callback = () => Assert.AreEqual(37, session.World.Column(Values)[0]);
            Assert.AreSame(cause, Assert.Catch(() => session.Pipeline.BeginTick(Time)));
            Assert.IsTrue(system.CompletedPrivateJob);
            Assert.IsFalse(session.Pipeline.HasPendingTick);
        }

        [TestCase("begin"), TestCase("end"), TestCase("reset"), TestCase("write"), TestCase("read"),
         TestCase("pipeline-dispose"), TestCase("session-dispose")]
        public void OnTickRejectsUnsafeReentry(string operation)
        {
            var callback = new CallbackSystem(); var sync = new SyncProbe();
            using var session = Create(sync, callback);
            callback.Callback = () => {
                callback.Callback = null; // Make any incorrect baseline reentry finite.
                Assert.Catch<InvalidOperationException>(() => Reenter(session, operation));
                Assert.IsTrue(session.World.Table(Things).Handles.IsCreated);
                Assert.AreEqual(0, callback.Destroys);
            };
            session.Step();
            Assert.AreEqual(1, sync.Calls);
            Assert.IsFalse(session.Pipeline.HasPendingTick);
        }

        [TestCase("begin"), TestCase("end"), TestCase("reset"), TestCase("write"), TestCase("read"),
         TestCase("pipeline-dispose"), TestCase("session-dispose")]
        public void RecoverySyncRejectsUnsafeReentryAndPreservesSchedulingFailure(string operation)
        {
            var cause = new Exception("OnTick failure");
            var callback = new CallbackSystem { Callback = () => throw cause }; var sync = new SyncProbe();
            using var session = Create(sync, callback);
            sync.Callback = () => {
                sync.Callback = null;
                Assert.Catch<InvalidOperationException>(() => Reenter(session, operation));
                Assert.IsTrue(session.World.Table(Things).Handles.IsCreated);
                Assert.AreEqual(0, callback.Destroys);
            };
            Assert.AreSame(cause, Assert.Catch(() => session.Pipeline.BeginTick(Time)));
            Assert.IsFalse(session.Pipeline.HasPendingTick);
            Assert.IsNull(cause.Data[CleanupErrors.DataKey], "reentry was rejected and handled inside the callback");
        }

        [TestCase(false), TestCase(true)]
        public void OrdinarySyncCannotDisposePipelineOrOwnerWhileCallbacksRun(bool throughOwner)
        {
            var callback = new CallbackSystem(); var sync = new SyncProbe();
            using var session = Create(sync, callback);
            sync.Callback = () => {
                sync.Callback = null;
                Assert.Catch<InvalidOperationException>(() => {
                    if (throughOwner) session.Dispose(); else session.Pipeline.Dispose();
                });
                Assert.IsTrue(session.World.Table(Things).Handles.IsCreated);
                Assert.AreEqual(0, callback.Destroys);
            };
            session.Step();
        }

#if SPF_DOTNET_HARNESS
        // These inject BEFORE Complete; the harness executes jobs eagerly. They validate ownership
        // state/retry policy, not Unity engine Complete failure behavior or native worker timing.
        [Test]
        public void SimulatedRecoveryCompletionRejectionKeepsWorldOwnedUntilRetry()
        {
            var cause = new Exception("OnTick failure"); var rejected = new Exception("simulated pre-completion rejection");
            var sync = new SyncProbe(); var writer = new WriterSystem();
            var callback = new CallbackSystem { Callback = () => throw cause };
            using var session = Create(sync, writer, callback);
            try
            {
                SetCompletionHook(session.Pipeline, _ => throw rejected);
                Assert.AreSame(cause, Assert.Catch(() => session.Pipeline.BeginTick(Time)));
                AssertSecondary(cause, rejected);
                Assert.IsTrue(session.Pipeline.HasPendingTick);
                Assert.AreEqual(0, sync.Calls);
                Assert.Catch<InvalidOperationException>(() => session.Pipeline.BeginTick(Time));
                Assert.AreSame(rejected, Assert.Catch(() => session.Dispose()));
                Assert.IsFalse(session.Pipeline.IsDisposed);
                Assert.AreNotEqual(SessionState.Disposed, session.State);
                Assert.AreEqual(0, callback.Destroys); Assert.AreEqual(0, sync.Disposals);
                Assert.IsTrue(session.World.Table(Things).Handles.IsCreated);
                SetCompletionHook(session.Pipeline, null);
                session.Sync();
                Assert.IsFalse(session.Pipeline.HasPendingTick); Assert.AreEqual(1, sync.Calls);
                Assert.AreEqual(0, session.Pipeline.Stats.TickCount);
                callback.Callback = null; session.Step();
                Assert.AreEqual(1, session.Pipeline.Stats.TickCount);
            }
            finally { SetCompletionHook(session.Pipeline, null); }
        }

        [TestCase(false), TestCase(true)]
        public void SimulatedSerialCompletionRejectionRetainsReturnedWorkAndSecondaryDiagnostic(bool recoveryAlsoRejects)
        {
            var first = new Exception("simulated serial pre-completion rejection");
            var secondary = new Exception("simulated recovery pre-completion rejection");
            var sync = new SyncProbe(); var later = new CallbackSystem();
            using var session = Create(sync, new WriterSystem(), later);
            var table = session.World.Table(Things);
            session.Pipeline.SerialProfiling = true;
            int calls = 0;
            try
            {
                SetCompletionHook(session.Pipeline, _ => {
                    if (++calls == 1) throw first;
                    if (recoveryAlsoRejects) throw secondary;
                });
                Assert.AreSame(first, Assert.Catch(() => session.Pipeline.BeginTick(Time)));
                Assert.AreEqual(2, calls);
                Assert.AreEqual(recoveryAlsoRejects, session.Pipeline.HasPendingTick);
                Assert.AreEqual(recoveryAlsoRejects ? 0 : 1, sync.Calls);
                Assert.AreEqual(0, session.Pipeline.Stats.TickCount);
                if (recoveryAlsoRejects)
                {
                    AssertSecondary(first, secondary);
                    Assert.AreSame(secondary, Assert.Catch(() => session.Dispose()));
                    Assert.AreEqual(0, later.Destroys); Assert.AreEqual(0, sync.Disposals);
                }
                SetCompletionHook(session.Pipeline, null);
                session.Dispose();
                Assert.AreEqual(1, later.Destroys); Assert.AreEqual(1, sync.Disposals);
                Assert.IsFalse(table.Handles.IsCreated);
            }
            finally { SetCompletionHook(session.Pipeline, null); }
        }

        [Test]
        public void SimulatedBarrierCompletionRejectionSkipsBarrierAndRetainsEarlierWork()
        {
            var first = new Exception("simulated barrier pre-completion rejection");
            var secondary = new Exception("simulated recovery pre-completion rejection");
            var sync = new SyncProbe(); var barrier = new BarrierSystem();
            using var session = Create(sync, new WriterSystem(), barrier);
            int calls = 0;
            try
            {
                SetCompletionHook(session.Pipeline, _ => { if (++calls == 1) throw first; throw secondary; });
                Assert.AreSame(first, Assert.Catch(() => session.Pipeline.BeginTick(Time)));
                AssertSecondary(first, secondary);
                Assert.AreEqual(0, barrier.Calls); Assert.AreEqual(0, sync.Calls);
                Assert.IsTrue(session.Pipeline.HasPendingTick);
                SetCompletionHook(session.Pipeline, null);
                session.Sync();
                Assert.AreEqual(1, sync.Calls); Assert.IsFalse(session.Pipeline.HasPendingTick);
            }
            finally { SetCompletionHook(session.Pipeline, null); }
        }

        static void SetCompletionHook(TickPipeline pipeline, Action<JobHandle> hook) =>
            typeof(TickPipeline).GetField("m_BeforeCompleteForTesting", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(pipeline, hook);

        sealed class BarrierSystem : SimSystemBase
        {
            public int Calls;
            public override SimPhase Phase => SimPhase.Move;
            public override void Declare(AccessDeclaration access) { }
            public override JobHandle OnTick(in SimContext context, JobHandle dependency) { Calls++; return dependency; }
        }
#endif

        static void Reenter(SimSession session, string operation)
        {
            switch (operation)
            {
                case "begin": session.Pipeline.BeginTick(Time); break;
                case "end": session.Pipeline.EndTick(); break;
                case "reset": session.Pipeline.Reset(); break;
                case "write": using (var writer = new BinaryWriter(new MemoryStream())) session.Pipeline.WriteSnapshot(writer); break;
                case "read": using (var reader = new BinaryReader(new MemoryStream(new byte[] { 0 }))) session.Pipeline.ReadSnapshot(reader); break;
                case "pipeline-dispose": session.Pipeline.Dispose(); break;
                case "session-dispose": session.Dispose(); break;
                default: throw new ArgumentException(operation);
            }
        }

        static void AssertSecondary(Exception primary, Exception secondary)
        {
            var errors = primary.Data[CleanupErrors.DataKey] as AggregateException;
            Assert.IsNotNull(errors);
            CollectionAssert.Contains(errors.Flatten().InnerExceptions, secondary);
        }

        static SimSession Create(SyncProbe sync, params ISimSystem[] systems)
        {
            var session = new SimSession(new[] { new Module(sync, systems) }, SessionSettings.Default, 1);
            session.World.CreateEntity(Things, out _);
            return session;
        }

        sealed class Module : IGameplayModule
        {
            readonly SyncProbe m_Sync; readonly ISimSystem[] m_Systems;
            public Module(SyncProbe sync, ISimSystem[] systems) { m_Sync = sync; m_Systems = systems; }
            public string Id => "TickFailure";
            public void DeclareData(WorldLayout layout)
            { layout.Table(Things, 2).Column(Values).Column(Undeclared); layout.Resource(SyncKey, m_Sync); }
            public void RegisterSystems(SystemRegistry registry) { foreach (var system in m_Systems) registry.Add(system); }
        }

        sealed class SyncProbe : ISyncResource, IDisposable
        {
            public Action Callback; public int Calls, Disposals;
            public void OnSync() { Calls++; Callback?.Invoke(); }
            public void Dispose() { Disposals++; }
        }

        sealed class CallbackSystem : SimSystemBase
        {
            public Action Callback; public int Destroys;
            public override SimPhase Phase => SimPhase.Move;
            // Declared so this isn't a barrier: an earlier returned job stays in the scheduler graph.
            public override void Declare(AccessDeclaration access) => access.Write(Undeclared);
            public override JobHandle OnTick(in SimContext context, JobHandle dependency) { Callback?.Invoke(); return dependency; }
            public override void OnDestroy(SimWorld world) => Destroys++;
        }

        sealed class WriterSystem : SimSystemBase
        {
            public override SimPhase Phase => SimPhase.Move;
            public override void Declare(AccessDeclaration access) => access.Write(Values);
            public override JobHandle OnTick(in SimContext context, JobHandle dependency) =>
                new WriteJob { Values = context.Column(Values) }.Schedule(dependency);
        }

        sealed class PrivateJobSystem : SimSystemBase
        {
            public Exception Failure; public bool CompletedPrivateJob;
            public override SimPhase Phase => SimPhase.Move;
            public override void Declare(AccessDeclaration access) => access.Write(Values);
            public override JobHandle OnTick(in SimContext context, JobHandle dependency)
            {
                var privateHandle = new WriteJob { Values = context.Column(Values) }.Schedule(dependency);
                try { throw Failure; }
                finally { privateHandle.Complete(); CompletedPrivateJob = true; }
            }
        }

        struct WriteJob : IJob
        {
            public NativeArray<int> Values;
            public void Execute() => Values[0] = 37;
        }
    }
}
