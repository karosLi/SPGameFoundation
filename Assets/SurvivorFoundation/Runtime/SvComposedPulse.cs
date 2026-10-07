using System;
using System.IO;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.Skills;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SurvivorFoundation
{
    /// <summary>Opt-in authored content for the existing nine-tick pulse pose. Wide is a data-only
    /// variant; Repulse adds the independent game-local outward-knock rule. No new action family.</summary>
    [Serializable]
    public struct SvPulseDefinition
    {
        public bool Enabled;
        public float Radius, Damage, OutwardKnock;
        public int CooldownTicks, Charges, Targets;
        public const int DurationTicks = 9, ReleaseTick = 3;
        public static SvPulseDefinition Wide => new SvPulseDefinition
        { Enabled = true, Radius = 5f, Damage = 20f, CooldownTicks = 120, Charges = 2, Targets = 128 };
        public static SvPulseDefinition Repulse { get { var d = Wide; d.OutwardKnock = .9f; return d; } }
        public void Validate()
        {
            if (!Enabled || !math.isfinite(Radius) || Radius <= 0 || Radius > 20 || !math.isfinite(Damage) || Damage < 0 || Damage > 10000 ||
                !math.isfinite(OutwardKnock) || OutwardKnock < 0 || OutwardKnock > 4 || CooldownTicks < 1 || CooldownTicks > 3600 ||
                Charges < 1 || Charges > 16 || Targets < 1 || Targets > 4096) throw new ArgumentOutOfRangeException(nameof(SvPulseDefinition));
        }
        internal void Write(BinaryWriter w)
        { w.Write(Radius); w.Write(Damage); w.Write(OutwardKnock); w.Write(CooldownTicks); w.Write(Charges); w.Write(Targets); }
        internal void Check(BinaryReader r)
        {
            if (r.ReadSingle() != Radius || r.ReadSingle() != Damage || r.ReadSingle() != OutwardKnock || r.ReadInt32() != CooldownTicks ||
                r.ReadInt32() != Charges || r.ReadInt32() != Targets) throw new InvalidDataException("Composed pulse content differs.");
        }
    }

    /// <summary>Game-local bounded request result; queued requests are not settled HP changes.</summary>
    public struct SvPulseReleaseResult
    {
        public int AcceptedRequests, RejectedRequests;
    }

    /// <summary>Single-owner, single-writer pulse action and stable target history. Authoritative
    /// schema v1; the history is fixed capacity, never evicts, and is recorded only after queue admission.
    /// AcceptedRequests means SvHit accepted by the bounded damage queue; ResolveSystem still owns HP.</summary>
    public sealed class SvComposedPulseState : ISnapshotResource, IResettableResource, IDisposable
    {
        public static readonly ResourceKey<SvComposedPulseState> Key = new ResourceKey<SvComposedPulseState>("Sv.ComposedPulse.V1");
        const int Magic = 0x53565031;
        public readonly SvPulseDefinition Definition;
        public NativeArray<EntityHandle> History;
        public HitHistoryState HitScope;
        public ActionTimeline Timeline;
        public bool Released;
        public float HpAtStart;
        public int Starts, Releases, Cancellations;
        public SvPulseReleaseResult LastRelease;
        public SvComposedPulseState(SvPulseDefinition definition)
        { definition.Validate(); Definition = definition; History = new NativeArray<EntityHandle>(definition.Targets, Allocator.Persistent); }
        public SkillSlots CreateSkills() => new SkillSlots(
            new SkillSlotDefinition(111, 2, SkillActivation.Tap, Definition.CooldownTicks, Definition.Charges),
            new SkillSlotDefinition(12, 3, SkillActivation.AimRelease, 120, 2),
            new SkillSlotDefinition(21, 0, SkillActivation.Hold, 1), new SkillSlotDefinition(22, 1, SkillActivation.Tap, 7));
        public void Advance(bool playing, bool alive, float hp, bool interrupt)
        {
            if (!alive || interrupt || Timeline.Running && hp < HpAtStart) { Cancel(); return; }
            if (!playing || !Timeline.Running) return;
            Timeline.Advance();
            if (Timeline.Tick >= SvPulseDefinition.DurationTicks) { Timeline.Stop(); HitHistory.Release(History, 0, History.Length, ref HitScope); }
        }
        public void Begin(float hp)
        {
            Timeline.Begin(); Released = false; HpAtStart = hp; LastRelease = default; if (Starts < int.MaxValue) Starts++;
            HitHistory.Begin(History, 0, History.Length, ref HitScope, EntityHandle.Null, Timeline.PulseId);
        }
        public void Cancel()
        {
            if (Timeline.Running && Cancellations < int.MaxValue) Cancellations++;
            Timeline.Stop(); HitHistory.Release(History, 0, History.Length, ref HitScope);
        }
        public bool TryRelease()
        {
            if (Released || !Timeline.Crossed(new ActionWindow(SvPulseDefinition.ReleaseTick, SvPulseDefinition.ReleaseTick + 1))) return false;
            Released = true; if (Releases < int.MaxValue) Releases++; return true;
        }
        public void OnReset()
        { Timeline = default; Released = false; HpAtStart = 0; Starts = Releases = Cancellations = 0; LastRelease = default; HitHistory.Release(History, 0, History.Length, ref HitScope); }
        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Magic); w.Write(1); Definition.Write(w);
            w.Write(Timeline.PreviousTick); w.Write(Timeline.Tick); w.Write(Timeline.PulseId); w.Write(Timeline.Running);
            w.Write(Released); w.Write(HpAtStart); w.Write(Starts); w.Write(Releases); w.Write(Cancellations);
            w.Write(LastRelease.AcceptedRequests); w.Write(LastRelease.RejectedRequests);
            NativeIO.WriteValue(w, HitScope); NativeIO.Write(w, History);
        }
        public void ReadSnapshot(BinaryReader r)
        {
            if (r.ReadInt32() != Magic || r.ReadInt32() != 1) throw new InvalidDataException("Unsupported composed pulse schema.");
            Definition.Check(r);
            Timeline = new ActionTimeline { PreviousTick = r.ReadInt32(), Tick = r.ReadInt32(), PulseId = r.ReadUInt32(), Running = r.ReadBoolean() };
            Released = r.ReadBoolean(); HpAtStart = r.ReadSingle(); Starts = r.ReadInt32(); Releases = r.ReadInt32(); Cancellations = r.ReadInt32();
            LastRelease = new SvPulseReleaseResult { AcceptedRequests = r.ReadInt32(), RejectedRequests = r.ReadInt32() };
            HitScope = NativeIO.ReadValue<HitHistoryState>(r); NativeIO.ReadAll(r, History);
            if (Timeline.Tick < 0 || Timeline.PreviousTick < -1 || Timeline.PreviousTick > Timeline.Tick ||
                Timeline.Running && (Timeline.PulseId == 0 || Timeline.Tick >= SvPulseDefinition.DurationTicks) ||
                Released && Timeline.Tick < SvPulseDefinition.ReleaseTick || !math.isfinite(HpAtStart) || HpAtStart < 0 ||
                Starts < 0 || Releases < 0 || Releases > Starts || Cancellations < 0 || Cancellations > Starts || LastRelease.AcceptedRequests < 0 || LastRelease.AcceptedRequests > History.Length || LastRelease.RejectedRequests < 0 ||
                !HitHistory.IsValid(History, 0, History.Length, HitScope) || HitScope.Pulse != (Timeline.Running ? Timeline.PulseId : 0) || !HitScope.Owner.IsNull ||
                Timeline.Running && (HitScope.Count != LastRelease.AcceptedRequests || Released != (Timeline.Tick >= SvPulseDefinition.ReleaseTick)))
                throw new InvalidDataException("Invalid composed pulse state.");
            for (int i = 0; i < HitScope.Count; i++)
            {
                if (History[i].Index < 0 || History[i].Generation <= 0) throw new InvalidDataException("Invalid pulse target.");
                for (int j = 0; j < i; j++) if (History[j] == History[i]) throw new InvalidDataException("Duplicate pulse target.");
            }
        }
        public void Dispose() { if (History.IsCreated) History.Dispose(); }
    }

    /// <summary>Game-local extension, not a foundation gameplay enum or a visual impulse. Reuses the
    /// existing SvHit.Knock contract so the normal damage resolver applies radius-scaled displacement.</summary>
    public static class SvRepulseRule
    {
        public static float2 Knock(float2 origin, float2 target, float strength) => math.normalizesafe(target - origin) * strength;
    }
}

