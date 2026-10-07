using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.L2.Skills;
using SPF.Testing;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvComposedPulseTests
    {
        static SvTestWorld Create(bool repulse = true, int history = 8, bool start = true, bool shortGuard = false) => new SvTestWorld(start: start, tweak: c =>
        {
            c.MobileSkills = c.WeaponCombat = true;
            c.ComposedPulse = repulse ? SvPulseDefinition.Repulse : SvPulseDefinition.Wide; c.ComposedPulse.Targets = history;
            c.Capacity.Enemies = 16; c.Capacity.Bullets = 32; c.Capacity.Gems = 32; c.Capacity.Events = 16;
            c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0; c.Settings.XpBase = 100000;
            if (shortGuard) { c.Settings.Variant = SvVariant.GuardBeacon; c.Settings.BeaconHp = 100; c.Settings.BeaconRadius = .9f; c.Settings.GuardDurationTicks = 3; }
            foreach (var e in c.Enemies) { e.Speed = 0; e.Hp = 100; e.Damage = 0; e.Shooter = false; }
        });
        static SvComposedPulseState State(SvTestWorld t) => t.World.Resource(SvComposedPulseState.Key);
        static void Begin(SvTestWorld t) { t.Game.Input = new InputFrame { Pressed = 1 }; t.Step(); t.Game.Input = default; }
        static float Hp(SvTestWorld t, int row = 0) => t.World.Column(SvKeys.Info)[row].Hp;
        static byte[] Save(SvComposedPulseState s) { using var stream = new MemoryStream(); using var w = new BinaryWriter(stream); s.WriteSnapshot(w); return stream.ToArray(); }
        static void Load(SvComposedPulseState s, byte[] bytes) { using var stream = new MemoryStream(bytes); using var r = new BinaryReader(stream); s.ReadSnapshot(r); }

        [TestCase(false)] [TestCase(true)]
        public void DataVariantAndLocalRuleReleaseOnceThroughExistingDamageResolver(bool repulse)
        {
            using var t = Create(repulse); t.Spawn(1, new float2(4.8f, 0)); t.Spawn(1, new float2(-6, 0));
            Begin(t); Assert.AreEqual(100, Hp(t)); Assert.AreEqual(0, State(t).Timeline.Tick);
            Assert.AreEqual(1, t.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
            t.Step(2); Assert.AreEqual(100, Hp(t), "windup is not a damage event");
            t.Step(); Assert.AreEqual(80, Hp(t)); Assert.AreEqual(100, Hp(t, 1));
            Assert.AreEqual(1, State(t).LastRelease.AcceptedRequests); Assert.AreEqual(1, State(t).HitScope.Count);
            Assert.AreEqual(4.8f + (repulse ? .9f / .6f : 0), t.World.Column(SvKeys.Position)[0].x, .0001f);
            Assert.AreEqual(SvWeapons.PulsePose, t.World.Resource(SvWeapons.PoseKey).ContentId);
            t.Step(5); Assert.AreEqual(80, Hp(t)); Assert.AreEqual(1, State(t).Releases);
        }

        [Test]
        public void BusyPressesRejectWithoutSpendingAndWhiffIsStillOneCommittedAction()
        {
            using var t = Create(); Begin(t);
            for (int i = 0; i < 8; i++) { t.Game.Input = new InputFrame { Pressed = 1 }; t.Step(); }
            t.Game.Input = default;
            Assert.AreEqual(1, State(t).Starts); Assert.AreEqual(1, State(t).Releases); Assert.Zero(State(t).LastRelease.AcceptedRequests);
            Assert.AreEqual(1, t.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
            t.Step(); Begin(t); Assert.AreEqual(2, State(t).Starts); Assert.AreEqual(0, t.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
        }

        [Test]
        public void FullDamageQueueNeverRecordsFailedRequestsOrDefersARelease()
        {
            using var t = Create(); var target = t.Spawn(1, new float2(4, 0)); Begin(t); t.Step(2);
            var hits = t.World.Resource(SvKeys.Hits);
            for (int i = 0; i < hits.Capacity; i++) Assert.IsTrue(hits.TryAdd(new SvHit { Target = 0, Damage = 0 }));
            t.Step(); Assert.AreEqual(100, Hp(t)); Assert.AreEqual(0, State(t).LastRelease.AcceptedRequests); Assert.AreEqual(1, State(t).LastRelease.RejectedRequests);
            Assert.AreEqual(HitRecordResult.Added, HitHistory.Check(State(t).History, 0, State(t).History.Length, State(t).HitScope, target));
            Assert.AreEqual(0, State(t).HitScope.Count); t.Step(4); Assert.AreEqual(100, Hp(t)); Assert.AreEqual(1, State(t).Releases);
        }

        [Test]
        public void HistoryCapacityRejectsExcessWithoutGrowthAndOneTargetCannotRepeat()
        {
            using var t = Create(history: 1); t.Spawn(1, new float2(4, 0)); t.Spawn(1, new float2(-4, 0)); Begin(t); t.Step(3);
            Assert.AreEqual(1, State(t).LastRelease.AcceptedRequests); Assert.AreEqual(1, State(t).LastRelease.RejectedRequests);
            Assert.AreEqual(180, Hp(t) + Hp(t, 1)); Assert.AreEqual(1, State(t).History.Length);
            Assert.AreEqual(HitRecordResult.Duplicate, HitHistory.Check(State(t).History, 0, 1, State(t).HitScope, State(t).History[0]));
            t.Step(4); Assert.AreEqual(180, Hp(t) + Hp(t, 1));
        }

        [TestCase("blink")] [TestCase("equip")] [TestCase("hurt")] [TestCase("death")] [TestCase("menu")]
        public void AcceptedActionCancellationNeverRefundsOrReleasesLater(string reason)
        {
            using var t = Create(); t.Spawn(1, new float2(4, 0)); Begin(t); t.Step();
            if (reason == "blink") t.Game.Input = new InputFrame { Pressed = 2, Aim = new float2(-1, 0) };
            else if (reason == "equip") t.Game.Input = new InputFrame { Pressed = 1u << SvWeapons.SwitchButton };
            else if (reason == "hurt") t.Game.Hp -= 1;
            else if (reason == "death") t.Game.Hp = 0;
            else t.Game.Flow = SvFlow.Menu;
            t.Step(); t.Game.Input = default;
            Assert.IsFalse(State(t).Timeline.Running); Assert.AreEqual(1, State(t).Cancellations); Assert.AreEqual(0, State(t).Releases);
            Assert.AreEqual(1, t.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges); Assert.AreEqual(0, State(t).HitScope.Count);
            t.Step(4); Assert.AreEqual(100, Hp(t)); Assert.AreEqual(0, State(t).Releases);
        }

        [Test]
        public void ActualAcceptedHeroDamageCancelsEvenAfterHealthWasRaisedAboveStart()
        {
            using var t = Create(); t.Spawn(1, new float2(4, 0)); t.Game.Hp = 50; Begin(t); t.Step();
            t.Game.Hp = 80; t.Game.Invulnerable = 0; // e.g. an upgrade accepted during a level-choice pause
            Assert.IsTrue(t.World.Resource(SvKeys.HeroDamage).TryAdd(1f)); t.Step();
            Assert.AreEqual(79, t.Game.Hp); Assert.IsFalse(State(t).Timeline.Running); Assert.AreEqual(1, State(t).Cancellations);
            t.Step(4); Assert.AreEqual(100, Hp(t)); Assert.Zero(State(t).Releases);
            Assert.AreEqual(1, t.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
        }

        [Test]
        public void PausedAndLevelChoiceActionResumesExactlyAndSameTickRestoreDoesNotDoubleRelease()
        {
            using var a = Create(); a.Spawn(1, new float2(4, 0)); Begin(a); a.Step();
            a.Session.Pause(); byte[] paused = a.Session.CaptureSnapshot(); for (int i = 0; i < 20; i++) a.Session.Update(1f / 30f); CollectionAssert.AreEqual(paused, a.Session.CaptureSnapshot()); a.Session.Resume();
            a.Game.Flow = SvFlow.LevelUp; a.Step(6); Assert.AreEqual(1, State(a).Timeline.Tick); a.Game.Flow = SvFlow.Playing;
            byte[] beforeRelease = a.Session.CaptureSnapshot(); using var b = Create(start: false); b.Session.RestoreSnapshot(beforeRelease);
            a.Step(2); b.Step(2); Assert.AreEqual(80, Hp(a)); Assert.AreEqual(1, State(a).Releases);
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot());
            byte[] afterRelease = a.Session.CaptureSnapshot(); a.Session.RestoreSnapshot(afterRelease); a.Step(4); b.Step(4);
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot()); Assert.AreEqual(80, Hp(a)); Assert.AreEqual(1, State(a).Releases);
        }

        [Test]
        public void VersionedStateRejectsContentAndCorruptionAndResetClearsOwnerHistory()
        {
            using var t = Create(); t.Spawn(1, new float2(4, 0)); Begin(t); t.Step(3); var bytes = Save(State(t));
            var definition = State(t).Definition; definition.Damage += 1; using var different = new SvComposedPulseState(definition);
            Assert.Throws<InvalidDataException>(() => Load(different, bytes));
            bytes[4] = 2; Assert.Throws<InvalidDataException>(() => Load(different, bytes));
            t.World.ClearLevel(); Assert.AreEqual(0, State(t).Starts); Assert.IsFalse(State(t).Timeline.Running); Assert.AreEqual(0, State(t).HitScope.Count);
            Assert.AreEqual(2, t.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
        }

        [Test]
        public void OldFactoryHasNoNewStateOrTimingAndContentIsFrozenAtComposition()
        {
            using var old = new SvTestWorld(tweak: c => { c.MobileSkills = c.WeaponCombat = true; });
            Assert.IsFalse(old.World.HasResource(SvComposedPulseState.Key)); Assert.AreEqual(11, old.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Definition.Id);
            using var t = Create(); t.Config.ComposedPulse.Damage = 999; t.Spawn(1, new float2(4, 0)); Begin(t); t.Step(3); Assert.AreEqual(80, Hp(t));
        }

        [Test]
        public void ActualWinDeathRestartAndMenuDoNotRetainPendingActions()
        {
            using var t = Create(shortGuard: true); Begin(t); t.Step(2);
            Assert.AreEqual(SvFlow.Won, t.Game.Flow); Assert.IsFalse(State(t).Timeline.Running); Assert.Zero(State(t).Releases);
            t.Game.Send(SvCommandKind.Start); t.Step();
            Assert.AreEqual(SvFlow.Playing, t.Game.Flow); Assert.Zero(State(t).Starts); Assert.AreEqual(2, t.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
            Begin(t); t.Game.Hp = 1; t.Game.Invulnerable = 0; t.World.Resource(SvKeys.HeroDamage).TryAdd(2); t.Step();
            Assert.AreEqual(SvFlow.Dead, t.Game.Flow); Assert.IsFalse(State(t).Timeline.Running); Assert.Zero(State(t).Releases);
            t.Game.Send(SvCommandKind.Start); t.Step(); Assert.AreEqual(SvFlow.Playing, t.Game.Flow); Assert.Zero(State(t).Starts);
            Begin(t); t.Game.Send(SvCommandKind.Menu); t.Step();
            Assert.AreEqual(SvFlow.Menu, t.Game.Flow); Assert.Zero(State(t).Starts); Assert.Zero(State(t).HitScope.Count);
        }

        [Test]
        public void WarmComposedPulseTickRemainsAllocationFreeWithPositiveControls()
        {
            using var t = Create(); t.Spawn(1, new float2(8, 0)); t.Step(150);
            Action run = () => { for (int i = 0; i < 240; i++) { t.Game.Input = new InputFrame { Pressed = i % 20 == 0 ? 1u : 0u }; t.Step(); } };
            run(); using var probe = new ManagedAllocationProbe(); var pre = probe.Calibrate(); var result = probe.Measure(run); var post = probe.Calibrate();
            TestContext.WriteLine($"Composed pulse current-thread allocation: {result.Value} {result.Metric}; controls {pre.RetainedArrays.Value}/{pre.Empty.Value}, {post.RetainedArrays.Value}/{post.Empty.Value}.");
            Assert.Zero(result.Value); Assert.Greater(State(t).Releases, 2);
        }
    }
}
