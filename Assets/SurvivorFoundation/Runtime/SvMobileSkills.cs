using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.Skills;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;

namespace SurvivorFoundation
{
    public static class SvMobileSkills
    {
        public static readonly ResourceKey<SkillSlots> Key = new ResourceKey<SkillSlots>("Sv.MobileSkills.V1");
        public const int Pulse = 0, Dash = 1;
        public const float PulseRadius = 4f, PulseDamage = 28f, DashDistance = 3f;
        // Survivor runs at 30 Hz. Two charges demonstrate sequential deterministic recharge.
        public static SkillSlots Create() => new SkillSlots(
            new SkillSlotDefinition(11, 2, SkillActivation.Tap, 90, 2),
            new SkillSlotDefinition(12, 3, SkillActivation.AimRelease, 120, 2));
    }
}

namespace SurvivorFoundation.Systems
{
    /// <summary>Input must run before HeroSystem consumes one-shot presses. Dash is a clamped blink,
    /// deliberately not a collision sweep or invulnerability grant. Aim zero uses authoritative facing.</summary>
    sealed class MobileSkillInputSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Input;
        public override int Order => -10;
        public override void Declare(AccessDeclaration access) => access.Write(SvMobileSkills.Key);
        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var game = context.World.Resource(SvKeys.Game);
            var slots = context.World.Resource(SvMobileSkills.Key);
            bool playing = game.Flow == SvFlow.Playing && game.Hp > 0f;
            slots.AdvanceTick(playing);
            if (!playing) { game.Input = default; return dependency; }
            if (game.Input.WasPressed(SvMobileSkills.Pulse)) slots.TryActivate(SvMobileSkills.Pulse, true);
            if (game.Input.WasPressed(SvMobileSkills.Dash) && slots.TryActivate(SvMobileSkills.Dash, true))
            {
                var aim = game.Input.Aim;
                if (!math.all(math.isfinite(aim)) || math.lengthsq(aim) < .0001f) aim = game.Facing;
                if (math.lengthsq(aim) < .0001f) aim = new float2(1f, 0f);
                aim = math.normalize(aim);
                float edge = context.World.Resource(SvKeys.Config).Settings.ArenaHalf;
                game.Hero = math.clamp(game.Hero + aim * SvMobileSkills.DashDistance, -edge, edge);
                game.Facing = aim;
            }
            return dependency;
        }
    }

    /// <summary>One bounded grid query; damage queue/resolver remain the sole HP owner. Full queue
    /// drops excess targets for this pulse, never creates an unbounded retry or changes recharge.</summary>
    sealed class MobileSkillPulseSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;
        public override int Order => 7;
        public override void Declare(AccessDeclaration access) => access.Read(SvKeys.EnemyGrid).Write(SvKeys.Hits).Write(SvKeys.Feedback).Read(SvMobileSkills.Key);
        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var game = context.World.Resource(SvKeys.Game);
            if (game.Flow != SvFlow.Playing || (context.World.Resource(SvMobileSkills.Key).Activated & 1u) == 0) return dependency;
            return new PulseJob
            {
                Grid = context.Resource(SvKeys.EnemyGrid).AsReader(), Hits = context.Resource(SvKeys.Hits).AsWriter(),
                Feedback = context.Resource(SvKeys.Feedback).AsWriter(),
                Origin = game.Hero, Damage = SvMobileSkills.PulseDamage * SvRules.Might(game),
            }.Schedule(dependency);
        }

        struct PulseVisitor : IGridVisitor
        {
            public float2 Origin;
            public float Damage;
            public ParallelQueue<SvHit>.Writer Hits;
            public bool Visit(in GridEntry entry)
            {
                if (CombatShapes.AnnulusHitsCircle(Origin, 0f, SvMobileSkills.PulseRadius, entry.Position, entry.Radius))
                    Hits.TryAdd(new SvHit { Target = entry.Owner, Damage = Damage });
                return true;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct PulseJob : IJob
        {
            public GridReader Grid;
            public ParallelQueue<SvFeedback>.Writer Feedback;
            public ParallelQueue<SvHit>.Writer Hits;
            public float2 Origin;
            public float Damage;
            public void Execute()
            {
                Feedback.TryAdd(new SvFeedback { Kind = SvFeedbackKind.Nova, Position = Origin, Value = SvMobileSkills.PulseRadius });
                var visitor = new PulseVisitor { Origin = Origin, Damage = Damage, Hits = Hits };
                float reach = SvMobileSkills.PulseRadius + Grid.MaxEntryRadius;
                Grid.QueryCells(Origin - reach, Origin + reach, ref visitor);
            }
        }
    }
}
