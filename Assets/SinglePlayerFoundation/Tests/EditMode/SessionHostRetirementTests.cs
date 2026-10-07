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
using UnityEngine;
using Object = UnityEngine.Object;

namespace SPF.Tests.EditMode
{
    public class SessionHostRetirementTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject m_Object;
        SessionHost m_Host;
        ProbeModule m_Module, m_Replacement;
        ModeDefinition m_Mode, m_ReplacementMode;
        SimSession m_Old;
        readonly List<SimSession> m_Created = new List<SimSession>();
        SessionTickLauncher Launcher => m_Object.GetComponent<SessionTickLauncher>();
        SimSession Retired => (SimSession)typeof(SessionHost).GetField("m_RetiredSession", Private)?.GetValue(m_Host);

        [SetUp]
        public void SetUp()
        {
            m_Module = ScriptableObject.CreateInstance<ProbeModule>();
            m_Replacement = ScriptableObject.CreateInstance<ProbeModule>();
            m_Mode = ModeDefinition.Create(new GameplayModuleAsset[] { m_Module }, SessionSettings.Default);
            m_ReplacementMode = ModeDefinition.Create(new GameplayModuleAsset[] { m_Replacement }, SessionSettings.Default);
            m_Object = new GameObject("Host retirement regression");
            m_Host = m_Object.AddComponent<SessionHost>(); m_Host.SessionCreated += session => m_Created.Add(session);
            m_Old = m_Host.Initialize(m_Mode, 7);
        }

        [TearDown]
        public void TearDown()
        {
            // Explicit fallback makes the intentionally-red run resource safe after its assertions.
            m_Module.Resource.Failure = null; m_Module.Resource.Callback = null;
            m_Module.Last.Failure = null; m_Module.Last.Callback = null;
            if (m_Replacement.Resource != null) { m_Replacement.Resource.Failure = null; m_Replacement.Resource.Callback = null; }
            if (m_Replacement.Last != null) { m_Replacement.Last.Failure = null; m_Replacement.Last.Callback = null; }
            Invoke("OnDestroy"); m_Old.Dispose();
            foreach (var session in m_Created) session.Dispose(); m_Created.Clear();
            Object.DestroyImmediate(m_Object); Object.DestroyImmediate(m_Mode); Object.DestroyImmediate(m_ReplacementMode);
            Object.DestroyImmediate(m_Module); Object.DestroyImmediate(m_Replacement);
        }

        [TestCase(false, false)] [TestCase(false, true)]
        [TestCase(true, false)] [TestCase(true, true)]
        public void DirectChildRetirementRetainsOwnerUntilReplacementOrDestroyRetry(bool overlap, bool destroyRetry)
        {
            m_Host.OverlapRendering = overlap;
            int notifications = 0; m_Host.SessionCreated += _ => notifications++;
            m_Module.Last.Callback = () => m_Host.Initialize(m_ReplacementMode, 8);
            // This is a deterministic in-progress child teardown, not a JobHandle.Complete failure.
            var failure = Assert.Throws<InvalidOperationException>(() => m_Old.Pipeline.Dispose());
            StringAssert.Contains("cleanup is still in progress", failure.Message);
            Assert.IsNull(m_Host.Session); Assert.IsFalse(Launcher.enabled);
            Assert.AreNotEqual(SessionState.Disposed, m_Old.State);
            Assert.AreSame(m_Old, Retired, "Unpublished storage still needs its one Host owner for a later retry.");
            Assert.AreEqual(0, m_Replacement.Declarations); Assert.Zero(notifications);
            Assert.IsTrue(m_Module.RemainingSawWorldAlive); Assert.Zero(m_Module.Resource.Disposals);
            Assert.AreEqual(1, m_Module.First.Destroys); Assert.AreEqual(1, m_Module.Last.Destroys);

            m_Host.enabled = false; Invoke("OnDisable");
            m_Host.enabled = true; Invoke("OnEnable");
            Assert.AreSame(m_Old, Retired); Assert.IsNull(m_Host.Session); Assert.IsFalse(Launcher.enabled);
            if (destroyRetry)
            {
                Invoke("OnDestroy"); Invoke("OnDestroy");
                Assert.IsNull(m_Host.Session); Assert.Zero(m_Replacement.Declarations); Assert.Zero(notifications);
            }
            else
            {
                m_Host.Initialize(m_ReplacementMode, 8);
                Assert.AreEqual(1, m_Replacement.Declarations); Assert.AreEqual(1, notifications);
                Assert.AreEqual(SessionState.Running, m_Host.Session.State);
            }
            Assert.IsNull(Retired); Assert.AreEqual(SessionState.Disposed, m_Old.State);
            Assert.AreEqual(1, m_Module.Resource.Disposals);
            Assert.AreEqual(1, m_Module.First.Destroys); Assert.AreEqual(1, m_Module.Last.Destroys);
        }

