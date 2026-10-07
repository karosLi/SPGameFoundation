using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Combat;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.Weapons;
using SPF.Runtime.Scheduling;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvProjectilePolicyTests
    {
        static SvTestWorld Setup(int events = 512)
        {
            var t = new SvTestWorld(tweak: c =>
            {
                c.WeaponCombat = c.MobileSkills = true; c.WeaponProfiles = WeaponProfiles.CreateDefaults(30);
                c.Capacity.Enemies = 256; c.Capacity.Events = events;
                c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0; c.Settings.XpBase = 100000;
                foreach (var e in c.Enemies) { e.Speed = 0; e.Hp = 10000; e.Damage = 0; e.Shooter = false; }
            });
            var w = t.World.Resource(SvWeapons.Key); w.RequestEquip(WeaponProfiles.Staff); t.Step(w.Profile(WeaponProfiles.Staff).EquipTicks);
            t.Game.Input = new InputFrame { Held = 1u << SvWeapons.AttackButton, Aim = new float2(1, 0) };
            t.Step(w.Current.ReleaseTick + 2); t.Game.Input = default;
            Assert.AreEqual(1, w.ActiveProjectiles); return t;
        }
        static void BuildGrid(SvTestWorld t)
        {
            var grid = t.World.Resource(SvKeys.EnemyGrid); int count = t.Enemies;
            for (int i = 0; i < count; i++) grid.Staging.Set(i, new GridEntry { Position = t.World.Column(SvKeys.Position)[i], Radius = t.World.Column(SvKeys.Info)[i].Radius, Owner = i });
            grid.StagingCount.Set(0, count); grid.ScheduleBuild(default).Complete();
        }
        static void CollisionPass(SvTestWorld t)
        {
            t.Session.Sync(); var context = new SimContext(t.World, new TickTime(t.Session.Clock.NextTickIndex, 1f / 30f, t.Session.Clock.Elapsed));
            var pipeline = t.Session.Pipeline;
            for (int i = 0; i < pipeline.SystemCount; i++)
            {
                var system = pipeline.GetSystem(i);
                if (system.GetType().Name != "WeaponCombatSystem") continue;
                system.OnTick(context, default).Complete(); return;
            }
            Assert.Fail("Actual WeaponCombatSystem missing");
        }
        [Test]
        public void MeasuredMotionIncludesCrossingTargetBeyondAuthoredSpeedPadding()
        {
            using var t = Setup(); var w = t.World.Resource(SvWeapons.Key); w.CollisionDebug.Enabled = true;
            t.Spawn(1, new float2(6, 0)); t.World.Column(SvKeys.PrevPosition).Set(0, new float2(-6, 0));
            var shot = w.Projectiles[0]; shot.Previous = shot.Position = float2.zero; w.Projectiles[0] = shot; BuildGrid(t);
            CollisionPass(t);
            Assert.AreEqual(12f, w.CollisionDebug.MaxTargetMovement); Assert.AreEqual(1, w.CollisionDebug.MovementRowsExamined);
            Assert.AreEqual(1, w.AcceptedHits); Assert.AreEqual(0, w.ActiveProjectiles); Assert.IsTrue(w.CollisionDebug.HasAccepted); Assert.AreEqual(CombatDamageOutcome.Queued, w.CollisionDebug.LastAccepted.DamageOutcome);
            Assert.Greater(w.CollisionDebug.LastAccepted.Fraction, 0); Assert.Less(w.CollisionDebug.LastAccepted.Fraction, 1);
            Assert.AreEqual(1, t.World.Resource(SvKeys.Hits).Count, "actual adapter queues damage; normal resolver applies it");
        }
        [TestCase(1)] [TestCase(32)]
        public void MotionBoundScansTargetsOnceRegardlessOfProjectileCount(int projectiles)
        {
            using var t = Setup(); var w = t.World.Resource(SvWeapons.Key); w.CollisionDebug.Enabled = true;
            for (int i = 0; i < 16; i++) { t.Spawn(1, new float2(8, i)); t.World.Column(SvKeys.PrevPosition).Set(i, new float2(7.5f, i)); }
            var template = w.Projectiles[0]; template.Previous = template.Position = new float2(-20, -20);
            for (int i = 0; i < projectiles; i++)
            {
                w.Projectiles[i] = template; var scope = w.Scopes[i + 1];
                HitHistory.Begin(w.History, (i + 1) * w.HistoryPerAttack, w.HistoryPerAttack, ref scope, w.Owner, template.Pulse); w.Scopes[i + 1] = scope;
            }
            BuildGrid(t); CollisionPass(t);
            Assert.AreEqual(16, w.CollisionDebug.MovementRowsExamined); Assert.AreEqual(.5f, w.CollisionDebug.MaxTargetMovement);
            Assert.AreEqual(projectiles, w.ActiveProjectiles); Assert.AreEqual(0, w.AcceptedHits);
        }
        [Test]
        public void FullDamageQueueConsumesContactWithoutRecordingOrInventingAcceptedImpact()
        {
            using var t = Setup(1); var w = t.World.Resource(SvWeapons.Key); w.CollisionDebug.Enabled = true;
            t.Spawn(1, float2.zero); var shot = w.Projectiles[0]; shot.Previous = shot.Position = float2.zero; w.Projectiles[0] = shot;
            var hits = t.World.Resource(SvKeys.Hits); for (int i = 0; i < hits.Capacity; i++) Assert.IsTrue(hits.TryAdd(new SvHit { Target = 0, Damage = 1 }));
            BuildGrid(t); CollisionPass(t);
            Assert.AreEqual(0, w.ActiveProjectiles); Assert.AreEqual(0, w.AcceptedHits); Assert.IsFalse(w.CollisionDebug.HasAccepted); Assert.Greater(w.RejectedHits, 0);
            bool found = false; for (int i = 0; i < w.CollisionDebug.Count; i++) found |= w.CollisionDebug.Entries[i].Reason == CombatContactReason.QueueFull;
            Assert.IsTrue(found); Assert.AreEqual(hits.Capacity, hits.Count);
        }
    }
}
