using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;
using Unity.Mathematics;

namespace RpgFoundation.Systems
{
    /// <summary>
    /// ApplyCommands (main thread, after rewards): chests open when the hero touches them, barrels break
    /// under the hero's strikes and projectiles, spike traps cycle and hurt whoever stands on them when
    /// they rise (queued as hits, resolved this tick). Loot uses the same item drops as monsters.
    /// </summary>
    sealed class PropSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 12;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            if (game.Flow != RpgFlow.Playing) return dependency;
            var config = world.Resource(RpgKeys.Config);
            var d = config.Dungeon;
            var feedback = world.Resource(RpgKeys.Feedback);
            var props = world.Table(RpgKeys.Prop);
            var positions = world.Column(RpgKeys.PropPosition);
            var infos = world.Column(RpgKeys.PropInfo);
            float dt = context.Time.DeltaTime;

            bool hero = world.Registry.TryResolve(game.Hero, out _, out int heroRow);
            float2 heroPos = hero ? world.Column(RpgKeys.Position)[heroRow] : default;
            float heroRadius = hero ? world.Column(RpgKeys.Info)[heroRow].Radius : 0f;
            // Did the hero's melee strike land this tick (wind-up just ended)?
            bool strike = false;
            float2 aim = default;
            float reach = 0f, arc = 1f;
            if (hero)
            {
                var c = world.Column(RpgKeys.Combat)[heroRow];
                var weapon = config.Weapons[(int)world.Column(RpgKeys.Loadout)[heroRow].Weapon];
                strike = !weapon.Ranged && c.Phase == ActionPhase.Recover && c.PhaseTime <= dt * 1.01f && c.PhaseSkill == 0;
                aim = c.Aim;
                reach = world.Column(RpgKeys.Stats)[heroRow][Stat.Range] + heroRadius;
                arc = weapon.ArcCos;
            }

            for (int row = props.Count - 1; row >= 0; row--)
            {
                var info = infos[row];
                float2 p = positions[row];
                var random = SimRandom.Create(context.Seed, context.Time.Tick, 0x200u + (uint)row);
                switch (info.Kind)
                {
                    case PropKind.Chest:
                        if (info.Active || !hero || math.distancesq(p, heroPos) > math.square(info.Radius + heroRadius + 0.3f)) break;
                        info.Active = true;
                        infos[row] = info;
                        RewardSystem.DropChest(world, config, p, game.Profile.Floor, ref random);
                        feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Chest, Position = p });
                        game.Version++;
                        break;
                    case PropKind.Barrel:
                        if (!(strike && CombatMath.InArc(heroPos, aim, p, info.Radius, reach, arc)) && !HitByProjectile(world, p, info.Radius)) break;
                        RewardSystem.DropBarrel(world, config, p, game.Profile.Floor, ref random);
                        feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Barrel, Position = p });
                        world.DestroyEntity(props.Handles[row]);
                        break;
                    case PropKind.Spikes:
                    {
                        info.Timer = (info.Timer + dt) % d.SpikeCycle;
                        bool up = info.Timer >= d.SpikeCycle - d.SpikesUp;
                        if (up && !info.Active)
                        {
                            Impale(world, p, info.Radius, d.TrapDamage * (1f + config.Settings.ScalingPerFloor * (game.Profile.Floor - 1)));
                            if (hero && math.distancesq(p, heroPos) < 64f)
                                feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Spikes, Position = p });
                        }
                        info.Active = up;
                        infos[row] = info;
                        break;
                    }
                }
            }
            return dependency;
        }

        static bool HitByProjectile(SimWorld world, float2 p, float radius)
        {
            var table = world.Table(RpgKeys.Projectile);
            var positions = world.Column(RpgKeys.ProjectilePosition);
            var infos = world.Column(RpgKeys.ProjectileInfo);
            for (int i = 0; i < table.Count; i++)
            {
                var info = infos[i];
                if (info.Team != Team.Hero) continue;
                if (math.distancesq(positions[i], p) <= math.square(radius + info.Radius)) return true;
            }
            return false;
        }

        static void Impale(SimWorld world, float2 p, float radius, float damage)
        {
            var hits = world.Resource(RpgKeys.Hits);
            var positions = world.Column(RpgKeys.Position);
            var infos = world.Column(RpgKeys.Info);
            int count = world.Table(RpgKeys.Actor).Count;
            for (int row = 0; row < count; row++)
            {
                var info = infos[row];
                if (info.Has(ActorFlags.Dead)) continue;
                if (math.distancesq(positions[row], p) > math.square(radius + info.Radius * 0.5f)) continue;
                hits.TryAdd(new HitEvent
                {
                    AttackerId = -2, TargetRow = row, Damage = damage, Position = positions[row], Direction = math.normalizesafe(positions[row] - p, new float2(0f, 1f)),
                    Knockback = 2f, Stagger = 0.3f, Source = HitSource.Trap,
                });
            }
        }
    }
}
