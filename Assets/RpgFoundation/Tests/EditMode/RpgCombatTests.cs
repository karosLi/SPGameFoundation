using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Stats;
using Unity.Mathematics;

namespace RpgFoundation.Tests
{
    /// <summary>Weapons, action phases, hit reactions and skills.</summary>
    public class RpgCombatTests
    {
        static RpgTestWorld Arena(int level = 1)
        {
            var t = new RpgTestWorld();
            t.ClearMonsters();
            if (level > 1)
            {
                t.Game.Profile.Level = level;
                t.World.Column(RpgKeys.Loadout).Set(t.HeroRow, t.Runtime.HeroLoadout(level, t.Game.Profile.Weapon));
            }
            return t;
        }

        static void Equip(RpgTestWorld t, WeaponKind kind)
        {
            t.Game.Profile.Inventory.Add(t.Runtime.GearId(kind, 1));
            t.Game.Send(RpgCommandKind.Equip, t.Game.Profile.Inventory.Count - 1);
            t.Step();
            Assert.AreEqual(kind, t.World.Column(RpgKeys.Loadout)[t.HeroRow].Weapon);
        }

        float Health(RpgTestWorld t, EntityHandle h) => t.World.Column(RpgKeys.Health)[t.Row(h)].Current;

        [Test]
        public void DamageLandsWhenTheWindupEnds()
        {
            using var t = Arena();
            var slime = t.SpawnMonster(1, t.FreeSpotNearHero(0.95f));
            float before = Health(t, slime);
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Attack });
            t.Step();
            var c = t.World.Column(RpgKeys.Combat)[t.HeroRow];
            Assert.AreEqual(ActionPhase.Windup, c.Phase, "the swing winds up first");
            Assert.AreEqual(before, Health(t, slime), "no damage during the wind-up");
            t.Step(6);
            Assert.Less(Health(t, slime), before, "the strike landed after the wind-up");
        }

        [Test]
        public void WeaponFamiliesChangeReachArcAndProjectiles()
        {
            using var t = Arena();
            // A rooted slime 2 m away: out of a sword's reach, inside a spear's.
            var slime = t.SpawnMonster(1, t.FreeSpotNearHero(2.0f));
            var stats = t.World.Column(RpgKeys.BaseStats);
            var b = stats[t.Row(slime)];
            b[Stat.Speed] = 0f;
            stats[t.Row(slime)] = b;
            float before = Health(t, slime);
            for (int i = 0; i < 20; i++) { t.Input(new InputFrame { Held = 1u << RpgButton.Attack }); t.Step(); }
            Assert.AreEqual(before, Health(t, slime), 1e-3f, "sword does not reach 2 m");
            Equip(t, WeaponKind.Spear);
            t.World.Column(RpgKeys.Position).Set(t.Row(slime), t.HeroPosition + math.normalizesafe(t.World.Column(RpgKeys.Position)[t.Row(slime)] - t.HeroPosition) * 2f);
            for (int i = 0; i < 20; i++) { t.Input(new InputFrame { Held = 1u << RpgButton.Attack }); t.Step(); }
            Assert.IsTrue(t.Row(slime) < 0 || Health(t, slime) < before, "the spear reaches it (hurt or killed)");

            Equip(t, WeaponKind.Bow);
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Attack });
            t.Step(8);
            Assert.Greater(t.World.Table(RpgKeys.Projectile).Count + t.World.Resource(RpgKeys.ProjectileRequests).Count, 0, "bows shoot arrows");
        }

        [Test]
        public void HeavyHitsKnockBackAndStaggerMonsters()
        {
            using var t = Arena();
            Equip(t, WeaponKind.Axe);
            var brute = t.SpawnMonster(1, t.FreeSpotNearHero(1.0f));   // a slime: light
            float2 start = t.World.Column(RpgKeys.Position)[t.Row(brute)];
            bool staggered = false;
            float pushed = 0f;
            for (int i = 0; i < 25 && t.Row(brute) >= 0; i++)
            {
                t.Input(new InputFrame { Held = 1u << RpgButton.Attack });
                t.Step();
                if (t.Row(brute) < 0) break;
                if (t.World.Column(RpgKeys.Combat)[t.Row(brute)].Phase == ActionPhase.Stagger) staggered = true;
                pushed = math.max(pushed, math.distance(start, t.World.Column(RpgKeys.Position)[t.Row(brute)]));
            }
            Assert.IsTrue(staggered, "the axe staggered the slime");
            Assert.Greater(pushed, 0.5f, "knocked back");
        }

        [Test]
        public void DashMovesFastAndIgnoresHits()
        {
            using var t = Arena(level: 2);
            float2 start = t.HeroPosition;
            t.World.Column(RpgKeys.Facing).Set(t.HeroRow, new float2(1f, 0f));
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Skill2 });
            t.Step();
            var c = t.World.Column(RpgKeys.Combat)[t.HeroRow];
            Assert.AreEqual(ActionPhase.Dash, c.Phase);
            Assert.Greater(c.Invulnerable, 0f);
            // A hit queued while dashing does nothing.
            float hp = t.World.Column(RpgKeys.Health)[t.HeroRow].Current;
            t.World.Resource(RpgKeys.Hits).TryAdd(new HitEvent { TargetRow = t.HeroRow, Damage = 50f, AttackerId = 999 });
            t.Step();
            Assert.AreEqual(hp, t.World.Column(RpgKeys.Health)[t.HeroRow].Current, 1e-3f, "i-frames");
            t.Step(6);
            Assert.Greater(math.distance(start, t.HeroPosition), 1.5f, "dashed (or slid along a wall)");
            Assert.Less(t.World.Column(RpgKeys.Mana)[t.HeroRow].Current, t.World.Column(RpgKeys.Mana)[t.HeroRow].Max, "mana spent");
        }

        [Test]
        public void WhirlwindAndFrostNovaHitEverythingAround()
        {
            using var t = Arena(level: 6);
            var around = new EntityHandle[4];
            for (int i = 0; i < 4; i++)
            {
                float a = i * math.PI * 0.5f + 0.3f;
                around[i] = t.SpawnMonster(3, t.HeroPosition + new float2(math.cos(a), math.sin(a)) * 1.4f);
            }
            var before = new float[4];
            for (int i = 0; i < 4; i++) before[i] = Health(t, around[i]);
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Skill3 });
            t.Step(30);
            for (int i = 0; i < 4; i++)
                if (t.Row(around[i]) >= 0) Assert.Less(Health(t, around[i]), before[i], $"whirlwind hit monster {i}");

            t.Input(new InputFrame { Pressed = 1u << RpgButton.Skill4 });
            t.Step(12);
            int slowed = 0;
            foreach (var h in around)
                if (t.Row(h) >= 0 && t.World.Column(RpgKeys.Mods)[t.Row(h)].HasSource(ModSource.Slow)) slowed++;
            Assert.Greater(slowed, 0, "frost nova slows");
        }

        [Test]
        public void SkillsNeedManaAndCooldown()
        {
            using var t = Arena();
            var mana = t.World.Column(RpgKeys.Mana);
            var m = mana[t.HeroRow];
            m.Current = 5f;
            mana[t.HeroRow] = m;
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Skill1 });
            t.Step();
            Assert.AreEqual(ActionPhase.None, t.World.Column(RpgKeys.Combat)[t.HeroRow].Phase, "not enough mana: nothing cast");
            m = mana[t.HeroRow];
            m.Current = m.Max;
            mana[t.HeroRow] = m;
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Skill1 });
            t.Step();
            Assert.AreEqual(ActionPhase.Cast, t.World.Column(RpgKeys.Combat)[t.HeroRow].Phase);
            t.Step(20);
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Skill1 });
            t.Step();
            Assert.AreNotEqual(ActionPhase.Cast, t.World.Column(RpgKeys.Combat)[t.HeroRow].Phase, "on cooldown");
            // Locked slots (dash unlocks at level 2) do nothing.
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Skill2 });
            t.Step();
            Assert.AreNotEqual(ActionPhase.Dash, t.World.Column(RpgKeys.Combat)[t.HeroRow].Phase);
        }

        [Test]
        public void TheBossSlamIsTelegraphedBeforeItHits()
        {
            using var t = Arena();
            var boss = t.SpawnMonster(t.Runtime.BossKind, t.FreeSpotNearHero(1.8f));
            t.Game.BossAlive = true;
            var feedback = t.World.Resource(RpgKeys.Feedback);
            float hp = t.World.Column(RpgKeys.Health)[t.HeroRow].Current;
            bool warned = false, slammed = false;
            float hpAtWarning = 0f;
            for (int i = 0; i < 90 && !slammed; i++)
            {
                feedback.Clear();
                t.Step();
                for (int e = 0; e < feedback.Count; e++)
                {
                    if (feedback[e].Kind == FeedbackKind.SlamWarning && !warned) { warned = true; hpAtWarning = t.World.Column(RpgKeys.Health)[t.HeroRow].Current; }
                    if (feedback[e].Kind == FeedbackKind.Slam) slammed = true;
                }
            }
            Assert.IsTrue(warned, "the slam is announced");
            Assert.IsTrue(slammed, "then it lands");
            Assert.Greater(hpAtWarning, 0f);
            Assert.Less(t.World.Column(RpgKeys.Health)[t.HeroRow].Current, hp, "the hero standing in it is hurt");
        }

        [Test]
        public void UnavailableSkillsDoNotBlockTheHeldAttack()
        {
            using var t = Arena();
            var slime = t.SpawnMonster(1, t.FreeSpotNearHero(0.95f));
            float before = Health(t, slime);
            // Hold attack and keep tapping a skill that is still locked at level 1.
            for (int i = 0; i < 12; i++)
            {
                t.Input(new InputFrame { Held = 1u << RpgButton.Attack, Pressed = 1u << RpgButton.Skill3 });
                t.Step();
            }
            Assert.IsTrue(t.Row(slime) < 0 || Health(t, slime) < before, "the swings went on");
        }
    
        [Test]
        public void PoisonStacksToACapAndBurnKeepsTheStrongest()
        {
            var s = new StatusState();
            var poison = StatusHit.From(StatusKind.Poison, 10f, 1f, 4f);   // 2.5 dps for 4 s
            Assert.AreEqual(2.5f, poison.Dps, 1e-5f);
            for (int i = 0; i < 5; i++) s.Apply(poison);
            Assert.AreEqual(2.5f * StatusState.PoisonStacks, s.PoisonDps, 1e-5f, "capped stacks");
            s.Apply(new StatusHit { Kind = StatusKind.Burn, Dps = 5f, Duration = 2f });
            s.Apply(new StatusHit { Kind = StatusKind.Burn, Dps = 3f, Duration = 3f });
            Assert.AreEqual(5f, s.BurnDps, 1e-5f, "the weaker burn does not replace the stronger");
            Assert.AreEqual(3f, s.Burn, 1e-5f, "but extends it");
            Assert.AreEqual(default(StatusHit).Kind, StatusHit.From(StatusKind.None, 10f, 1f, 4f).Kind);
        }

        [Test]
        public void DamageOverTimeIgnoresArmourAndExpires()
        {
            using var t = Arena();
            var baseStats = t.World.Column(RpgKeys.BaseStats);
            var b = baseStats[t.HeroRow];
            b[Stat.Armour] = 10000f;
            b[Stat.Regen] = 0f;
            baseStats[t.HeroRow] = b;
            t.Step();
            var statuses = t.World.Column(RpgKeys.Status);
            var st = statuses[t.HeroRow];
            st.Apply(new StatusHit { Kind = StatusKind.Poison, Dps = 10f, Duration = 2f });
            statuses[t.HeroRow] = st;
            float before = t.World.Column(RpgKeys.Health)[t.HeroRow].Current;
            t.Step(3 * 30);
            float after = t.World.Column(RpgKeys.Health)[t.HeroRow].Current;
            Assert.AreEqual(20f, before - after, 0.5f, "10 dps for 2 s, armour ignored");
            Assert.IsFalse(t.World.Column(RpgKeys.Status)[t.HeroRow].Poisoned, "expired");
        }

        [Test]
        public void ToxicSlimesPoisonTheHero()
        {
            using var t = Arena();
            int toxic = 0;
            for (int k = 0; k < t.Runtime.Monsters.Length; k++) if (t.Runtime.Monsters[k].Status == StatusKind.Poison) toxic = k + 1;
            Assert.Greater(toxic, 0, "a poisonous monster exists");
            t.SpawnMonster(toxic, t.FreeSpotNearHero(0.9f));
            bool poisoned = false;
            for (int i = 0; i < 200 && !poisoned; i++) { t.Step(); poisoned = t.World.Column(RpgKeys.Status)[t.HeroRow].Poisoned; }
            Assert.IsTrue(poisoned, "its claws poison");
        }

        [Test]
        public void FireballBurnsAndBurnKillsCountAsKills()
        {
            using var t = Arena();
            var brute = t.SpawnMonster(3, t.FreeSpotNearHero(3f));
            var aim = math.normalize(t.World.Column(RpgKeys.Position)[t.Row(brute)] - t.HeroPosition);
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Skill1, Aim = aim, Move = aim * 0.01f });
            bool burning = false;
            for (int i = 0; i < 60 && !burning; i++) { t.Step(); burning = t.World.Column(RpgKeys.Status)[t.Row(brute)].Burning; }
            Assert.IsTrue(burning, "the fireball sets its target on fire");

            // Leave it at 1 HP: the burn finishes it, with the usual rewards.
            t.Game.MonstersAlive = 1;
            var healths = t.World.Column(RpgKeys.Health);
            var h = healths[t.Row(brute)];
            h.Current = 1f;
            healths[t.Row(brute)] = h;
            int kills = t.Game.Profile.Kills;
            t.Step(30);
            Assert.IsFalse(t.World.Registry.IsAlive(brute), "burned to death");
            Assert.AreEqual(kills + 1, t.Game.Profile.Kills);
        }
    }
}