namespace SurvivorFoundation.Systems
{
    sealed class ComposedPulseSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;
        public override int Order => 7;
        public override void Declare(AccessDeclaration a) => a.Write(SvComposedPulseState.Key).Read(SvKeys.Enemy).Read(SvKeys.Info)
            .Read(SvKeys.EnemyGrid).Write(SvKeys.Hits).Write(SvKeys.Feedback);
        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            dependency.Complete(); var world = context.World; var game = world.Resource(SvKeys.Game); var state = world.Resource(SvComposedPulseState.Key);
            if (game.Flow != SvFlow.Playing || game.Hp <= 0 || !state.TryRelease()) return dependency;
            var visitor = new Targets { State = state, Origin = game.Hero, Might = SvRules.Might(game),
                Handles = world.Table(SvKeys.Enemy).Handles, Info = world.Column(SvKeys.Info), Hits = world.Resource(SvKeys.Hits) };
            var grid = world.Resource(SvKeys.EnemyGrid).AsReader(); float reach = state.Definition.Radius + grid.MaxEntryRadius;
            grid.QueryCells(game.Hero - reach, game.Hero + reach, ref visitor);
            // Nova describes the actual release, including a whiff. It is never an accepted-damage fact.
            world.Resource(SvKeys.Feedback).TryAdd(new SvFeedback { Kind = SvFeedbackKind.Nova, Position = game.Hero, Value = state.Definition.Radius });
            return dependency;
        }
        struct Targets : IGridVisitor
        {
            public SvComposedPulseState State; public float2 Origin; public float Might;
            public NativeArray<EntityHandle> Handles; public NativeArray<EnemyInfo> Info; public EventQueue<SvHit> Hits;
            public bool Visit(in GridEntry e)
            {
                if (Info[e.Owner].Has(EnemyFlags.Dead) || !CombatShapes.AnnulusHitsCircle(Origin, 0, State.Definition.Radius, e.Position, e.Radius)) return true;
                var check = HitHistory.Check(State.History, 0, State.History.Length, State.HitScope, Handles[e.Owner]);
                if (check != HitRecordResult.Added) { if (check == HitRecordResult.Full) State.LastRelease.RejectedRequests++; return true; }
                // Only this synchronous visitor writes this scope. No writer may intervene between
                // Check, successful TryAdd, and TryRecord. This is deliberately not a concurrent transaction.
                if (!Hits.TryAdd(new SvHit { Target = e.Owner, Damage = State.Definition.Damage * Might,
                    Knock = SvRepulseRule.Knock(Origin, e.Position, State.Definition.OutwardKnock) })) { State.LastRelease.RejectedRequests++; return true; }
                HitHistory.TryRecord(State.History, 0, State.History.Length, ref State.HitScope, Handles[e.Owner]);
                State.LastRelease.AcceptedRequests++; return true;
            }
        }
    }
}
