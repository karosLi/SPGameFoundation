using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvMobileSkillTests
    {
        static SvTestWorld Create() => new SvTestWorld(tweak: c =>
        {
            c.MobileSkills = true; c.Capacity.Events = 32; c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0;
            c.Settings.XpBase = 100000;
            foreach (var e in c.Enemies) { e.Speed = 0; e.Hp = 100; e.Damage = 0; }
        });
        static void StopWeapons(SvTestWorld t) { for (int i = 0; i < t.Game.Upgrades.Length; i++) t.Game.Upgrades[i] = 0; }

        [Test]
        public void PulseDamagesActualTargetsOnceAndHonorsChargeGate()
        {
            using var t = Create(); StopWeapons(t);
            var near = t.Spawn(1, new float2(1, 0)); t.Spawn(1, new float2(8, 0));
            t.Game.Input = new InputFrame { Pressed = 1 }; t.Step();
            Assert.AreEqual(72, t.World.Column(SvKeys.Info)[0].Hp, .001f); Assert.AreEqual(100, t.World.Column(SvKeys.Info)[1].Hp, .001f);
            t.Game.Input = new InputFrame { Pressed = 1 }; t.Step();
            Assert.AreEqual(44, t.World.Column(SvKeys.Info)[0].Hp, .001f);
            t.Game.Input = new InputFrame { Pressed = 1 }; t.Step();
            Assert.AreEqual(44, t.World.Column(SvKeys.Info)[0].Hp, .001f);
            var slots = t.World.Resource(SvMobileSkills.Key); Assert.AreEqual(0, slots.GetSnapshot(0).Charges);
            t.Game.Input = default; t.Step(88); Assert.AreEqual(1, slots.GetSnapshot(0).Charges);
        }

        [Test]
        public void PulseIncludesExactOuterTangencyButNotOutside()
        {
            using var t = Create(); StopWeapons(t);
            float tangent = SvMobileSkills.PulseRadius + t.Runtime.Enemies[0].Radius;
            t.Spawn(1, new float2(tangent, 0)); t.Spawn(1, new float2(-tangent - .01f, 0));
            t.Game.Input = new InputFrame { Pressed = 1 }; t.Step();
            Assert.AreEqual(72, t.World.Column(SvKeys.Info)[0].Hp, .001f);
            Assert.AreEqual(100, t.World.Column(SvKeys.Info)[1].Hp, .001f);
        }

        [Test]
        public void FullQueueDropsPulseTargetsButStillSpendsTheCharge()
        {
            using var t = Create(); StopWeapons(t); t.Spawn(1, new float2(1, 0));
            var hits = t.World.Resource(SvKeys.Hits);
            for (int i = 0; i < hits.Capacity; i++) Assert.IsTrue(hits.TryAdd(new SvHit { Target = 0, Damage = 0 }));
            t.Game.Input = new InputFrame { Pressed = 1 }; t.Step();
            Assert.AreEqual(100, t.World.Column(SvKeys.Info)[0].Hp, .001f);
            Assert.AreEqual(1, t.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
            t.Step(); Assert.AreEqual(100, t.World.Column(SvKeys.Info)[0].Hp, .001f, "dropped hits are not deferred to a later tick");
            t.Game.Input = new InputFrame { Pressed = 1 }; t.Step(); Assert.AreEqual(72, t.World.Column(SvKeys.Info)[0].Hp, .001f);
        }

        [Test]
        public void SimultaneousBlinkAndPulseUsePostBlinkPostMovementOrigin()
        {
            using var t = Create(); StopWeapons(t);
            t.Spawn(1, new float2(-1.4f, 0)); t.Spawn(1, new float2(7.4f, 0));
            t.Game.Input = new InputFrame { Pressed = 3, Aim = new float2(1, 0), Move = new float2(1, 0) }; t.Step();
            Assert.AreEqual(3 + t.Config.Settings.HeroSpeed / 30f, t.Game.Hero.x, .001f);
            Assert.AreEqual(100, t.World.Column(SvKeys.Info)[0].Hp, .001f, "old-origin target is out of range after blinking");
            Assert.AreEqual(72, t.World.Column(SvKeys.Info)[1].Hp, .001f, "normal movement also precedes pulse collision");
        }

        [Test]
        public void AimReleaseBlinkClampsArenaAndConsumesExactlyOneCharge()
        {
            using var t = Create(); StopWeapons(t);
            t.Game.Input = new InputFrame { Pressed = 2, Aim = new float2(0, 5) }; t.Step();
            Assert.AreEqual(new float2(0, 3), t.Game.Hero);
            t.Step(); Assert.AreEqual(new float2(0, 3), t.Game.Hero, "one shot does not repeat without new press");
            t.Game.Hero = new float2(t.Config.Settings.ArenaHalf - 1, 0);
            t.Game.Input = new InputFrame { Pressed = 2, Aim = new float2(1, 0) }; t.Step();
            Assert.AreEqual(t.Config.Settings.ArenaHalf, t.Game.Hero.x);
            Assert.AreEqual(0, t.World.Resource(SvMobileSkills.Key).GetSnapshot(1).Charges);
        }

        [Test]
        public void PausedFlowCannotRechargeOrQueueGhostSkills()
        {
            using var t = Create(); StopWeapons(t);
            t.Game.Input = new InputFrame { Pressed = 1 }; t.Step();
            var slots = t.World.Resource(SvMobileSkills.Key); int recharge = slots.GetSnapshot(0).RechargeTicks;
            t.Game.Flow = SvFlow.LevelUp; t.Game.Input = new InputFrame { Pressed = 2 }; t.Step(100);
            Assert.AreEqual(recharge, slots.GetSnapshot(0).RechargeTicks); Assert.AreEqual(0u, t.Game.Input.Pressed);
            t.Game.Flow = SvFlow.Playing; t.Step(); Assert.AreEqual(float2.zero, t.Game.Hero);
        }

        [Test]
        public void MobileSnapshotsContinueAndClassicSchemaStaysSeparate()
        {
            using var a = Create(); StopWeapons(a); a.Game.Input = new InputFrame { Pressed = 3, Aim = new float2(0, 1) }; a.Step(12);
            byte[] saved = a.Session.CaptureSnapshot(); a.Step(22);
            using var b = Create(); b.Session.RestoreSnapshot(saved); b.Step(22);
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot());
            b.Game.Send(SvCommandKind.Start); b.Step(); Assert.AreEqual(2, b.World.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
            using var classic = new SvTestWorld(); Assert.Throws<InvalidDataException>(() => classic.Session.RestoreSnapshot(saved));
        }
    }
}
