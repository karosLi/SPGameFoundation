using System;
using System.IO;
using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SurvivorFoundation
{
    /// <summary>Opt-in periodic crossed flying-blade paths. Each pulse queries two capsules; a target
    /// in their overlap takes one hit. Configuration is session-constant, independent of render quality.</summary>
    [Serializable]
    public struct SvCrossedBlades
    {
        public bool Enabled;
        public int TickInterval, MaxTargets;
        public float Reach, HalfWidth, DamagePerPulse;
    }

    /// <summary>Only installed by the opt-in config. This schema is separate from classic/guard saves.
    /// Null owner denotes this level-owned singleton; OnReset invalidates it. No projectile entities.</summary>
    public sealed class SvCrossedBladeState : IDisposable, IJobData, IResettableResource, ISnapshotResource
    {
        const int Magic = 0x53434232;
        public NativeArray<EntityHandle> Targets;
        public NativeArray<HitHistoryState> Scope;
        // [0] rejected by history capacity, [1] rejected by damage queue. Saturating deterministic counts.
        public NativeArray<int> Rejections;
        public ActionTimeline Timeline;
        public int Pulses;

        public SvCrossedBladeState(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Targets = new NativeArray<EntityHandle>(capacity, Allocator.Persistent);
            Scope = new NativeArray<HitHistoryState>(1, Allocator.Persistent);
            Rejections = new NativeArray<int>(2, Allocator.Persistent);
            OnReset();
        }

        public void OnReset()
        {
            for (int i = 0; i < Targets.Length; i++) Targets[i] = default;
            Scope[0] = default;
            Rejections[0] = Rejections[1] = 0;
            Timeline = default;
            Timeline.Begin();
            Pulses = 0;
        }

        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Magic); w.Write(1); w.Write(Targets.Length);
            w.Write(Timeline.PreviousTick); w.Write(Timeline.Tick); w.Write(Timeline.PulseId); w.Write(Timeline.Running);
            w.Write(Pulses);
            NativeIO.Write(w, Scope); NativeIO.Write(w, Targets); NativeIO.Write(w, Rejections);
        }

        public void ReadSnapshot(BinaryReader r)
        {
            if (r.ReadInt32() != Magic || r.ReadInt32() != 1 || r.ReadInt32() != Targets.Length)
                throw new InvalidDataException("Unsupported crossed-blade snapshot layout.");
            Timeline = new ActionTimeline { PreviousTick = r.ReadInt32(), Tick = r.ReadInt32(), PulseId = r.ReadUInt32(), Running = r.ReadBoolean() };
            Pulses = r.ReadInt32();
            NativeIO.ReadAll(r, Scope); NativeIO.ReadAll(r, Targets); NativeIO.ReadAll(r, Rejections);
            var scope = Scope[0];
            if (!HitHistory.IsValid(Targets, 0, Targets.Length, scope) || !scope.Owner.IsNull ||
                (scope.Count > 0 && scope.Pulse == 0) || Timeline.Tick < 0 || Timeline.PreviousTick < -1 ||
                Timeline.PreviousTick > Timeline.Tick || Timeline.PulseId == 0 || !Timeline.Running ||
                Pulses < 0 || Rejections[0] < 0 || Rejections[1] < 0 ||
                (Pulses == 0
                    ? scope.Pulse != 0 || scope.Count != 0 || Timeline.PulseId != 1
                    : scope.Pulse != (Timeline.PulseId == 1 ? uint.MaxValue : Timeline.PulseId - 1)))
                throw new InvalidDataException("Invalid crossed-blade state.");
            for (int i = 0; i < scope.Count; i++)
                if (Targets[i].Index < 0 || Targets[i].Generation <= 0) throw new InvalidDataException("Invalid saved blade target.");
        }

        public void Dispose() { Targets.Dispose(); Scope.Dispose(); Rejections.Dispose(); }
    }
}

