using System.IO;
using NUnit.Framework;
using SPF.Contracts;

namespace BrawlerFoundation.Tests
{
    public class BwMobileSkillTests
    {
        [Test]
        public void MobileHoldProducesActualHitAndDoesNotSpendDuringRecovery()
        {
            using var t = new BwTestWorld(mobileCombat: true); t.Duel(.9f);
            t.Game.Input = new InputFrame { Held = 1, Pressed = 1 }; t.Step(5);
            Assert.AreEqual(22f, t.Info(1).Hp, .001f);
            var slots = t.World.Resource(BwMobileSkills.Key);
            Assert.AreEqual(0, slots.GetSnapshot(0).Charges);
            t.Game.Input = new InputFrame { Pressed = 2 }; t.Step();
            Assert.AreEqual(2, slots.GetSnapshot(1).Charges, "kick cannot spend a charge while already attacking");
            t.Game.Input = default; t.Step(30);
            Assert.AreEqual(1, slots.GetSnapshot(0).Charges);
        }

        [Test]
        public void SimultaneousSkillsSpendOnlyTheExecutedKick()
        {
            using var t = new BwTestWorld(mobileCombat: true); t.Duel(1.15f);
            t.Game.Input = new InputFrame { Pressed = 3, Held = 1 }; t.Step();
            var slots = t.World.Resource(BwMobileSkills.Key);
            Assert.AreEqual(AttackKind.Kick, t.Info(t.Player).Attack);
            Assert.AreEqual(1, slots.GetSnapshot(0).Charges); Assert.AreEqual(1, slots.GetSnapshot(1).Charges);
        }

        [Test]
        public void MobileMidAttackAndRechargeSnapshotContinueExactly()
        {
            using var a = new BwTestWorld(mobileCombat: true); a.Duel(.9f); a.Press(0); a.Step(4);
            byte[] saved = a.Session.CaptureSnapshot(); a.Step(24);
            using var b = new BwTestWorld(mobileCombat: true); b.Session.RestoreSnapshot(saved); b.Step(24);
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot());
            b.Game.Send(BwCommandKind.Start); b.Step();
            Assert.AreEqual(2, b.World.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges);
            using var classic = new BwTestWorld();
            Assert.Throws<InvalidDataException>(() => classic.Session.RestoreSnapshot(saved));
        }
    }
}
