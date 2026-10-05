using SPF.Contracts;
using SPF.L2.Combat;
using SPF.L2.Stats;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace RpgFoundation
{
    /// <summary>Entity creation (main thread: ApplyCommands / Resolve phases).</summary>
    public static class RpgSpawner
    {
        /// <summary>Hero base stats for a level; attack rate and reach come from the equipped weapon family.</summary>
        public static StatBlock HeroBaseStats(RpgRuntimeConfig config, int level, int weaponGear)
        {
            var s = config.Settings;
            var weapon = config.Weapons[(int)config.HeroWeapon(weaponGear)];
            var b = new StatBlock();
            int l = math.max(level - 1, 0);
            b[Stat.MaxHealth] = s.HeroHealth + s.HeroHealthPerLevel * l;
            b[Stat.Attack] = s.HeroAttack + s.HeroAttackPerLevel * l;
            b[Stat.Armour] = s.HeroArmour + s.HeroArmourPerLevel * l;
            b[Stat.Speed] = s.HeroSpeed;
            b[Stat.AttackRate] = weapon.AttackRate;
            b[Stat.Crit] = s.HeroCrit;
            b[Stat.Regen] = s.HeroRegen;
            b[Stat.Range] = weapon.Range;
            b[Stat.MaxMana] = s.HeroMana + s.HeroManaPerLevel * l;
            b[Stat.ManaRegen] = s.HeroManaRegen;
            b[Stat.SkillPower] = 1f + 0.05f * l;
            return b;
        }

        /// <summary>Permanent modifiers of the equipped gear (re-applied on equip).</summary>
        public static void ApplyGear(ref ModifierSet mods, RpgRuntimeConfig config, int weapon, int armour)
        {
            mods.RemoveSource(ModSource.Weapon);
            mods.RemoveSource(ModSource.Armour);
            if (weapon > 0 && weapon < config.Gear.Length)
                mods.Apply(new Modifier { Source = ModSource.Weapon, Stat = Stat.Attack, Op = ModifierOp.Add, Value = config.Gear[weapon].Attack, Remaining = -1f });
            if (armour > 0 && armour < config.Gear.Length)
            {
                var g = config.Gear[armour];
                mods.Apply(new Modifier { Source = ModSource.Armour, Stat = Stat.Armour, Op = ModifierOp.Add, Value = g.Armour, Remaining = -1f });
                var health = new Modifier { Source = ModSource.Armour, Stat = Stat.MaxHealth, Op = ModifierOp.Add, Value = g.Health, Remaining = -1f };
                mods.Apply(health);
            }
        }

        public static EntityHandle SpawnHero(SimWorld world, RpgRuntimeConfig config, HeroProfile profile, float2 position)
        {
            var handle = world.CreateEntity(RpgKeys.Actor, out int row);
            if (handle.IsNull) return handle;
            var s = config.Settings;
            var baseStats = HeroBaseStats(config, profile.Level, profile.Weapon);
            var mods = new ModifierSet();
            ApplyGear(ref mods, config, profile.Weapon, profile.Armour);
            var stats = mods.Evaluate(baseStats);
            Write(world, row, position, new ActorInfo { Id = handle.Index, Kind = 0, Team = Team.Hero, Flags = ActorFlags.Hero, Radius = s.HeroRadius },
                baseStats, stats, mods, stats[Stat.MaxHealth] * math.saturate(profile.HealthFraction <= 0f ? 1f : profile.HealthFraction));
            world.Column(RpgKeys.Loadout).Set(row, config.HeroLoadout(profile.Level, profile.Weapon));
            return handle;
        }

        public static EntityHandle SpawnMonster(SimWorld world, RpgRuntimeConfig config, int kind, float2 position, int floor)
        {
            var handle = world.CreateEntity(RpgKeys.Actor, out int row);
            if (handle.IsNull) return handle;
            var def = config.Monsters[kind - 1];
            float scale = 1f + config.Settings.ScalingPerFloor * math.max(floor - 1, 0);
            var b = new StatBlock();
            b[Stat.MaxHealth] = def.Health * scale;
            b[Stat.Attack] = def.Attack * scale;
            b[Stat.Armour] = def.Armour * (1f + 0.5f * (scale - 1f));
            b[Stat.Speed] = def.Speed;
            b[Stat.AttackRate] = def.AttackRate;
            b[Stat.Crit] = 0.05f;
            b[Stat.Regen] = 0f;
            b[Stat.Range] = def.Range;
            b[Stat.SkillPower] = 1f;
            var flags = (def.Boss ? ActorFlags.Boss : 0) | (def.Ranged ? ActorFlags.Ranged : 0);
            Write(world, row, position, new ActorInfo { Id = handle.Index, Kind = (byte)kind, Team = Team.Monsters, Flags = flags, Radius = def.Radius },
                b, b, default, b[Stat.MaxHealth]);
            world.Column(RpgKeys.Loadout).Set(row, new Loadout { Weapon = def.Weapon, S0 = def.Skill });
            world.Column(RpgKeys.Brain).Set(row, new Brain { State = AIState.Idle, Home = position, WanderTarget = position });
            return handle;
        }

        static void Write(SimWorld world, int row, float2 position, in ActorInfo info, in StatBlock baseStats, in StatBlock stats, in ModifierSet mods, float health)
        {
            world.Column(RpgKeys.Position).Set(row, position);
            world.Column(RpgKeys.PrevPosition).Set(row, position);
            world.Column(RpgKeys.Facing).Set(row, new float2(0f, -1f));
            world.Column(RpgKeys.MoveIntent).Set(row, float2.zero);
            world.Column(RpgKeys.Info).Set(row, info);
            world.Column(RpgKeys.BaseStats).Set(row, baseStats);
            world.Column(RpgKeys.Stats).Set(row, stats);
            world.Column(RpgKeys.Mods).Set(row, mods);
            world.Column(RpgKeys.Health).Set(row, new Health { Current = health, Max = stats[Stat.MaxHealth] });
            world.Column(RpgKeys.Mana).Set(row, new Health { Current = stats[Stat.MaxMana], Max = stats[Stat.MaxMana] });
            world.Column(RpgKeys.Combat).Set(row, default);
            world.Column(RpgKeys.Brain).Set(row, default);
        }

        public static bool SpawnItem(SimWorld world, float2 position, ItemKind kind, int value)
        {
            var handle = world.CreateEntity(RpgKeys.Item, out int row);
            if (handle.IsNull) return false;
            world.Column(RpgKeys.ItemPosition).Set(row, position);
            world.Column(RpgKeys.ItemInfo).Set(row, new ItemInfo { Kind = kind, Value = value, Radius = kind == ItemKind.Gear ? 0.35f : 0.25f });
            return true;
        }

        public static bool SpawnProjectile(SimWorld world, in ProjectileRequest r)
        {
            var handle = world.CreateEntity(RpgKeys.Projectile, out int row);
            if (handle.IsNull) return false;
            world.Column(RpgKeys.ProjectilePosition).Set(row, r.Position);
            world.Column(RpgKeys.ProjectilePrev).Set(row, r.Position);
            world.Column(RpgKeys.ProjectileInfo).Set(row, new ProjectileInfo
            {
                Velocity = math.normalizesafe(r.Direction, new float2(1f, 0f)) * r.Speed,
                Radius = r.Radius > 0f ? r.Radius : 0.15f,
                Life = r.Life > 0f ? r.Life : 2.5f,
                Damage = r.Damage,
                CritChance = r.CritChance,
                Knockback = r.Knockback,
                ExplodeRadius = r.ExplodeRadius,
                Pierce = r.Pierce,
                LastHit = -1,
                Team = r.Team,
                OwnerId = r.OwnerId,
                Visual = r.Visual,
            });
            return true;
        }

        public static void DestroyAll(SimWorld world, TableKey key)
        {
            var table = world.Table(key);
            var handles = table.Handles;
            for (int row = table.Count - 1; row >= 0; row--)
                world.DestroyEntity(handles[row]);
        }
    }
}
