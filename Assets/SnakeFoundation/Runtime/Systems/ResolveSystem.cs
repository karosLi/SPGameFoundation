using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L2.Buffs;
using SPF.L2.Collision;
using SPF.L2.Props;
using SPF.L2.Skills;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>
    /// Resolve phase: applies the game rules to everything detected this tick, single-threaded and in a
    /// fixed order (contacts by row, then eats / hits sorted) so outcomes are deterministic.
    /// Deaths are only flagged here; bodies are dropped and snakes removed next ApplyCommands.
    /// </summary>
    sealed class ResolveSystem : SimSystemBase
    {
        NativeArray<byte> m_FoodTaken;
        NativeArray<byte> m_PropTaken;
        NativeArray<byte> m_HeadOnDone;

        public override SimPhase Phase => SimPhase.Resolve;

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Snake).Read(SnakeKeys.Contact).Read(SnakeKeys.Control).Read(SnakeKeys.Radius)
            .Write(SnakeKeys.Head).Write(SnakeKeys.Heading).Write(SnakeKeys.Mass).Write(SnakeKeys.Info).Write(SnakeKeys.Buffs)
            .Read(SnakeKeys.Food).Read(SnakeKeys.FoodPosition).Read(SnakeKeys.FoodInfo)
            .Read(SnakeKeys.Prop).Read(SnakeKeys.PropInfo).Read(SnakeKeys.PropPosition)
            .Read(SnakeKeys.Projectile)
            .Write(SnakeKeys.Eats).Write(SnakeKeys.Hits).Write(SnakeKeys.Deaths).Write(SnakeKeys.FoodSpawns)
            .Write(SnakeKeys.ProjectileSpawns).Write(SnakeKeys.Feedback).Write(SnakeKeys.RemovedItems)
            .Write(SnakeKeys.Signal).Write(SimWorld.DestroyQueueKey);

        public override void OnCreate(SimWorld world)
        {
            m_FoodTaken = new NativeArray<byte>(world.Table(SnakeKeys.Food).Capacity, Allocator.Persistent);
            m_PropTaken = new NativeArray<byte>(world.Table(SnakeKeys.Prop).Capacity, Allocator.Persistent);
            m_HeadOnDone = new NativeArray<byte>(world.Table(SnakeKeys.Snake).Capacity, Allocator.Persistent);
        }

        public override void OnDestroy(SimWorld world)
        {
            if (m_FoodTaken.IsCreated) m_FoodTaken.Dispose();
            if (m_PropTaken.IsCreated) m_PropTaken.Dispose();
            if (m_HeadOnDone.IsCreated) m_HeadOnDone.Dispose();
        }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            var game = world.Resource(SnakeKeys.Game);
            int playerRow = world.Registry.TryResolve(game.Player, out _, out int row) ? row : -1;

            return new ResolveJob
            {
                SnakeHandles = context.Handles(SnakeKeys.Snake),
                SnakeCount = context.Count(SnakeKeys.Snake),
                Contact = context.Column(SnakeKeys.Contact),
                Control = context.Column(SnakeKeys.Control),
                Radius = context.Column(SnakeKeys.Radius),
                Head = context.Column(SnakeKeys.Head),
                Heading = context.Column(SnakeKeys.Heading),
                Mass = context.Column(SnakeKeys.Mass),
                Info = context.Column(SnakeKeys.Info),
                Buffs = context.Column(SnakeKeys.Buffs),
                FoodHandles = context.Handles(SnakeKeys.Food),
                FoodInfo = context.Column(SnakeKeys.FoodInfo),
                FoodPosition = context.Column(SnakeKeys.FoodPosition),
                PropHandles = context.Handles(SnakeKeys.Prop),
                PropInfo = context.Column(SnakeKeys.PropInfo),
                PropPosition = context.Column(SnakeKeys.PropPosition),
                ProjectileHandles = context.Handles(SnakeKeys.Projectile),
                Eats = world.Resource(SnakeKeys.Eats).Raw,
                Hits = world.Resource(SnakeKeys.Hits).Raw,
                Deaths = world.Resource(SnakeKeys.Deaths).AsWriter(),
                FoodSpawns = world.Resource(SnakeKeys.FoodSpawns).AsWriter(),
                ProjectileSpawns = world.Resource(SnakeKeys.ProjectileSpawns).AsWriter(),
                Feedback = world.Resource(SnakeKeys.Feedback).AsWriter(),
                Removed = world.Resource(SnakeKeys.RemovedItems).AsWriter(),
                Destroy = world.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                Signals = world.Resource(SnakeKeys.Signal).Values,
                FoodTaken = m_FoodTaken,
                PropTaken = m_PropTaken,
                HeadOnDone = m_HeadOnDone,
                Settings = config.Settings,
                BuffTable = config.BuffTable,
                Props = config.Props,
                Portals = config.Portals,
                PortalCount = config.PortalCount,
                Region = config.Regions[game.ActiveRegion],
                ActiveRegion = game.ActiveRegion,
                PlayerRow = playerRow,
            }.Schedule(dependency);
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        struct ResolveJob : IJob
        {
            [ReadOnly] public NativeArray<EntityHandle> SnakeHandles;
            public int SnakeCount;
            [ReadOnly] public NativeArray<SnakeContact> Contact;
            [ReadOnly] public NativeArray<SnakeControl> Control;
            [ReadOnly] public NativeArray<float> Radius;
            public NativeArray<float2> Head;
            public NativeArray<float2> Heading;
            public NativeArray<float> Mass;
            public NativeArray<SnakeInfo> Info;
            public NativeArray<BuffSet> Buffs;
            [ReadOnly] public NativeArray<EntityHandle> FoodHandles;
            [ReadOnly] public NativeArray<FoodInfo> FoodInfo;
            [ReadOnly] public NativeArray<float2> FoodPosition;
            [ReadOnly] public NativeArray<EntityHandle> PropHandles;
            [ReadOnly] public NativeArray<PropInfo> PropInfo;
            [ReadOnly] public NativeArray<float2> PropPosition;
            [ReadOnly] public NativeArray<EntityHandle> ProjectileHandles;
            public ParallelQueue<EatCandidate> Eats;
            public ParallelQueue<ProjectileHit> Hits;
            public ParallelQueue<DeathEvent>.Writer Deaths;
            public ParallelQueue<FoodSpawnRequest>.Writer FoodSpawns;
            public ParallelQueue<ProjectileSpawnRequest>.Writer ProjectileSpawns;
            public ParallelQueue<FeedbackEvent>.Writer Feedback;
            public ParallelQueue<int2>.Writer Removed;
            public ParallelQueue<EntityHandle>.Writer Destroy;
            public NativeArray<int> Signals;
            public NativeArray<byte> FoodTaken;
            public NativeArray<byte> PropTaken;
            public NativeArray<byte> HeadOnDone;
            public SnakeSettings Settings;
            public BuffTable BuffTable;
            [ReadOnly] public NativeArray<PropDefinition> Props;
            [ReadOnly] public NativeArray<PortalDef> Portals;
            public int PortalCount;
            public RegionDef Region;
            public int ActiveRegion;
            public int PlayerRow;

            bool Active(int row)
            {
                var info = Info[row];
                return info.Region == ActiveRegion && !info.Has(SnakeFlags.Dead);
            }

            public void Execute()
            {
                ResolveContacts();
                ResolveEats();
                ResolveHits();
                ResolveSkills();
                CheckPortal();
            }

            void ResolveContacts()
            {
                for (int i = 0; i < SnakeCount; i++)
                    HeadOnDone[i] = 0;

                for (int i = 0; i < SnakeCount; i++)
                {
                    if (!Active(i)) continue;
                    var contact = Contact[i];
                    switch (contact.Kind)
                    {
                        case ContactKind.Wall:
                            if (ConsumeShield(i))
                            {
                                Head[i] = math.clamp(Head[i], Region.Min + Radius[i], Region.Max - Radius[i]);
                                Heading[i] = math.normalizesafe(Region.Center - Head[i], -Heading[i]);
                            }
                            else Kill(i, -1, DeathCause.Wall);
                            break;

                        case ContactKind.Body:
                            if (!ConsumeShield(i))
                                Kill(i, contact.OtherRow, DeathCause.Body);
                            break;

                        case ContactKind.Head:
                        {
                            int j = contact.OtherRow;
                            if (HeadOnDone[i] != 0 || !Active(j)) break;
                            HeadOnDone[i] = 1;
                            HeadOnDone[j] = 1;
                            var outcome = ChainCollisionRules.HeadOn(Mass[i], Mass[j], Settings.HeadOnTolerance);
                            bool iDies = outcome == CollisionOutcome.FirstDies || outcome == CollisionOutcome.BothDie;
                            bool jDies = outcome == CollisionOutcome.SecondDies || outcome == CollisionOutcome.BothDie;
                            if (iDies && !ConsumeShield(i)) Kill(i, jDies ? -1 : j, DeathCause.HeadOn);
                            if (jDies && !ConsumeShield(j)) Kill(j, iDies ? -1 : i, DeathCause.HeadOn);
                            break;
                        }
                    }
                }
            }

            bool ConsumeShield(int row)
            {
                var buffs = Buffs[row];
                var stats = buffs.Evaluate(BuffTable);
                if (!stats.Shield) return false;
                for (int k = 0; k < BuffSet.Capacity; k++)
                {
                    var slot = buffs.Get(k);
                    if (slot.Kind != 0 && BuffTable.Get(slot.Kind).Stat == BuffStat.Shield)
                        buffs.Set(k, default);
                }
                Buffs[row] = buffs;
                return true;
            }

            void Kill(int victim, int killer, DeathCause cause)
            {
                var info = Info[victim];
                if (info.Has(SnakeFlags.Dead)) return;
                info.Flags |= SnakeFlags.Dead;
                Info[victim] = info;

                bool involvesPlayer = victim == PlayerRow || killer == PlayerRow;
                EntityHandle killerHandle = EntityHandle.Null;
                if (killer >= 0)
                {
                    var k = Info[killer];
                    k.Kills++;
                    Info[killer] = k;
                    killerHandle = SnakeHandles[killer];
                    if (killer == PlayerRow)
                        Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Kill, Position = Head[victim], Size = Radius[victim], SnakeId = k.Id, InvolvesPlayer = true });
                }
                Deaths.TryAdd(new DeathEvent { Victim = SnakeHandles[victim], Killer = killerHandle, Cause = cause });
                Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Death, Position = Head[victim], Size = Radius[victim], SnakeId = info.Id, InvolvesPlayer = involvesPlayer });
            }

            void ResolveEats()
            {
                var eats = Eats.AsArray();
                eats.Sort();
                int lastFood = -1, lastProp = -1;
                for (int n = 0; n < eats.Length; n++)
                {
                    var e = eats[n];
                    if (!Active(e.SnakeRow)) continue;
                    if (e.TargetType == EatTarget.Food)
                    {
                        if (e.TargetRow == lastFood) continue;
                        lastFood = e.TargetRow;
                        var food = FoodInfo[e.TargetRow];
                        Mass[e.SnakeRow] += food.Value * Settings.MassPerFoodValue;
                        Destroy.TryAdd(FoodHandles[e.TargetRow]);
                        Removed.TryAdd(new int2(0, food.Chunk));
                        if (e.SnakeRow == PlayerRow)
                            Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Eat, Position = FoodPosition[e.TargetRow], Size = food.Radius, SnakeId = Info[e.SnakeRow].Id, InvolvesPlayer = true });
                    }
                    else
                    {
                        if (e.TargetRow == lastProp) continue;
                        lastProp = e.TargetRow;
                        var prop = PropInfo[e.TargetRow];
                        var def = Props[math.max(prop.Kind - 1, 0)];
                        var buffs = Buffs[e.SnakeRow];
                        buffs.Apply(def.BuffKind, BuffTable.Get(def.BuffKind).Duration);
                        Buffs[e.SnakeRow] = buffs;
                        Destroy.TryAdd(PropHandles[e.TargetRow]);
                        Removed.TryAdd(new int2(1, prop.Chunk));
                        Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Pickup, Position = PropPosition[e.TargetRow], Size = prop.Kind, SnakeId = Info[e.SnakeRow].Id, InvolvesPlayer = e.SnakeRow == PlayerRow });
                    }
                }
                Eats.Clear();
            }

            void ResolveHits()
            {
                var hits = Hits.AsArray();
                hits.Sort();
                int lastProjectile = -1;
                var skill = Settings.Skill;
                for (int n = 0; n < hits.Length; n++)
                {
                    var h = hits[n];
                    if (h.ProjectileRow == lastProjectile || !Active(h.SnakeRow)) continue;
                    lastProjectile = h.ProjectileRow;
                    Destroy.TryAdd(ProjectileHandles[h.ProjectileRow]);

                    if (ConsumeShield(h.SnakeRow)) continue;
                    var buffs = Buffs[h.SnakeRow];
                    if (skill.HitBuffKind != 0)
                        buffs.Apply(skill.HitBuffKind, BuffTable.Get(skill.HitBuffKind).Duration);
                    Buffs[h.SnakeRow] = buffs;
                    float loss = math.min(skill.HitMassLoss, math.max(Mass[h.SnakeRow] - Settings.MinMass, 0f));
                    Mass[h.SnakeRow] -= loss;
                    if (loss > 0f)
                        FoodSpawns.TryAdd(new FoodSpawnRequest { Position = h.Position, Value = loss / math.max(Settings.MassPerFoodValue, 1e-3f), Color = 5 });
                    Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Hit, Position = h.Position, Size = loss, SnakeId = Info[h.SnakeRow].Id, InvolvesPlayer = h.SnakeRow == PlayerRow });
                }
                Hits.Clear();
            }

            void ResolveSkills()
            {
                var skill = Settings.Skill;
                for (int i = 0; i < SnakeCount; i++)
                {
                    if (!Control[i].UseSkill || !Active(i)) continue;
                    var info = Info[i];
                    if (!SkillRules.CanCast(skill, info.SkillCooldown, Mass[i])) continue;
                    info.SkillCooldown = skill.Cooldown;
                    Info[i] = info;
                    Mass[i] -= skill.MassCost;
                    float2 heading = Heading[i];
                    ProjectileSpawns.TryAdd(new ProjectileSpawnRequest
                    {
                        Position = Head[i] + heading * (Radius[i] + skill.ProjectileRadius + 0.5f),
                        Velocity = heading * skill.ProjectileSpeed,
                        OwnerId = info.Id,
                        OwnerRow = i,
                        Skill = 1,
                    });
                    Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.SkillCast, Position = Head[i], Size = 1f, SnakeId = info.Id, InvolvesPlayer = i == PlayerRow });
                }
            }

            void CheckPortal()
            {
                if (PlayerRow < 0 || !Active(PlayerRow)) return;
                var info = Info[PlayerRow];
                if (info.PortalCooldown > 0f) return;
                float2 head = Head[PlayerRow];
                for (int p = 0; p < PortalCount; p++)
                {
                    var portal = Portals[p];
                    if (portal.FromRegion != ActiveRegion) continue;
                    float r = portal.Radius + Radius[PlayerRow];
                    if (math.distancesq(head, portal.Position) < r * r)
                    {
                        Signals[SnakeFoundation.Signals.PortalRequest] = p + 1;
                        return;
                    }
                }
            }
        }
    }
}
