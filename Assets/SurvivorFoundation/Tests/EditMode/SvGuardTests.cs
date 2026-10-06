using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Presentation.Sprites;
using SurvivorFoundation.Presentation;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SurvivorFoundation.Tests
{
    public class SvGuardTests
    {
        static void Guard(SvConfig c)
        {
            var example = SvConfig.CreateGuardExample();
            c.Settings = example.Settings;
            Object.DestroyImmediate(example);
            c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0f;
            c.Settings.GuardDurationTicks = 3000;
            c.Settings.XpBase = 100000f;
            c.Enemies[0].Speed = 0f; c.Enemies[0].Hp = 1000f;
        }

        static void Quiet(SvTestWorld t)
        {
            t.Game.Upgrades[(int)Upgrade.Bolt] = 0;
            t.Game.AnnularTicks = t.Game.AnnularPulses = 0;
        }

        [Test]
        public void ClassicRestoresAndRewritesActual97a2b34SnapshotByteForByte()
        {
            using var t = new SvTestWorld(seed: 71, start: false, tweak: c =>
            {
                c.Capacity.Enemies = 16; c.Capacity.Bullets = 64; c.Capacity.Gems = 32; c.Capacity.Events = 128;
                c.Settings.MaxEnemies = 16; c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0f;
                c.Enemies[0].Speed = 0f; c.Enemies[0].Hp = 60f;
            });
            byte[] old = SvLegacySnapshotFixtures.Decode(SvLegacySnapshotFixtures.Checkpoint);
            t.Session.RestoreSnapshot(old);
            CollectionAssert.AreEqual(old, t.Session.CaptureSnapshot(), "classic preserves old game payload and complete world resource layout");
            Assert.AreEqual(2, t.Enemies); Assert.AreEqual(1, t.Bullets); Assert.AreEqual(2, t.Gems);
            Assert.AreEqual(1, t.Game.Commands.Count, "legacy pending commands are in the original position");
            t.Step(23);
#if SPF_DOTNET_HARNESS
            // Old fixture was captured with this harness backend. Real Burst fast-math may differ
            // in last bits, so Unity validates restoration/rewrite exactly and continuation semantically.
            CollectionAssert.AreEqual(SvLegacySnapshotFixtures.Decode(SvLegacySnapshotFixtures.Continued), t.Session.CaptureSnapshot());
#endif
            Assert.AreEqual(96.75f, t.Game.Hp, 0.0001f);
            Assert.AreEqual(3.3333323f, t.Game.Hero.x, 0.0001f);
            Assert.AreEqual(-1.6666662f, t.Game.Hero.y, 0.0001f);
        }

        [Test]
        public void ClassicRemainsDefaultAndGuardIsOptIn()
        {
            var s = SvConfig.DefaultSettings();
            Assert.AreEqual(SvVariant.Classic, s.Variant);
            Assert.IsFalse(s.AnnularSkill.Enabled);
        }

        [Test]
        public void AnnularDamageUsesSixPlayingTicksAndPausesWithUpgradeScreen()
        {
            using var t = new SvTestWorld(tweak: Guard); Quiet(t);
            t.Runtime.Settings.AnnularSkill.DamagePerSecond = 30f;
            t.Spawn(1, new float2(2.4f, 0));
            float hp = t.World.Column(SvKeys.Info)[0].Hp;
            t.Step(5);
            Assert.AreEqual(hp, t.World.Column(SvKeys.Info)[0].Hp);
            Assert.AreEqual(0, t.Game.AnnularPulses);
            t.Step();
            Assert.AreEqual(hp - 6f, t.World.Column(SvKeys.Info)[0].Hp, 0.0001f);
            Assert.AreEqual(1, t.Game.AnnularPulses);
            t.Game.Flow = SvFlow.LevelUp; t.Step(90);
            Assert.AreEqual(1, t.Game.AnnularPulses);
            t.Game.Flow = SvFlow.Playing; t.Step(6);
            Assert.AreEqual(hp - 12f, t.World.Column(SvKeys.Info)[0].Hp, 0.0001f);
        }

        [Test]
        public void AnnulusPreservesHollowCenterAndExactCircleBoundary()
        {
            var s = new SvAnnularSkill { RadiusA = 2f, RadiusB = 5f, HalfWidth = 0.25f };
            Assert.IsFalse(s.Hits(float2.zero, float2.zero, 0.5f));
            Assert.IsFalse(s.Hits(float2.zero, new float2(3.5f, 0), 0.5f));
            Assert.IsTrue(s.Hits(float2.zero, new float2(1.5f, 0), 0.25f));
            Assert.IsTrue(s.Hits(float2.zero, new float2(5.5f, 0), 0.25f));
            Assert.IsFalse(s.Hits(float2.zero, new float2(5.501f, 0), 0.25f));
            Assert.AreEqual(5.25f, s.OuterRadius);
        }

        [TestCase(0f)]
        [TestCase(-3f)]
        public void NonpositiveAnnularBandsAreDisabledWithoutInvisibleCenterDamage(float disabledRadius)
        {
            var skill = new SvAnnularSkill { Enabled = true, RadiusA = 2f, RadiusB = disabledRadius, HalfWidth = 0.25f };
            Assert.IsFalse(skill.Hits(float2.zero, float2.zero, 0.1f));
            Assert.IsTrue(skill.Hits(float2.zero, new float2(2f, 0), 0.1f));
            Assert.AreEqual(2.25f, skill.OuterRadius);
            skill.RadiusA = disabledRadius;
            Assert.IsFalse(skill.Hits(float2.zero, float2.zero, 0.1f));
            Assert.AreEqual(0f, skill.OuterRadius);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EnemyBulletHitsFirstActorAlongSweep(bool heroFirst)
        {
            using var t = new SvTestWorld(tweak: c => { Guard(c); c.Settings.AnnularSkill.Enabled = false; }); Quiet(t);
            float heroHp = t.Game.Hp, beaconHp = t.Game.BeaconHp;
            t.World.Resource(SvKeys.BulletSpawns).TryAdd(new BulletSpawn
            {
                Position = heroFirst ? new float2(0, 2) : new float2(0, -6),
                Velocity = heroFirst ? new float2(0, -240) : new float2(0, 180),
                Radius = 0.2f, Damage = 7f, Life = 1f, Team = BulletTeam.Enemy, Visual = BulletVisual.EnemyOrb,
            });
            t.Step(2);
            Assert.AreEqual(heroFirst ? heroHp - 7f : heroHp, t.Game.Hp, 0.0001f);
            Assert.AreEqual(heroFirst ? beaconHp : beaconHp - 7f, t.Game.BeaconHp, 0.0001f);
        }

        [Test]
        public void OverlappingBandsHitOnceAndBroadphaseIncludesLargeEdgeTargets()
        {
            using var t = new SvTestWorld(tweak: Guard); Quiet(t);
            t.Runtime.Settings.AnnularSkill = new SvAnnularSkill { Enabled = true, RadiusA = 5, RadiusB = 5, HalfWidth = 0.25f, DamagePerSecond = 30f, TickInterval = 6 };
            t.Spawn(1, new float2(5.5f, 0));
            var infos = t.World.Column(SvKeys.Info); var info = infos[0]; info.Radius = 0.25f; infos[0] = info;
            t.Step(6);
            Assert.AreEqual(info.Hp - 6f, t.World.Column(SvKeys.Info)[0].Hp, 0.0001f, "one pulse, including target radius beyond broadphase center");
        }

        [Test]
        public void EnemiesTargetBeaconUnlessHeroInterceptsNearby()
        {
            Assert.AreEqual(new float2(0, -4), SvGuardRules.Target(new float2(8, 0), float2.zero, new float2(0, -4), true, 3));
            Assert.AreEqual(float2.zero, SvGuardRules.Target(new float2(2, 0), float2.zero, new float2(0, -4), true, 3));
            Assert.AreEqual(float2.zero, SvGuardRules.Target(new float2(8, 0), float2.zero, new float2(0, -4), false, 3));
            using var t = new SvTestWorld(tweak: c => { Guard(c); c.Enemies[0].Speed = 2; c.Settings.AnnularSkill.Enabled = false; });
            Quiet(t); t.Game.Hero = t.Game.HeroPrev = new float2(12, 12);
            var from = new float2(0, 5); t.Spawn(1, from); t.Step(30);
            Assert.Less(math.distance(t.World.Column(SvKeys.Position)[0], t.Runtime.Settings.BeaconPosition), math.distance(from, t.Runtime.Settings.BeaconPosition));
        }

        [Test]
        public void GuardArenaKeepsHeroAndEnemiesInsideBounds()
        {
            using var t = new SvTestWorld(tweak: c => { Guard(c); c.Settings.AnnularSkill.Enabled = false; }); Quiet(t);
            float half = t.Runtime.Settings.ArenaHalf;
            t.Game.Hero = t.Game.HeroPrev = new float2(half - 0.01f, half - 0.01f);
            t.Input(new float2(1, 1)); t.Spawn(1, new float2(-half, -half)); t.Step(90);
            Assert.LessOrEqual(math.abs(t.Game.Hero.x), half); Assert.LessOrEqual(math.abs(t.Game.Hero.y), half);
            var p = t.World.Column(SvKeys.Position)[0];
            Assert.LessOrEqual(math.abs(p.x), half); Assert.LessOrEqual(math.abs(p.y), half);
        }

        [Test]
        public void BeaconIsSolidAndContactDamageHasFixedTickCooldown()
        {
            using var t = new SvTestWorld(tweak: c => { Guard(c); c.Settings.AnnularSkill.Enabled = false; c.Settings.BeaconHurtCooldownTicks = 6; });
            Quiet(t); t.Game.Hero = t.Game.HeroPrev = new float2(12, 12);
            var beacon = t.Runtime.Settings.BeaconPosition;
            t.Spawn(1, beacon);
            float hp = t.Game.BeaconHp;
            t.Step(2);
            Assert.Less(t.Game.BeaconHp, hp);
            float after = t.Game.BeaconHp;
            Assert.GreaterOrEqual(math.distance(t.World.Column(SvKeys.Position)[0], beacon), t.Runtime.Settings.BeaconRadius + t.World.Column(SvKeys.Info)[0].Radius - 0.0001f);
            t.Step(4); Assert.AreEqual(after, t.Game.BeaconHp);
            t.World.Resource(SvKeys.HeroDamage).TryAdd(SvDamage.ToBeacon(4f)); t.Step(2);
            t.World.Resource(SvKeys.HeroDamage).TryAdd(SvDamage.ToBeacon(4f)); t.Step();
            Assert.Less(t.Game.BeaconHp, after);
        }

        [Test]
        public void WaveDeadlineRequiresClearingAndFailureWinsOverVictory()
        {
            using var t = new SvTestWorld(tweak: c => { Guard(c); c.Settings.GuardDurationTicks = 12; c.Settings.AnnularSkill.Enabled = false; }); Quiet(t);
            t.Spawn(1, new float2(8, 8)); t.Step(30);
            Assert.AreEqual(SvFlow.Playing, t.Game.Flow, "remaining enemies must be cleared");
            t.World.Resource(SvKeys.Hits).TryAdd(new SvHit { Target = 0, Damage = 1e6f }); t.Step(3);
            Assert.AreEqual(SvFlow.Won, t.Game.Flow);
            int ticks = t.Game.RunTicks; t.Step(30); Assert.AreEqual(ticks, t.Game.RunTicks);
            t.Game.Send(SvCommandKind.Start); t.Step(); Quiet(t);
            t.Game.RunTicks = 12;
            t.World.Resource(SvKeys.HeroDamage).TryAdd(SvDamage.ToBeacon(1e6f)); t.Step();
            Assert.AreEqual(SvFlow.Dead, t.Game.Flow); Assert.AreEqual(SvLossReason.BeaconLost, t.Game.LossReason);
        }

        [Test]
        public void HeroDeathRestartAndMenuResetObjectivesAndPools()
        {
            using var t = new SvTestWorld(tweak: Guard); Quiet(t);
            t.World.Resource(SvKeys.HeroDamage).TryAdd(1e6f); t.Step();
            Assert.AreEqual(SvFlow.Dead, t.Game.Flow); Assert.AreEqual(SvLossReason.HeroFell, t.Game.LossReason);
            t.Game.Send(SvCommandKind.Start); t.Step();
            Assert.AreEqual(SvFlow.Playing, t.Game.Flow); Assert.AreEqual(SvLossReason.None, t.Game.LossReason);
            Assert.AreEqual(t.Runtime.Settings.BeaconHp, t.Game.BeaconHp);
            Assert.AreEqual(1, t.Game.RunTicks); Assert.AreEqual(0, t.Game.AnnularPulses);
            t.Spawn(1, new float2(8, 8)); t.Game.Send(SvCommandKind.Menu); t.Step();
            Assert.AreEqual(SvFlow.Menu, t.Game.Flow); Assert.AreEqual(0, t.Enemies); Assert.AreEqual(0, t.Bullets); Assert.AreEqual(0, t.Gems);
        }

        [Test]
        public void GuardSnapshotsResumeAnnularCadenceAndDamageExactly()
        {
            using var a = new SvTestWorld(seed: 42, tweak: Guard); Quiet(a);
            a.Spawn(1, new float2(2.4f, 0)); a.Step(17);
            var snapshot = a.Session.CaptureSnapshot();
            a.Step(41);
            using var b = new SvTestWorld(seed: 42, tweak: Guard, start: false);
            b.Session.RestoreSnapshot(snapshot); b.Step(41);
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot());
        }

        [Test]
        public void GuardKeepsBoundedEntityAndEventCapacity()
        {
            using var t = new SvTestWorld(tweak: c => { Guard(c); c.Capacity.Enemies = 8; c.Capacity.Events = 8; c.Settings.MaxEnemies = 8; }); Quiet(t);
            for (int i = 0; i < 100; i++) t.Spawn(1, new float2(2.4f, 0));
            Assert.AreEqual(8, t.Enemies);
            t.Step(60);
            Assert.LessOrEqual(t.Enemies, 8);
            Assert.LessOrEqual(t.World.Resource(SvKeys.Hits).Count, 16);
            Assert.AreEqual(10, t.Game.AnnularPulses);
        }

        [Test]
        public void GuardFixedTicksAllocateNoManagedMemoryAfterWarmup()
        {
            using var t = new SvTestWorld(tweak: Guard); Quiet(t);
            for (int i = 0; i < 24; i++) t.Spawn(1, new float2(5.4f, i * 0.01f));
            t.Step(30);
            Assert.That(() => t.Step(60), AllocConstraint.None);
        }

        [Test]
        public void TranslucentSortIsFarToNearAndStableAtEqualDepth()
        {
            using var sprites = new NativeArray<PackedSprite>(5, Allocator.TempJob);
            using var scratch = new NativeArray<PackedSprite>(5, Allocator.TempJob);
            float[] depths = { 2, 4, 2, 8, 2 };
            var writable = sprites;
            for (int i = 0; i < 5; i++) writable[i] = PackedSprite.Pack(new float2(i, 0), new float2(1), new float4(0, 0, 1, 1), depths[i], new float4(1));
            new SvSpriteOrder { Sprites = sprites, Scratch = scratch, Count = 5 }.Execute();
            int[] expected = { 3, 1, 0, 2, 4 };
            for (int i = 0; i < 5; i++) Assert.AreEqual(expected[i], sprites[i].Center.x);
        }

        [Test]
        public void SmoothArtHasRealAlphaCoverageAndPixelStyleStaysPointFiltered()
        {
            using var smooth = SvArt.Build(1, _ => Color.green, SvArtStyle.SmoothOutline);
            Assert.AreEqual(FilterMode.Bilinear, smooth.Sheet.Texture.filterMode);
#if !SPF_DOTNET_HARNESS
            int fractional = 0;
            foreach (var p in smooth.Sheet.Texture.GetPixels32()) if (p.a > 0 && p.a < 255) fractional++;
            Assert.Greater(fractional, 100, "supersampled coverage must reach the translucent renderer");
#endif
            using var pixel = SvArt.Build(1, _ => Color.green);
            Assert.AreEqual(FilterMode.Point, pixel.Sheet.Texture.filterMode);
        }
    }
}
