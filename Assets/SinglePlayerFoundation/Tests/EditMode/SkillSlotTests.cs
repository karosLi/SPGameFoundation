using System;
using System.IO;
using NUnit.Framework;
using SPF.Testing;
using SPF.Contracts;
using SPF.L2.Skills;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class SkillSlotTests
    {
        static SkillSlots Create() => new SkillSlots(new SkillSlotDefinition(1, 0, SkillActivation.Tap, 3, 2));
        static byte[] Save(SkillSlots slots) { using var s = new MemoryStream(); using var w = new BinaryWriter(s); slots.WriteSnapshot(w); return s.ToArray(); }
        static void Load(SkillSlots slots, byte[] bytes) { using var s = new MemoryStream(bytes); using var r = new BinaryReader(s); slots.ReadSnapshot(r); }

        [Test]
        public void ChargesRechargeSequentiallyOnExactPlayingTicks()
        {
            var slots = Create();
            Assert.IsTrue(slots.TryActivate(0, true)); Assert.IsFalse(slots.TryActivate(0, true), "at most once per slot per tick");
            slots.AdvanceTick(true); Assert.IsTrue(slots.TryActivate(0, true));
            Assert.AreEqual(0, slots.GetSnapshot(0).Charges); Assert.AreEqual(2, slots.GetSnapshot(0).RechargeTicks, "second charge does not restart first recharge");
            for (int i = 0; i < 100; i++) slots.AdvanceTick(false);
            Assert.AreEqual(2, slots.GetSnapshot(0).RechargeTicks);
            slots.AdvanceTick(true); Assert.AreEqual(0, slots.GetSnapshot(0).Charges);
            slots.AdvanceTick(true); Assert.AreEqual(1, slots.GetSnapshot(0).Charges); Assert.AreEqual(3, slots.GetSnapshot(0).RechargeTicks);
            for (int i = 0; i < 3; i++) slots.AdvanceTick(true);
            Assert.AreEqual(2, slots.GetSnapshot(0).Charges); Assert.AreEqual(0, slots.GetSnapshot(0).RechargeTicks);
        }

        [Test]
        public void EligibilityAndReadOnlySnapshotsCannotSpendCharges()
        {
            var slots = Create(); byte[] before = Save(slots);
            for (int i = 0; i < 50; i++) { Assert.IsFalse(slots.TryActivate(0, false)); Assert.IsFalse(slots.GetSnapshot(0, false).Ready); }
            CollectionAssert.AreEqual(before, Save(slots));
            Assert.IsFalse(slots.TryActivate(-1, true)); Assert.IsFalse(slots.TryActivate(8, true));
        }

        [Test]
        public void MidRechargeSnapshotContinuesAndResetRefills()
        {
            var a = Create(); a.TryActivate(0, true); a.AdvanceTick(true); a.TryActivate(0, true);
            var b = Create(); Load(b, Save(a));
            for (int tick = 0; tick < 25; tick++)
            { a.AdvanceTick(true); b.AdvanceTick(true); a.TryActivate(0, tick % 4 == 0); b.TryActivate(0, tick % 4 == 0); CollectionAssert.AreEqual(Save(a), Save(b)); }
            b.OnReset(); CollectionAssert.AreEqual(Save(Create()), Save(b));
        }

        [Test]
        public void SnapshotRejectsChangedDefinitionCapacityAndCorruptCharges()
        {
            var bytes = Save(Create());
            Assert.Throws<InvalidDataException>(() => Load(new SkillSlots(new SkillSlotDefinition(1, 0, SkillActivation.Tap, 4, 2)), bytes));
            using (var s = new MemoryStream(bytes)) using (var w = new BinaryWriter(s)) { s.Position = bytes.Length - 8; w.Write(3); }
            Assert.Throws<InvalidDataException>(() => Load(Create(), bytes));
            Assert.Throws<ArgumentException>(() => new SkillSlots());
            Assert.Throws<ArgumentException>(() => new SkillSlots(default(SkillSlotDefinition)));
        }

        [Test]
        public void SkillInputKeepsReleasedAimUntilTickConsumption()
        {
            var frame = new InputFrame { Pressed = 2, Aim = new float2(0, 1) };
            for (int i = 0; i < 20; i++) frame = SkillInput.Latch(frame, default, 1);
            Assert.AreEqual(2u, frame.Pressed); Assert.AreEqual(new float2(0, 1), frame.Aim);
            frame = SkillInput.Latch(frame, new InputFrame { Pressed = 1 }, 1);
            Assert.AreEqual(new float2(0, 1), frame.Aim, "other slot does not erase queued aimed cast");
            frame = SkillInput.Latch(frame, new InputFrame { Pressed = 2 }, 1);
            Assert.AreEqual(float2.zero, frame.Aim, "new zero-aim release intentionally uses facing");
        }

        [Test]
        public void LocalCancelPreservesOtherSlotsAndMovement()
        {
            var frame = new InputFrame { Pressed = 3, Held = 3, Aim = new float2(0, 1), Move = new float2(1, 0) };
            var blinkCanceled = SkillInput.CancelSlot(frame, 1, 1);
            Assert.AreEqual(1u, blinkCanceled.Pressed); Assert.AreEqual(1u, blinkCanceled.Held);
            Assert.AreEqual(frame.Move, blinkCanceled.Move); Assert.AreEqual(float2.zero, blinkCanceled.Aim);
            var pulseCanceled = SkillInput.CancelSlot(frame, 0, 1);
            Assert.AreEqual(2u, pulseCanceled.Pressed); Assert.AreEqual(frame.Aim, pulseCanceled.Aim);
            Assert.AreEqual(frame.Move, pulseCanceled.Move);
        }

        [Test]
        public void WarmedChargeGateAndSnapshotReadsAllocateNothing()
        {
            var slots = Create();
            for (int i = 0; i < 100; i++) { slots.AdvanceTick(true); slots.TryActivate(0, true); _ = slots.GetSnapshot(0); }
            Action measured = () =>
            {
                for (int i = 0; i < 10000; i++) { slots.AdvanceTick(true); slots.TryActivate(0, true); _ = slots.GetSnapshot(0); }
            };
            using var probe = new ManagedAllocationProbe();
            var calibrationBefore = probe.Calibrate();
            var sample = probe.Measure(measured);
            var calibrationAfter = probe.Calibrate();
            TestContext.WriteLine($"Skill slots, 10000 iterations after 100 warm-up iterations: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty calibration before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
            Assert.AreEqual(0, sample.Value, $"Warmed skill slot calls must allocate zero {sample.Metric}.");
        }
    }
}
