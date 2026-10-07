using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Session;
using Unity.Mathematics;
using Unity.Collections;

namespace SurvivorFoundation.Tests
{
    public class SvComposedPulseSaveTests
    {
        const string Domain = "stage-e-tests.same-runtime";
        static SvTestWorld Create(bool repulse = true, bool start = true) => new SvTestWorld(start: start, tweak: c =>
        {
            c.MobileSkills = c.WeaponCombat = true; c.ComposedPulse = repulse ? SvPulseDefinition.Repulse : SvPulseDefinition.Wide; c.ComposedPulse.Targets = 8;
            c.Capacity.Enemies = 16; c.Capacity.Bullets = c.Capacity.Gems = 32; c.Capacity.Events = 16;
            c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0;
            foreach (var e in c.Enemies) { e.Hp = 100; e.Speed = 0; e.Damage = 0; e.Shooter = false; }
        });
        [Test]
        public void DistinctCompleteRecipeContinuesInFlightActionAgainstUninterruptedControl()
        {
            using var control = Create(); control.Spawn(1, new float2(4, 0)); control.Game.Input = new InputFrame { Pressed = 1 }; control.Step(); control.Game.Input = default; control.Step();
            byte[] envelope = SvComposedPulseSave.Capture(control.Session, Domain);
            Assert.Throws<InvalidDataException>(() => SvWeaponSave.Describe(control.Session, Domain), "old recipe cannot cover additional authoritative state");
            Assert.AreEqual("survivor.composed-pulse", SvComposedPulseSave.Describe(control.Session, Domain).ModeId);
            using var restored = Create(start: false); using (var stream = new MemoryStream(envelope)) SvComposedPulseSave.Restore(stream, restored.Session, Domain);
            control.Step(4); restored.Step(4); CollectionAssert.AreEqual(control.Session.CaptureSnapshot(), restored.Session.CaptureSnapshot());
            Assert.AreEqual(80, restored.World.Column(SvKeys.Info)[0].Hp); Assert.AreEqual(1, restored.World.Resource(SvComposedPulseState.Key).Releases);
        }
        [TestCase(false)] [TestCase(true)]
        public void ReplacedOrDisposedHistoryCannotMasqueradeAsTheAuthoredCapacity(bool disposed)
        {
            using var t = Create(); var pulse = t.World.Resource(SvComposedPulseState.Key);
            pulse.History.Dispose();
            pulse.History = disposed ? default : new NativeArray<EntityHandle>(pulse.Definition.Targets + 1, Allocator.Persistent);
            Assert.Throws<InvalidDataException>(() => SvComposedPulseSave.Describe(t.Session, Domain));
            Assert.Throws<InvalidDataException>(() => SvComposedPulseSave.Capture(t.Session, Domain));
        }

        [Test]
        public void RuleChangeRejectsBeforeDestinationMutationAndHasSeparateVisualIdentity()
        {
            using var source = Create(); var before = SvComposedPulseSave.Describe(source.Session, Domain);
            byte[] envelope = SvComposedPulseSave.Capture(source.Session, Domain);
            using var different = Create(false); byte[] destination = different.Session.CaptureSnapshot();
            using var stream = new MemoryStream(envelope); Assert.Throws<InvalidDataException>(() => SvComposedPulseSave.Restore(stream, different.Session, Domain));
            CollectionAssert.AreEqual(destination, different.Session.CaptureSnapshot());
            var after = SvComposedPulseSave.Describe(different.Session, Domain);
            Assert.AreNotEqual(before.ContentFingerprint, after.ContentFingerprint); Assert.AreEqual(before.VisualFingerprint, after.VisualFingerprint);
        }
    }
}