        [Test]
        public void DirectChildDestroyRetainsOwnerUntilDestroyCanFinish()
        {
            m_Module.Last.Callback = () => Invoke("OnDestroy");
            var failure = Assert.Throws<TargetInvocationException>(() => m_Old.Pipeline.Dispose());
            Assert.IsInstanceOf<InvalidOperationException>(failure.InnerException);
            Assert.IsNull(m_Host.Session); Assert.IsFalse(Launcher.enabled);
            Assert.AreSame(m_Old, Retired); Assert.IsTrue(m_Module.RemainingSawWorldAlive);
            Assert.Zero(m_Module.Resource.Disposals);
            Invoke("OnDisable"); Invoke("OnDestroy"); Invoke("OnDestroy");
            Assert.IsNull(Retired); Assert.AreEqual(SessionState.Disposed, m_Old.State);
            Assert.AreEqual(1, m_Module.Resource.Disposals);
        }

        [TestCase(false)] [TestCase(true)]
        public void ReentrantHostReplacementCannotCreateDuringOwnedCleanup(bool fromResource)
        {
            Action replace = () => m_Host.Initialize(m_ReplacementMode, 9);
            if (fromResource) m_Module.Resource.Callback = replace;
            else m_Module.Last.Callback = replace;
            Assert.Throws<InvalidOperationException>(() => m_Host.Initialize(m_ReplacementMode, 8));
            Assert.IsNull(m_Host.Session); Assert.IsNull(Retired);
            Assert.AreEqual(SessionState.Disposed, m_Old.State);
            Assert.Zero(m_Replacement.Declarations, "No replacement may overlap an outer retirement callback.");
            Assert.AreEqual(1, m_Module.Resource.Disposals);
            Assert.AreEqual(1, m_Module.First.Destroys); Assert.AreEqual(1, m_Module.Last.Destroys);
            m_Host.Initialize(m_ReplacementMode, 9); Assert.AreEqual(1, m_Replacement.Declarations);
        }

        [Test]
        public void CompletedRetirementPreservesOriginalErrorsAndClearsRetrySlot()
        {
            var original = new InvalidOperationException("system cleanup diagnostic");
            var secondary = new ApplicationException("resource cleanup diagnostic");
            m_Module.Last.Failure = original; m_Module.Resource.Failure = secondary;
            var actual = Assert.Throws<InvalidOperationException>(() => m_Host.Initialize(m_ReplacementMode, 8));
            Assert.AreSame(original, actual);
            var cleanup = actual.Data["SPF.CleanupFailures"] as AggregateException;
            Assert.IsNotNull(cleanup); CollectionAssert.Contains(cleanup.Flatten().InnerExceptions, secondary);
            Assert.AreEqual(SessionState.Disposed, m_Old.State); Assert.IsNull(Retired); Assert.IsNull(m_Host.Session);
            Assert.Zero(m_Replacement.Declarations); Assert.AreEqual(1, m_Module.Resource.Disposals);
            Assert.AreEqual(1, m_Module.First.Destroys); Assert.AreEqual(1, m_Module.Last.Destroys);
            m_Host.Initialize(m_ReplacementMode, 9); Assert.AreEqual(1, m_Replacement.Declarations);
        }

