using NUnit.Framework;
using SPF.Contracts;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvGameTests
    {
        [Test]
        public void WavesGrowAroundTheHero()
        {
            using var t = new SvTestWorld();
            Assert.AreEqual(SvFlow.Playing, t.Game.Flow);
            t.Step(30 * 10);
            Assert.Greater(t.Enemies + t.Game.Kills, 20, "enemies keep coming");
            var positions = t.World.Column(SvKeys.Position);
            float far = 0f;
            for (int i = 0; i < t.Enemies; i++) far = math.max(far, math.distance(positions[i], t.Game.Hero));
            Assert.Greater(far, 8f, "spawned off-screen");
        }

        [Test]
        public void BoltsKillEnemiesWhichDropGemsThatLevelTheHeroUp()
        {
            using var t = new SvTestWorld(tweak: c => { c.Settings.SpawnPerSecond = 0f; c.Settings.SpawnGrowth = 0f; c.Settings.EliteEvery = 0f; });
            for (int i = 0; i < 4; i++) t.Spawn(1, new float2(4f + i * 0.8f, 0.3f * i));
            for (int i = 0; i < 300 && t.Game.Kills < 4; i++) t.Step();
            Assert.AreEqual(4, t.Game.Kills, "the magic bolt killed the bats");
            Assert.AreEqual(0, t.Enemies);
            Assert.AreEqual(4, t.Gems + t.Game.Xp, "each dropped a gem (some already collected: they died close by)");
            for (int i = 0; i < 4; i++) t.Spawn(1, new float2(-6f, i));

            // Gems fly to the hero inside the magnet radius and give XP; enough XP offers upgrades.
            for (int i = 0; i < 300 && t.Game.Flow != SvFlow.LevelUp; i++) t.Step();
            Assert.AreEqual(SvFlow.LevelUp, t.Game.Flow, "levelled up");
            Assert.AreEqual(SvGameState.ChoiceCount, t.Game.ChoiceCountOffered);
            Assert.AreNotEqual(t.Game.Choices[0], t.Game.Choices[1]);
            int choice = t.Game.Choices[0];
            int before = t.Game.Upgrades[choice];
            uint tick = t.Session.Clock.NextTickIndex;
            float time = t.Game.Time;
            t.Step(10);
            Assert.AreEqual(time, t.Game.Time, "the run waits while choosing");
            t.Game.Send(SvCommandKind.Choose, 0);
            t.Step();
            Assert.AreEqual(before + 1, t.Game.Upgrades[choice]);
            Assert.AreEqual(SvFlow.Playing, t.Game.Flow);
            Assert.Greater(t.Session.Clock.NextTickIndex, tick);
        }

        [Test]
        public void ShootersFireSpiralsThatHurtTheHero()
        {
            using var t = new SvTestWorld(tweak: c => { c.Settings.SpawnPerSecond = 0f; c.Settings.SpawnGrowth = 0f; c.Settings.EliteEvery = 0f; });
            t.Game.Upgrades[(int)Upgrade.Bolt] = 0;   // let the mage live
            int mage = 0;
            for (int k = 0; k < t.Runtime.EnemyKinds; k++) if (t.Runtime.Enemies[k].Shooter) mage = k + 1;
            t.Spawn(mage, new float2(6f, 0f));
            int enemyBullets = 0;
            float hp = t.Game.Hp;
            for (int i = 0; i < 300 && t.Game.Hp >= hp; i++)
            {
                t.Step();
                var infos = t.World.Column(SvKeys.BulletInfo);
                for (int b = 0; b < t.Bullets; b++) if (infos[b].Team == BulletTeam.Enemy) enemyBullets++;
            }
            Assert.Greater(enemyBullets, 0, "the mage fires patterns");
            Assert.Less(t.Game.Hp, hp, "and they hurt");
        }

        [Test]
        public void ContactDamageIsLimitedByInvulnerability()
        {
            using var t = new SvTestWorld(tweak: c => { c.Settings.SpawnPerSecond = 0f; c.Settings.SpawnGrowth = 0f; c.Settings.EliteEvery = 0f; });
            t.Game.Upgrades[(int)Upgrade.Bolt] = 0;
            for (int i = 0; i < 20; i++) t.Spawn(2, new float2(0.2f * (i % 5), 0.2f * (i / 5)));
            float hp = t.Game.Hp;
            t.Step(2);
            Assert.AreEqual(hp - t.Runtime.Enemies[1].Damage, t.Game.Hp, 1e-3f, "a swarm deals one hit, then a short invulnerability");
        }

        [Test]
        public void UpgradesAddWeapons()
        {
            using var t = new SvTestWorld(tweak: c => { c.Settings.SpawnPerSecond = 0f; c.Settings.SpawnGrowth = 0f; c.Settings.EliteEvery = 0f; });
            t.Game.Upgrades[(int)Upgrade.Nova] = 2;
            t.Game.Upgrades[(int)Upgrade.Spiral] = 3;
            t.Step(90);
            int nova = 0, spiral = 0;
            var infos = t.World.Column(SvKeys.BulletInfo);
            for (int b = 0; b < t.Bullets; b++)
            {
                if (infos[b].Visual == BulletVisual.Nova) nova++;
                if (infos[b].Visual == BulletVisual.Spiral) spiral++;
            }
            Assert.Greater(nova, 0, "nova rings");
            Assert.Greater(spiral, 10, "spiral streams");
        }

        [Test]
        public void RunsAreDeterministicAndSnapshotsResumeExactly()
        {
            byte[] Run(int ticks, out byte[] mid)
            {
                using var t = new SvTestWorld(seed: 11, tweak: c => c.Settings.SpawnPerSecond = 20f);
                mid = null;
                for (int i = 0; i < ticks; i++)
                {
                    t.Input(new float2(math.sin(i * 0.05f), math.cos(i * 0.031f)));
                    t.Step();
                    if (t.Game.Flow == SvFlow.LevelUp) { t.Game.Send(SvCommandKind.Choose, i % 3); }
                    if (i == ticks / 2) mid = t.Session.CaptureSnapshot();
                }
                return t.Session.CaptureSnapshot();
            }
            var a = Run(400, out var midA);
            var b = Run(400, out _);
            CollectionAssert.AreEqual(a, b, "same seed and input, same run");

            using var r = new SvTestWorld(seed: 11, tweak: c => c.Settings.SpawnPerSecond = 20f, start: false);
            r.Session.RestoreSnapshot(midA);
            for (int i = 201; i < 400; i++)
            {
                r.Input(new float2(math.sin(i * 0.05f), math.cos(i * 0.031f)));
                r.Step();
                if (r.Game.Flow == SvFlow.LevelUp) { r.Game.Send(SvCommandKind.Choose, i % 3); }
            }
            CollectionAssert.AreEqual(a, r.Session.CaptureSnapshot(), "a restored snapshot continues identically");
        }

        [Test]
        public void BulletsArePooledAndCompacted()
        {
            using var t = new SvTestWorld(tweak: c => { c.Settings.SpawnPerSecond = 0f; c.Settings.SpawnGrowth = 0f; c.Settings.EliteEvery = 0f; });
            t.Game.Upgrades[(int)Upgrade.Spiral] = 5;
            t.Step(60);
            Assert.Greater(t.Bullets, 50);
            Assert.AreEqual(1, t.World.Registry.AliveCount == 0 ? 1 : 0, "bullets take no registry slots");
            t.Game.Upgrades[(int)Upgrade.Spiral] = 0;
            t.Step(60);
            Assert.AreEqual(0, t.Bullets, "expired bullets were compacted away");
        }
    }
}
