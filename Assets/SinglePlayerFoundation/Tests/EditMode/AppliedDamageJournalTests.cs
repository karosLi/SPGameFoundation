using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Combat;
using SPF.L2.Combat;
using SPF.Runtime.World;
using SPF.Testing;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class AppliedDamageJournalTests
    {
        static readonly EntityHandle Target = new EntityHandle(2, 7);
        [Test]
        public void IndependentConsumersReadFactsWithoutDrainingOrBorrowingRows()
        {
            using var journal = new AppliedDamageJournal(8);
            var a = journal.CreateCursor(); var b = journal.CreateCursor();
            Assert.IsTrue(journal.AsWriter().Publish(Target, new float2(3, 4), .125f, false, 12));
            Assert.IsTrue(journal.AsWriter().Publish(new EntityHandle(2, 8), new float2(5, 6), 2.5f, true, 13));
            Assert.IsTrue(journal.TryRead(ref a, out var first)); Assert.AreEqual(Target, first.Target); Assert.AreEqual(.125f, first.Amount);
            Assert.AreEqual(new float2(3, 4), first.Position); Assert.AreEqual(12, first.Tick); Assert.AreEqual(1UL, first.Sequence);
            Assert.IsTrue(journal.TryRead(ref a, out var second)); Assert.IsTrue(second.Critical); Assert.AreEqual(8, second.Target.Generation);
            Assert.IsFalse(journal.TryRead(ref a, out _));
            Assert.IsTrue(journal.TryRead(ref b, out var same)); Assert.AreEqual(first.Sequence, same.Sequence); Assert.AreEqual(2, journal.Count);
            var late = journal.CreateCursor(); Assert.IsFalse(journal.TryRead(ref late, out _));
        }
        [Test]
        public void NormalFloodCannotEvictCriticalReserveAndLossIsExactPerReader()
        {
            using var journal = new AppliedDamageJournal(4);
            var cursor = journal.CreateCursor(); var writer = journal.AsWriter();
            writer.Publish(Target, 0, 1, true, 1);
            for (int i = 0; i < 10; i++) writer.Publish(Target, 0, 1, false, 2 + i);
            Assert.AreEqual(4, journal.Count); Assert.AreEqual(4, journal.HighWater); Assert.AreEqual(7UL, journal.OverwrittenNormal); Assert.Zero(journal.OverwrittenCritical);
            Assert.AreEqual(10UL, journal.AcceptedNormal); Assert.AreEqual(1UL, journal.AcceptedCritical);
            Assert.IsTrue(journal.TryRead(ref cursor, out var critical)); Assert.IsTrue(critical.Critical); Assert.AreEqual(1UL, critical.Sequence); Assert.AreEqual(7UL, cursor.Missed);
            ulong previous = critical.Sequence; int count = 1;
            while (journal.TryRead(ref cursor, out var fact)) { Assert.Greater(fact.Sequence, previous); previous = fact.Sequence; count++; }
            Assert.AreEqual(4, count); Assert.AreEqual(7UL, cursor.Missed);
            writer.Publish(Target, 0, 2, true, 20); Assert.IsTrue(journal.TryRead(ref cursor, out var latest)); Assert.AreEqual(12UL, latest.Sequence);
        }
        [Test]
        public void AlternatingLanesMergeInGlobalSequenceOrder()
        {
            using var journal = new AppliedDamageJournal(16); var cursor = journal.CreateCursor();
            for (int i = 0; i < 8; i++) journal.AsWriter().Publish(Target, 0, i + 1, (i & 1) == 1, i);
            for (ulong i = 1; i <= 8; i++) { Assert.IsTrue(journal.TryRead(ref cursor, out var fact)); Assert.AreEqual(i, fact.Sequence); }
            Assert.IsFalse(journal.TryRead(ref cursor, out _)); Assert.Zero(cursor.Missed);
        }
        [Test]
        public void InvalidOrZeroDamageAndSyntheticHandlesNeverPublish()
        {
            using var journal = new AppliedDamageJournal(4); var w = journal.AsWriter();
            Assert.IsFalse(w.Publish(default, 0, 1, false, 0));
            Assert.IsFalse(w.Publish(new EntityHandle(-1, 1), 0, 1, false, 0));
            Assert.IsFalse(w.Publish(Target, new float2(float.NaN, 0), 1, false, 0));
            foreach (float amount in new[] { 0f, -1f, float.PositiveInfinity, float.NaN }) Assert.IsFalse(w.Publish(Target, 0, amount, false, 0));
            Assert.IsFalse(w.Publish(Target, 0, 1, false, -1)); Assert.Zero(journal.Count); Assert.Zero(journal.LatestSequence);
        }
        [Test]
        public void ResetSameTickRestoreAndCrossWorldCursorCannotReplay()
        {
            using var a = new AppliedDamageJournal(4); using var b = new AppliedDamageJournal(4); var cursor = a.CreateCursor();
            a.AsWriter().Publish(Target, 0, 1, false, 5); b.AsWriter().Publish(Target, 0, 1, false, 5);
            Assert.IsFalse(b.TryRead(ref cursor, out _), "same numeric revision is not the same world");
            cursor = a.CreateCursor(true); uint revision = a.Revision;
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); a.WriteSnapshot(writer); Assert.Zero(stream.Length);
            using var reader = new BinaryReader(stream); a.ReadSnapshot(reader); Assert.AreNotEqual(revision, a.Revision); Assert.Zero(a.Count);
            a.AsWriter().Publish(Target, 0, 1, true, 5); Assert.IsFalse(a.TryRead(ref cursor, out _));
            a.AsWriter().Publish(Target, 0, 1, false, 6); Assert.IsTrue(a.TryRead(ref cursor, out var fresh)); Assert.AreEqual(6, fresh.Tick);
            a.OnReset(); Assert.Zero(a.AcceptedNormal); Assert.Zero(a.HighWater);
        }
        [Test]
        public void DeterministicCriticalCadenceClampsOverkillAndSkipsNonDamage()
        {
            using var rule = new CriticalDamageState(new CriticalDamageRule { EveryNthHit = 2, Multiplier = 2 });
            Assert.AreEqual(.25f, rule.Apply(.25f, 10, out bool first)); Assert.IsFalse(first);
            Assert.Zero(rule.Apply(0, 10, out _)); Assert.Zero(rule.Apply(float.NaN, 10, out _)); Assert.Zero(rule.Apply(1, 0, out _));
            Assert.AreEqual(1.5f, rule.Apply(20, 1.5f, out bool second)); Assert.IsTrue(second); Assert.AreEqual(2UL, rule.AcceptedHits); Assert.Zero(rule.Phase);
            Assert.AreEqual(2, rule.Apply(2, 10, out bool third)); Assert.IsFalse(third);
        }
        [Test]
        public void CriticalStateRoundTripValidatesVersionRuleAndPhase()
        {
            var config = new CriticalDamageRule { EveryNthHit = 2, Multiplier = 3 };
            using var a = new CriticalDamageState(config); using var b = new CriticalDamageState(config);
            a.Apply(1, 10, out _); using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); a.WriteSnapshot(writer); stream.Position = 0;
            using var reader = new BinaryReader(stream); b.ReadSnapshot(reader); Assert.AreEqual(3, b.Apply(1, 10, out bool critical)); Assert.IsTrue(critical);
            using var different = new CriticalDamageState(CriticalDamageRule.Default); stream.Position = 0; Assert.Throws<InvalidDataException>(() => different.ReadSnapshot(reader));
            b.OnReset(); Assert.Zero(b.AcceptedHits); Assert.Zero(b.Phase);
        }
        [Test]
        public void WarmWritesAndMultipleReadsAreAllocationFreeWithCalibratedControls()
        {
            using var journal = new AppliedDamageJournal(64); using var rule = new CriticalDamageState(CriticalDamageRule.Default);
            var a = journal.CreateCursor(); var b = journal.CreateCursor();
            Action run = () => { for (int i = 0; i < 2000; i++) { float amount = rule.Apply(.25f, 10, out bool crit); journal.AsWriter().Publish(Target, 0, amount, crit, i); journal.TryRead(ref a, out _); journal.TryRead(ref b, out _); } };
            run(); using var probe = new ManagedAllocationProbe(); probe.Calibrate(); var result = probe.Measure(run); probe.Calibrate(); Assert.Zero(result.Value);
        }
    }
}
