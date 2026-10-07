using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Runtime.World;
using SPF.Runtime.Session;
using SPF.Runtime.Composition;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvDamageNumbersTests
    {
        const string Domain = "damage-number-tests.same-runtime";
        static SvTestWorld Create(bool start = true, float multiplier = 2, int capacity = 16) => new SvTestWorld(start: start, tweak: c =>
        {
            c.MobileSkills = c.WeaponCombat = c.DamageNumbers = true;
            c.ComposedPulse = SvPulseDefinition.Wide; c.ComposedPulse.Targets = 8;
            c.DamageNumberCriticalRule = new CriticalDamageRule { EveryNthHit = 2, Multiplier = multiplier }; c.DamageNumberJournalCapacity = capacity;
            c.Capacity.Enemies = 16; c.Capacity.Bullets = c.Capacity.Gems = 32; c.Capacity.Events = 16;
            c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0; c.Settings.XpBase = 100000;
            foreach (var e in c.Enemies) { e.Hp = 100; e.Speed = e.Damage = 0; e.Shooter = false; }
        });
        static void Pulse(SvTestWorld t) { t.Game.Input = new InputFrame { Pressed = 1 }; t.Step(); t.Game.Input = default; t.Step(3); }
        [TestCase(1)] [TestCase(513)]
        public void GameJournalRejectsCapacityOutsidePerFrameBudget(int capacity)
        {
            var config = SvConfig.CreateDamageNumbersExample(); config.DamageNumberJournalCapacity = capacity;
            var module = SvModule.Create(config);
            try { Assert.Throws<ArgumentException>(() => new SimSession(new IGameplayModule[] { module }, SessionSettings.Default, 7)); }
            finally { UnityEngine.Object.DestroyImmediate(module); UnityEngine.Object.DestroyImmediate(config); }
        }

        [Test]
        public void ActualPulseSettlesNormalAndCriticalFactsWithRealHandlesAndAcceptedAnchor()
        {
            using var t = Create(); var left = t.Spawn(1, new float2(4.8f, 0)); var right = t.Spawn(1, new float2(-4.8f, 0));
            var journal = t.World.Resource(AppliedDamageJournal.Key); var cursor = journal.CreateCursor();
            t.Game.Input = new InputFrame { Pressed = 1 }; t.Step(); t.Game.Input = default; t.Step(2); Assert.Zero(journal.Count);
            t.Step(); Assert.IsTrue(journal.TryRead(ref cursor, out var normal)); Assert.IsTrue(journal.TryRead(ref cursor, out var critical));
            Assert.AreEqual(left, normal.Target); Assert.AreEqual(right, critical.Target); Assert.IsFalse(normal.Critical); Assert.IsTrue(critical.Critical);
            Assert.AreEqual(20, normal.Amount); Assert.AreEqual(40, critical.Amount); Assert.AreEqual(new float2(4.8f, 0), normal.Position);
            Assert.AreEqual(80, t.World.Column(SvKeys.Info)[0].Hp); Assert.AreEqual(60, t.World.Column(SvKeys.Info)[1].Hp);
            Assert.AreEqual(2, t.World.Resource(SvComposedPulseState.Key).LastRelease.AcceptedRequests);
        }
        [Test]
        public void SettledFractionalOverkillNeverDisplaysRequestedDamageOrDeadDuplicate()
        {
            using var t = Create(); var handle = t.Spawn(1, new float2(20, 0)); var enemy = t.World.Column(SvKeys.Info)[0]; enemy.Hp = 1.5f; t.World.Column(SvKeys.Info).Set(0, enemy);
            var queue = t.World.Resource(SvKeys.Hits); queue.TryAdd(new SvHit { Target = 0, Damage = .25f }); queue.TryAdd(new SvHit { Target = 0, Damage = 20 }); queue.TryAdd(new SvHit { Target = 0, Damage = 30 });
            var journal = t.World.Resource(AppliedDamageJournal.Key); var cursor = journal.CreateCursor(); t.Step();
            Assert.IsTrue(journal.TryRead(ref cursor, out var a)); Assert.AreEqual(.25f, a.Amount); Assert.IsFalse(a.Critical);
            Assert.IsTrue(journal.TryRead(ref cursor, out var b)); Assert.AreEqual(1.25f, b.Amount); Assert.IsTrue(b.Critical); Assert.AreEqual(handle, b.Target);
            Assert.IsFalse(journal.TryRead(ref cursor, out _)); Assert.AreEqual(0, t.World.Column(SvKeys.Info)[0].Hp);
            Assert.Zero(queue.Count); Assert.Greater(t.World.Resource(SvKeys.Feedback).Count, 0, "journal readers do not drain existing presentation feedback");
            t.Step(); Assert.IsFalse(t.World.Registry.IsAlive(handle)); Assert.AreEqual(2UL, t.World.Resource(CriticalDamageState.Key).AcceptedHits);
            var recycled = t.Spawn(1, new float2(-20, 0)); Assert.AreEqual(handle.Index, recycled.Index); Assert.AreNotEqual(handle.Generation, recycled.Generation);
            var replay = journal.CreateCursor(true); Assert.IsTrue(journal.TryRead(ref replay, out var detached)); Assert.AreEqual(handle, detached.Target); Assert.AreEqual(new float2(20, 0), detached.Position);
        }
        [Test]
        public void FullRequestQueueAndInvalidTargetsProduceNoFakeFacts()
        {
            using var t = Create(); t.Spawn(1, new float2(4.8f, 0)); t.Game.Input = new InputFrame { Pressed = 1 }; t.Step(); t.Game.Input = default; t.Step(2);
            var hits = t.World.Resource(SvKeys.Hits); for (int i = 0; i < hits.Capacity; i++) hits.TryAdd(new SvHit { Target = 0, Damage = 0 });
            t.Step(); Assert.Zero(t.World.Resource(AppliedDamageJournal.Key).Count); Assert.Zero(t.World.Resource(CriticalDamageState.Key).AcceptedHits);
            Assert.AreEqual(1, t.World.Resource(SvComposedPulseState.Key).LastRelease.RejectedRequests);
            hits.TryAdd(new SvHit { Target = int.MaxValue, Damage = 20 }); hits.TryAdd(new SvHit { Target = 0, Damage = float.NaN }); t.Step(); Assert.Zero(t.World.Resource(AppliedDamageJournal.Key).Count);
        }
        [Test]
        public void CompleteSaveContinuesCriticalCadenceAndDiscardsSameTickFeedback()
        {
            using var control = Create(); control.Spawn(1, new float2(20, 0)); control.World.Resource(SvKeys.Hits).TryAdd(new SvHit { Target = 0, Damage = 3 }); control.Step();
            var journal = control.World.Resource(AppliedDamageJournal.Key); var cursor = journal.CreateCursor(true); byte[] envelope = SvDamageNumbersSave.Capture(control.Session, Domain);
            Assert.AreEqual("survivor.damage-numbers", SvDamageNumbersSave.Describe(control.Session, Domain).ModeId);
            Assert.Throws<InvalidDataException>(() => SvComposedPulseSave.Describe(control.Session, Domain));
            using var restored = Create(start: false); using (var source = new MemoryStream(envelope)) SvDamageNumbersSave.Restore(source, restored.Session, Domain);
            Assert.Zero(restored.World.Resource(AppliedDamageJournal.Key).Count); Assert.AreEqual(1, restored.World.Resource(CriticalDamageState.Key).Phase);
            foreach (var t in new[] { control, restored }) { t.World.Resource(SvKeys.Hits).TryAdd(new SvHit { Target = 0, Damage = 3 }); t.Step(); }
            CollectionAssert.AreEqual(control.Session.CaptureSnapshot(), restored.Session.CaptureSnapshot()); Assert.AreEqual(91, restored.World.Column(SvKeys.Info)[0].Hp);
            byte[] raw = control.Session.CaptureSnapshot(); control.Session.RestoreSnapshot(raw); Assert.Zero(journal.Count); Assert.IsFalse(journal.TryRead(ref cursor, out _));
        }
        [Test]
        public void ChangedCriticalRuleIsRejectedBeforeMutationAndOldModesRemainOptOut()
        {
            using var a = Create(); using var b = Create(multiplier: 3); byte[] saved = SvDamageNumbersSave.Capture(a.Session, Domain), before = b.Session.CaptureSnapshot();
            using var stream = new MemoryStream(saved); Assert.Throws<InvalidDataException>(() => SvDamageNumbersSave.Restore(stream, b.Session, Domain)); CollectionAssert.AreEqual(before, b.Session.CaptureSnapshot());
            using var old = new SvTestWorld(tweak: c => { c.MobileSkills = c.WeaponCombat = true; c.ComposedPulse = SvPulseDefinition.Wide; });
            Assert.IsFalse(old.World.HasResource(AppliedDamageJournal.Key)); Assert.IsFalse(old.World.HasResource(CriticalDamageState.Key));
            Assert.AreEqual("survivor.composed-pulse", SvComposedPulseSave.Describe(old.Session, Domain).ModeId);
        }
        [Test]
        public void ActualWorldSwapBackBeforeFirstReadCannotRetargetASettledFact()
        {
            using var t = Create(); var removed = t.Spawn(1, new float2(20, 0)); var target = t.Spawn(1, new float2(-20, 0));
            var journal = t.World.Resource(AppliedDamageJournal.Key); var cursor = journal.CreateCursor();
            t.World.Resource(SvKeys.Hits).TryAdd(new SvHit { Target = 1, Damage = 2 }); t.Step();
            Assert.IsTrue(t.World.DestroyEntity(removed)); Assert.IsTrue(t.World.Registry.TryResolve(target, out _, out int row)); Assert.Zero(row);
            t.World.Column(SvKeys.Position).Set(row, new float2(9, 9));
            var replacement = t.Spawn(1, new float2(30, 0)); Assert.AreEqual(removed.Index, replacement.Index); Assert.AreNotEqual(removed.Generation, replacement.Generation);
            Assert.IsTrue(journal.TryRead(ref cursor, out var fact)); Assert.AreEqual(target, fact.Target); Assert.AreEqual(new float2(-20, 0), fact.Position); Assert.AreEqual(2, fact.Amount);
            Assert.IsFalse(journal.TryRead(ref cursor, out _));
        }

        [Test]
        public void ProducerArrivalOrderDoesNotChangeAcceptedCriticalFacts()
        {
            using var a = Create(); using var b = Create();
            foreach (var t in new[] { a, b }) { t.Spawn(1, new float2(20, 0)); t.Spawn(1, new float2(-20, 0)); }
            var requests = new[] { new SvHit { Target = 1, Damage = 3 }, new SvHit { Target = 0, Damage = 2 },
                new SvHit { Target = 1, Damage = 1 }, new SvHit { Target = 0, Damage = .5f } };
            for (int i = 0; i < requests.Length; i++) { a.World.Resource(SvKeys.Hits).TryAdd(requests[i]); b.World.Resource(SvKeys.Hits).TryAdd(requests[requests.Length - 1 - i]); }
            a.Step(); b.Step(); CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot());
            var ja = a.World.Resource(AppliedDamageJournal.Key); var jb = b.World.Resource(AppliedDamageJournal.Key);
            var ca = ja.CreateCursor(true); var cb = jb.CreateCursor(true);
            while (ja.TryRead(ref ca, out var fa)) { Assert.IsTrue(jb.TryRead(ref cb, out var fb)); Assert.AreEqual(fa.Target, fb.Target); Assert.AreEqual(fa.Critical, fb.Critical); Assert.AreEqual(fa.Amount, fb.Amount); Assert.AreEqual(fa.Position, fb.Position); }
            Assert.IsFalse(jb.TryRead(ref cb, out _));
        }

        [Test]
        public void PressureCannotAlterAuthoritativeSimulationOrFutureCriticalHits()
        {
            using var tiny = Create(capacity: 2); using var large = Create(capacity: 64);
            foreach (var t in new[] { tiny, large }) { t.Spawn(1, new float2(20, 0)); for (int i = 0; i < 10; i++) { t.World.Resource(SvKeys.Hits).TryAdd(new SvHit { Target = 0, Damage = .25f }); t.Step(); } }
            CollectionAssert.AreEqual(tiny.Session.CaptureSnapshot(), large.Session.CaptureSnapshot()); Assert.Greater(tiny.World.Resource(AppliedDamageJournal.Key).Overwritten, 0UL);
            tiny.Game.Send(SvCommandKind.Start); tiny.Step(); Assert.Zero(tiny.World.Resource(AppliedDamageJournal.Key).Count); Assert.Zero(tiny.World.Resource(CriticalDamageState.Key).AcceptedHits);
        }
    }
}
