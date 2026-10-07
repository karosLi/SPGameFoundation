using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Combat;
using SPF.L2.Combat;
using SPF.L2.Weapons;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    public class BwDamageNumbersTests
    {
        const string Domain = "damage-number-tests.same-runtime";
        sealed class Duel : IDisposable
        {
            readonly GameplayModuleAsset module; readonly ModeDefinition mode;
            public readonly SimSession Session;
            public SimWorld World => Session.World;
            public BwGameState Game => World.Resource(BwKeys.Game);
            public AppliedDamageJournal Journal => World.Resource(AppliedDamageJournal.Key);
            public FighterInfo Read(int row = 0) => World.Column(BwKeys.Info)[row];
            public void Write(FighterInfo value, int row = 0) => World.Column(BwKeys.Info).Set(row, value);
            public Duel(float multiplier = 2, int capacity = 32)
            {
                var belt = BwBeltConfig.Default; belt.Fighters = 16; belt.TargetsPerAttack = 8;
                mode = BwMode.CreateDamageNumbersBelt(belt, BwComposedAbilityConfig.Default,
                    new CriticalDamageRule { EveryNthHit = 2, Multiplier = multiplier }, out module, capacity);
                Session = SimSession.Create(mode, 7); Session.Start();
                BwSpawner.Spawn(World, 0, 0, 1, 0); BwSpawner.Spawn(World, 1, new float2(.9f, 0), -1, 0);
                PrepareTarget(1000); Game.Flow = BwFlow.Fighting;
            }
            public void PrepareTarget(float hp)
            {
                var target = Read(1); target.Hp = target.MaxHp = hp; target.State = FighterState.Hit; target.StateTime = -100; target.Attack = AttackKind.None; target.VelocityX = 0; Write(target, 1);
                World.Column(BwBeltKeys.Ground).Set(1, new float2(.9f, 0)); World.Column(BwBeltKeys.Motion).Set(1, default);
            }
            public void Step(int count = 1) { for (int i = 0; i < count; i++) Session.Step(); }
            public void Press(int button) { Game.Input = new InputFrame { Pressed = 1u << button }; Step(); Game.Input = default; }
            public AppliedDamageFact EnemyFact(ref DamageFactCursor cursor)
            {
                var target = World.Table(BwKeys.Fighter).Handles[1];
                while (Journal.TryRead(ref cursor, out var fact)) if (fact.Target == target) return fact;
                Assert.Fail("Expected an actual settled enemy damage fact."); return default;
            }
            public void Dispose() { Session.Dispose(); UnityEngine.Object.DestroyImmediate(mode); UnityEngine.Object.DestroyImmediate(module); }
        }
        [TestCase(1)] [TestCase(513)]
        public void GameJournalRejectsCapacityOutsidePerFrameBudget(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BwModule.CreateDamageNumbersBelt(BwBeltConfig.Default,
                BwComposedAbilityConfig.Default, CriticalDamageRule.Default, capacity));
        }

        [Test]
        public void RealComposedKickShowsNormalThenCriticalAndPreservesEarnedCreditRule()
        {
            using var t = new Duel(); var cursor = t.Journal.CreateCursor(); var target = t.World.Table(BwKeys.Fighter).Handles[1];
            t.Press(BwButton.Kick); t.Step(2); Assert.Zero(t.Journal.Count); t.Step(6);
            var normal = t.EnemyFact(ref cursor); Assert.IsFalse(normal.Critical); Assert.AreEqual(22, normal.Amount); Assert.AreEqual(target, normal.Target);
            Assert.AreEqual(6, t.World.Resource(BwComposedAbilityState.Key).HealCredit); t.Step(24); t.PrepareTarget(1000);
            t.Press(BwButton.Kick); t.Step(8); var critical = t.EnemyFact(ref cursor); Assert.IsTrue(critical.Critical); Assert.AreEqual(44, critical.Amount);
            Assert.AreEqual(956, t.Read(1).Hp); Assert.AreEqual(12, t.World.Resource(BwComposedAbilityState.Key).HealCredit);
            Assert.AreEqual(2UL, t.World.Resource(CriticalDamageState.Key).AcceptedHits);
        }
        [Test]
        public void RealWeaponContactPublishesDetachedAnchorAndClampedActualLoss()
        {
            using var t = new Duel(); t.PrepareTarget(1.5f); var cursor = t.Journal.CreateCursor();
            t.Press(0); t.Step(20); var fact = t.EnemyFact(ref cursor);
            Assert.AreEqual(1.5f, fact.Amount); Assert.IsFalse(fact.Critical); Assert.AreEqual(0, t.Read(1).Hp);
            var accepted = fact.Position; t.World.Column(BwBeltKeys.Ground).Set(1, new float2(100, 100)); Assert.AreEqual(accepted, fact.Position);
            Assert.AreEqual(FighterState.KO, t.Read(1).State);
        }
        [Test]
        public void WhiffNeverAdvancesCriticalCadenceOrEmitsFakeDamage()
        {
            using var t = new Duel(); t.World.Column(BwBeltKeys.Ground).Set(1, new float2(8, 0)); t.Press(BwButton.Kick); t.Step(30);
            Assert.Zero(t.Journal.Count); Assert.Zero(t.World.Resource(CriticalDamageState.Key).AcceptedHits); Assert.Zero(t.World.Resource(BwComposedAbilityState.Key).HealCredit);
        }
        [Test]
        public void CompleteEnvelopePreservesCriticalCadenceAndEActionsAndRestoreDiscardsFacts()
        {
            using var control = new Duel(); var cursor = control.Journal.CreateCursor(); control.Press(BwButton.Kick); control.Step(32);
            Assert.AreEqual(1, control.World.Resource(CriticalDamageState.Key).Phase); control.PrepareTarget(1000);
            control.Press(BwButton.Kick); control.Step(2); byte[] save = BwDamageNumbersSave.Capture(control.Session, Domain);
            Assert.AreEqual("brawler.damage-numbers", BwDamageNumbersSave.Describe(control.Session, Domain).ModeId);
            Assert.Throws<InvalidDataException>(() => BwComposedAbilitySave.Describe(control.Session, Domain));
            using var restored = new Duel(); using (var stream = new MemoryStream(save)) BwDamageNumbersSave.Restore(stream, restored.Session, Domain);
            Assert.Zero(restored.Journal.Count); control.Step(12); restored.Step(12);
            CollectionAssert.AreEqual(control.Session.CaptureSnapshot(), restored.Session.CaptureSnapshot()); Assert.AreEqual(956, restored.Read(1).Hp);
            var restoredCursor = restored.Journal.CreateCursor(true); var hit = restored.EnemyFact(ref restoredCursor); Assert.IsTrue(hit.Critical); Assert.AreEqual(44, hit.Amount);
            uint revision = control.Journal.Revision; byte[] raw = control.Session.CaptureSnapshot(); control.Session.RestoreSnapshot(raw);
            Assert.AreNotEqual(revision, control.Journal.Revision); Assert.Zero(control.Journal.Count); Assert.IsFalse(control.Journal.TryRead(ref cursor, out _));
        }
        [Test]
        public void DifferentRuleRejectsBeforeMutationAndRestartClearsCadence()
        {
            using var a = new Duel(); using var b = new Duel(3); byte[] save = BwDamageNumbersSave.Capture(a.Session, Domain), before = b.Session.CaptureSnapshot();
            using var stream = new MemoryStream(save); Assert.Throws<InvalidDataException>(() => BwDamageNumbersSave.Restore(stream, b.Session, Domain)); CollectionAssert.AreEqual(before, b.Session.CaptureSnapshot());
            a.Press(BwButton.Kick); a.Step(10); Assert.Greater(a.Journal.Count, 0); a.Game.Send(BwCommandKind.Start); a.Step();
            Assert.Zero(a.Journal.Count); Assert.Zero(a.World.Resource(CriticalDamageState.Key).AcceptedHits);
        }
        [Test]
        public void TinyAndLargeJournalsHaveIdenticalAuthorityUnderRealWeaponPressure()
        {
            using var tiny = new Duel(capacity: 2); using var large = new Duel(capacity: 64);
            for (int i = 0; i < 240; i++)
            {
                tiny.PrepareTarget(1000); large.PrepareTarget(1000);
                tiny.Game.Input = large.Game.Input = new InputFrame { Held = 1u << BwButton.Punch };
                tiny.Step(); large.Step();
            }
            Assert.Greater(tiny.World.Resource(CriticalDamageState.Key).AcceptedHits, 4UL);
            Assert.Greater(tiny.Journal.Overwritten, 0UL); Assert.LessOrEqual(tiny.Journal.Count, 2);
            CollectionAssert.AreEqual(tiny.Session.CaptureSnapshot(), large.Session.CaptureSnapshot());
        }

        [Test]
        public void OldComposedFactoryStillOmitsBothOptionalResources()
        {
            var mode = BwMode.CreateComposedAbilityBelt(BwBeltConfig.Default, BwComposedAbilityConfig.Default, out var module);
            try { using var session = SimSession.Create(mode, 7); Assert.IsFalse(session.World.HasResource(AppliedDamageJournal.Key)); Assert.IsFalse(session.World.HasResource(CriticalDamageState.Key));
                Assert.AreEqual("brawler.composed-ability-belt", BwComposedAbilitySave.Describe(session, Domain).ModeId); }
            finally { UnityEngine.Object.DestroyImmediate(mode); UnityEngine.Object.DestroyImmediate(module); }
        }
    }
}