        [TestCase(false)] [TestCase(true)]
        public void ThrowingSessionCreatedBindingRetiresNewSessionAndPreservesDiagnostics(bool cleanupThrows)
        {
            var bindingFailure = new InvalidOperationException("view binding failed");
            var cleanupFailure = new ApplicationException("binding cleanup failed");
            SimSession failed = null;
            Action<SimSession> handler = created => {
                failed = created;
                if (cleanupThrows) m_Replacement.Resource.Failure = cleanupFailure;
                throw bindingFailure;
            };
            m_Host.SessionCreated += handler;
            try
            {
                var actual = Assert.Throws<InvalidOperationException>(() => m_Host.Initialize(m_ReplacementMode, 8));
                Assert.AreSame(bindingFailure, actual);
                Assert.IsNull(m_Host.Session, "A failed view binding must not leave a published/ticking Session.");
                Assert.IsFalse(Launcher.enabled); Assert.IsNull(Retired);
                Assert.AreEqual(SessionState.Disposed, failed.State);
                Assert.AreEqual(1, m_Replacement.Resource.Disposals);
                Assert.AreEqual(1, m_Replacement.First.Destroys); Assert.AreEqual(1, m_Replacement.Last.Destroys);
                Assert.AreEqual(SessionState.Disposed, m_Old.State); Assert.AreEqual(1, m_Module.Resource.Disposals);
                if (cleanupThrows)
                {
                    var cleanup = actual.Data["SPF.CleanupFailures"] as AggregateException;
                    Assert.IsNotNull(cleanup); CollectionAssert.Contains(cleanup.Flatten().InnerExceptions, cleanupFailure);
                }
            }
            finally { m_Host.SessionCreated -= handler; }
            m_Host.Initialize(m_ReplacementMode, 9);
            Assert.AreEqual(2, m_Replacement.Declarations); Assert.AreEqual(SessionState.Running, m_Host.Session.State);
            Assert.IsTrue(Launcher.enabled);
        }

        [Test]
        public void SessionCreatedCannotReenterReplacementAndOrphanOuterBinding()
        {
            int calls = 0;
            Action<SimSession> handler = _ => { if (++calls == 1) m_Host.Initialize(m_ReplacementMode, 9); };
            m_Host.SessionCreated += handler;
            try
            {
                Assert.Throws<InvalidOperationException>(() => m_Host.Initialize(m_ReplacementMode, 8));
                Assert.AreEqual(1, calls); Assert.AreEqual(1, m_Replacement.Declarations);
                Assert.AreEqual(1, m_Replacement.Resource.Disposals);
                Assert.IsNull(m_Host.Session); Assert.IsNull(Retired); Assert.IsFalse(Launcher.enabled);
            }
            finally { m_Host.SessionCreated -= handler; }
            m_Host.Initialize(m_ReplacementMode, 10); Assert.AreEqual(2, m_Replacement.Declarations);
        }

        [Test]
        public void SessionCreatedCannotReturnAnAlreadyDisposedBinding()
        {
            Action<SimSession> handler = created => created.Dispose();
            m_Host.SessionCreated += handler;
            try
            {
                Assert.Throws<InvalidOperationException>(() => m_Host.Initialize(m_ReplacementMode, 8));
                Assert.IsNull(m_Host.Session); Assert.IsNull(Retired); Assert.IsFalse(Launcher.enabled);
                Assert.AreEqual(1, m_Replacement.Resource.Disposals);
            }
            finally { m_Host.SessionCreated -= handler; }
            m_Host.Initialize(m_ReplacementMode, 9); Assert.AreEqual(2, m_Replacement.Declarations);
        }

        void Invoke(string name) => typeof(SessionHost).GetMethod(name, Private).Invoke(m_Host, null);

        sealed class ProbeModule : GameplayModuleAsset
        {
            static readonly ResourceKey<ProbeResource> Key = new ResourceKey<ProbeResource>("Host.Retirement.Resource");
            public ProbeResource Resource;
            public ProbeSystem First, Last;
            public int Declarations;
            public bool RemainingSawWorldAlive;
            public override void DeclareData(WorldLayout layout)
            { Declarations++; layout.Resource(Key, Resource = new ProbeResource()); }
            public override void RegisterSystems(SystemRegistry registry)
            {
                First = new ProbeSystem { Callback = () => RemainingSawWorldAlive = Resource.Disposals == 0 };
                Last = new ProbeSystem(); registry.Add(First).Add(Last);
            }
        }
        sealed class ProbeResource : IDisposable
        {
            public int Disposals; public Action Callback; public Exception Failure;
            public void Dispose() { Disposals++; Callback?.Invoke(); if (Failure != null) throw Failure; }
        }
        sealed class ProbeSystem : SimSystemBase
        {
            public int Destroys; public Action Callback; public Exception Failure;
            public override SimPhase Phase => SimPhase.Move;
            public override void Declare(AccessDeclaration access) { }
            public override JobHandle OnTick(in SimContext context, JobHandle dependency) => dependency;
            public override void OnDestroy(SimWorld world) { Destroys++; Callback?.Invoke(); if (Failure != null) throw Failure; }
        }
    }
}
