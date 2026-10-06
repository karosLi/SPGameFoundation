using System;
using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;

namespace SurvivorFoundation
{
    /// <summary>Two persistent damaging bands. Cadence is simulation ticks, independent of render frames.
    /// The hollow centers do not damage. Overlapping bands hit each target once per pulse.</summary>
    [Serializable]
    public struct SvAnnularSkill
    {
        public bool Enabled;
        public float RadiusA, RadiusB, HalfWidth, DamagePerSecond;
        public int TickInterval;
        public float OuterRadius => math.max(RadiusA, RadiusB) > 0f ? math.max(RadiusA, RadiusB) + math.max(0f, HalfWidth) : 0f;
        public bool Hits(float2 origin, float2 target, float targetRadius) =>
            (RadiusA > 0f && CombatShapes.AnnulusHitsCircle(origin, math.max(0f, RadiusA - math.max(0f, HalfWidth)), math.max(0f, RadiusA + math.max(0f, HalfWidth)), target, targetRadius)) ||
            (RadiusB > 0f && CombatShapes.AnnulusHitsCircle(origin, math.max(0f, RadiusB - math.max(0f, HalfWidth)), math.max(0f, RadiusB + math.max(0f, HalfWidth)), target, targetRadius));
    }

    /// <summary>Explicit sign tag in the existing float damage queue: positive = hero, negative = beacon.
    /// The classic queue schema/resource order is unchanged; only guard rules consume negative entries.</summary>
    public static class SvDamage
    {
        public static float ToBeacon(float damage) => -math.max(0f, damage);
        public static float BeaconAmount(float encoded) => math.max(0f, -encoded);
    }

    public static class SvGuardRules
    {
        /// <summary>First swept circle contact in [0,1], or +infinity. Starting overlaps contact at zero.</summary>
        public static float EntryFraction(float2 from, float2 to, float radius, float2 center, float targetRadius)
            => CombatSweep.PointCircle(from, to, center, math.max(0f, radius) + math.max(0f, targetRadius), out float fraction)
                ? fraction : float.PositiveInfinity;

        public static float2 Target(float2 enemy, float2 hero, float2 beacon, bool guard, float aggroRadius) =>
            !guard || math.distancesq(enemy, hero) <= math.max(0f, aggroRadius) * math.max(0f, aggroRadius) ? hero : beacon;
    }
}

namespace SurvivorFoundation.Systems
{
    /// <summary>Reuses the grid, hit queue and resolver. No GameObjects, colliders or per-target timers.</summary>
    sealed class AnnularSkillSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;
        public override int Order => 5;
        public override void Declare(AccessDeclaration access) => access.Read(SvKeys.EnemyGrid).Write(SvKeys.Hits);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var game = context.World.Resource(SvKeys.Game);
            var skill = context.World.Resource(SvKeys.Config).Settings.AnnularSkill;
            if (game.Flow != SvFlow.Playing || !skill.Enabled) return dependency;
            int interval = math.max(1, skill.TickInterval);
            if (++game.AnnularTicks < interval) return dependency;
            game.AnnularTicks = 0;
            game.AnnularPulses++;
            return new PulseJob
            {
                Grid = context.Resource(SvKeys.EnemyGrid).AsReader(), Hits = context.Resource(SvKeys.Hits).AsWriter(),
                Skill = skill, Origin = game.Hero,
                Damage = math.max(0f, skill.DamagePerSecond) * interval * context.Time.DeltaTime * SvRules.Might(game),
            }.Schedule(dependency);
        }

        struct BandHits : IGridVisitor
        {
            public SvAnnularSkill Skill;
            public float2 Origin;
            public float Damage;
            public ParallelQueue<SvHit>.Writer Hits;
            public bool Visit(in GridEntry entry)
            {
                if (Skill.Hits(Origin, entry.Position, entry.Radius))
                    Hits.TryAdd(new SvHit { Target = entry.Owner, Damage = Damage });
                return true;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct PulseJob : IJob
        {
            public GridReader Grid;
            public ParallelQueue<SvHit>.Writer Hits;
            public SvAnnularSkill Skill;
            public float2 Origin;
            public float Damage;
            public void Execute()
            {
                var visitor = new BandHits { Skill = Skill, Origin = Origin, Damage = Damage, Hits = Hits };
                // Grid.Query uses strict circle overlap; include exact shape tangencies using cells
                // expanded by the grid's largest target radius, then the inclusive narrow phase.
                float reach = Skill.OuterRadius + Grid.MaxEntryRadius;
                Grid.QueryCells(Origin - reach, Origin + reach, ref visitor);
            }
        }
    }
}
