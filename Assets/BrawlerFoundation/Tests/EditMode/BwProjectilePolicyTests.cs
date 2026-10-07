using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Combat;
using SPF.L2.Weapons;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    public class BwProjectilePolicyTests
    {
        static BwTestWorld Setup()
        {
            var t = new BwTestWorld(belt: BwBeltConfig.Default, weapons: true);
            t.World.ClearLevel(); t.Game.Flow = BwFlow.Fighting;
            BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0); BwSpawner.Spawn(t.World, 1, new float2(6, 0), -1, 0);
            var f = t.Info(1); f.Hp = f.MaxHp = 10000; f.Cooldown = 100; t.World.Column(BwKeys.Info).Set(1, f);
            var w = t.World.Resource(BwWeapons.Key); w.RequestEquip(WeaponProfiles.Staff); t.Step(w.Current.EquipTicks + 30);
            t.Press(BwButton.Punch); t.Step(w.Current.ReleaseTick); return t;
        }
        [Test]
        public void FallingTargetEntersHurtHeightAfterInitialGroundOverlapAndIsActuallyDamaged()
        {
            using var t = Setup(); var w = t.World.Resource(BwWeapons.Key); Assert.AreEqual(1, w.ActiveProjectiles);
            w.CollisionDebug.Enabled = true;
            var shot = w.Projectiles[0]; shot.Position = shot.Previous = t.World.Column(BwBeltKeys.Ground)[1]; w.Projectiles[0] = shot;
            var m = t.World.Column(BwBeltKeys.Motion)[1]; m.Height = m.PreviousHeight = 3; m.HeightVelocity = -120; t.World.Column(BwBeltKeys.Motion).Set(1, m);
            t.Step();
            Assert.AreEqual(9976, t.Info(1).Hp, .001f); Assert.AreEqual(0, w.ActiveProjectiles); Assert.AreEqual(1, w.AcceptedHits);
            Assert.IsTrue(w.CollisionDebug.HasAccepted); Assert.Greater(w.CollisionDebug.LastAccepted.Fraction, 0); Assert.Less(w.CollisionDebug.LastAccepted.Fraction, 1);
            Assert.AreEqual(CombatContactReason.Accepted, w.CollisionDebug.LastAccepted.Reason);
        }
        [Test]
        public void HeightSeparatedTargetDoesNotConsumeProjectileOrFakeAcceptedTrace()
        {
            using var t = Setup(); var w = t.World.Resource(BwWeapons.Key); w.CollisionDebug.Enabled = true;
            var shot = w.Projectiles[0]; shot.Position = shot.Previous = t.World.Column(BwBeltKeys.Ground)[1]; w.Projectiles[0] = shot;
            var m = t.World.Column(BwBeltKeys.Motion)[1]; m.Height = m.PreviousHeight = 4; m.HeightVelocity = 0; t.World.Column(BwBeltKeys.Motion).Set(1, m);
            t.Step(); Assert.AreEqual(10000, t.Info(1).Hp); Assert.AreEqual(1, w.ActiveProjectiles); Assert.IsFalse(w.CollisionDebug.HasAccepted);
            bool heightMiss = false; for (int i = 0; i < w.CollisionDebug.Count; i++) heightMiss |= w.CollisionDebug.Entries[i].Reason == CombatContactReason.HeightMiss;
            Assert.IsTrue(heightMiss);
        }
        [Test]
        public void EnablingTraceDoesNotChangeSavedCombatAndResetClearsItsContacts()
        {
            using var t = Setup(); var w = t.World.Resource(BwWeapons.Key); byte[] before = t.Session.CaptureSnapshot();
            w.CollisionDebug.Enabled = true; w.CollisionDebug.Begin(w.Tick); w.CollisionDebug.Record(new CombatContactTrace { Reason = CombatContactReason.Accepted, DamageOutcome = CombatDamageOutcome.Applied });
            CollectionAssert.AreEqual(before, t.Session.CaptureSnapshot()); t.Session.RestoreSnapshot(before);
            Assert.AreEqual(0, w.CollisionDebug.Count); Assert.IsFalse(w.CollisionDebug.HasAccepted);
        }
    }
}