namespace SurvivorFoundation.Systems
{
    sealed class CrossedBladeSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;
        public override int Order => 6;
        public override void Declare(AccessDeclaration access) => access.Read(SvKeys.Enemy).Read(SvKeys.EnemyGrid)
            .Write(SvKeys.Hits).Write(SvKeys.CrossedBlades);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var game = context.World.Resource(SvKeys.Game);
            if (game.Flow != SvFlow.Playing) return dependency;
            var skill = context.World.Resource(SvKeys.Config).Settings.CrossedBlades;
            if (!skill.Enabled) return dependency;
            var state = context.Resource(SvKeys.CrossedBlades);
            state.Timeline.Advance();
            int interval = math.clamp(skill.TickInterval, 1, int.MaxValue - 1);
            if (!state.Timeline.Crossed(new ActionWindow(interval, interval + 1))) return dependency;
            uint pulse = state.Timeline.PulseId;
            state.Timeline.Begin();
            if (state.Pulses < int.MaxValue) state.Pulses++;
            return new PulseJob
            {
                Grid = context.Resource(SvKeys.EnemyGrid).AsReader(), Handles = context.Handles(SvKeys.Enemy),
                Hits = context.Resource(SvKeys.Hits).AsWriter(), Targets = state.Targets, Scope = state.Scope,
                Rejections = state.Rejections, Origin = game.Hero, Skill = skill, Pulse = pulse,
                Damage = math.max(0f, skill.DamagePerPulse) * SvRules.Might(game),
            }.Schedule(dependency);
        }

        struct BladeHits : IGridVisitor
        {
            public float2 Start, End;
            public float Width, Damage;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<EntityHandle> Targets;
            public NativeArray<int> Rejections;
            public HitHistoryState Scope;
            public ParallelQueue<SvHit>.Writer Hits;

            public bool Visit(in GridEntry entry)
            {
                if (!CombatShapes.BeamHitsCircle(Start, End, Width, entry.Position, entry.Radius)) return true;
                EntityHandle target = Handles[entry.Owner];
                var result = HitHistory.Check(Targets, 0, Targets.Length, Scope, target);
                if (result == HitRecordResult.Full) { Increment(0); return true; }
                if (result != HitRecordResult.Added) return true;
                if (!Hits.TryAdd(new SvHit { Target = entry.Owner, Damage = Damage })) { Increment(1); return true; }
                HitHistory.TryRecord(Targets, 0, Targets.Length, ref Scope, target);
                return true;
            }

            void Increment(int index) { if (Rejections[index] < int.MaxValue) Rejections[index]++; }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct PulseJob : IJob
        {
            public GridReader Grid;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public ParallelQueue<SvHit>.Writer Hits;
            public NativeArray<EntityHandle> Targets;
            public NativeArray<HitHistoryState> Scope;
            public NativeArray<int> Rejections;
            public float2 Origin;
            public SvCrossedBlades Skill;
            public float Damage;
            public uint Pulse;

            public void Execute()
            {
                var scope = Scope[0];
                HitHistory.Begin(Targets, 0, Targets.Length, ref scope, EntityHandle.Null, Pulse);
                float reach = math.max(0f, Skill.Reach), width = math.max(0f, Skill.HalfWidth);
                var visitor = new BladeHits { Handles = Handles, Targets = Targets, Scope = scope, Rejections = Rejections, Hits = Hits, Width = width, Damage = Damage };
                for (int axis = 0; axis < 2; axis++)
                {
                    float2 delta = axis == 0 ? new float2(reach, 0f) : new float2(0f, reach);
                    visitor.Start = Origin - delta; visitor.End = Origin + delta;
                    CombatShapes.BeamBounds(visitor.Start, visitor.End, width, out var min, out var max);
                    Grid.QueryCells(min - Grid.MaxEntryRadius, max + Grid.MaxEntryRadius, ref visitor);
                }
                Scope[0] = visitor.Scope;
            }
        }
    }
}
