using System;
using System.Collections.Generic;
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
    /// <summary>Authoritative fixed-capacity flying swords. Rendering never changes these limits.</summary>
    [Serializable]
    public struct SvFlyingSwords
    {
        public bool Enabled;
        public int Capacity, BaseCount, HistoryPerSword, OrbitTicks, OutboundTicks, ReturnTicks, WaveTicks;
        public float OrbitRadius, OrbitRadiansPerSecond, Speed, ReturnSpeed, TurnRate, TargetRange, Radius, Damage;
        public static SvFlyingSwords Default => new SvFlyingSwords
        {
            Enabled = true, Capacity = 24, BaseCount = 10, HistoryPerSword = 12,
            OrbitTicks = 22, OutboundTicks = 25, ReturnTicks = 45, WaveTicks = 75 * 30,
            OrbitRadius = 2.2f, OrbitRadiansPerSecond = 1.6f, Speed = 22f, ReturnSpeed = 26f,
            TurnRate = 7f, TargetRange = 12f, Radius = .16f, Damage = 12f,
        };
        public void Validate()
        {
            if (!Enabled) return;
            if (Capacity < 1 || Capacity > 64 || BaseCount < 1 || BaseCount > Capacity || HistoryPerSword < 1 || HistoryPerSword > 128 ||
                OrbitTicks < 1 || OutboundTicks < 1 || ReturnTicks < 1 || WaveTicks < 1 ||
                !Finite(OrbitRadius) || OrbitRadius < 0 || !Finite(OrbitRadiansPerSecond) ||
                !Finite(Speed) || Speed <= 0 || !Finite(ReturnSpeed) || ReturnSpeed <= 0 ||
                !Finite(TurnRate) || TurnRate <= 0 || !Finite(TargetRange) || TargetRange <= 0 ||
                !Finite(Radius) || Radius < 0 || !Finite(Damage) || Damage < 0)
                throw new ArgumentException("Invalid flying-sword configuration.");
        }
        public uint Fingerprint()
        {
            uint h = 2166136261u;
            Hash(ref h, Enabled ? 1u : 0u); Hash(ref h, (uint)Capacity); Hash(ref h, (uint)BaseCount);
            Hash(ref h, (uint)HistoryPerSword); Hash(ref h, (uint)OrbitTicks); Hash(ref h, (uint)OutboundTicks);
            Hash(ref h, (uint)ReturnTicks); Hash(ref h, (uint)WaveTicks);
            Hash(ref h, math.asuint(OrbitRadius)); Hash(ref h, math.asuint(OrbitRadiansPerSecond));
            Hash(ref h, math.asuint(Speed)); Hash(ref h, math.asuint(ReturnSpeed)); Hash(ref h, math.asuint(TurnRate));
            Hash(ref h, math.asuint(TargetRange)); Hash(ref h, math.asuint(Radius)); Hash(ref h, math.asuint(Damage));
            return h;
        }
        static void Hash(ref uint h, uint value) { unchecked { h = (h ^ value) * 16777619u; } }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }

    public enum SvSwordPhase : byte { Orbit, Outbound, Returning }
    public struct SvSword
    {
        public float2 Position, Previous, Direction;
        public EntityHandle Target;
        public ActionTimeline Timeline;
        public SvSwordPhase Phase;
        public int Age;
        public bool Active;
    }

    public struct SvSwordContact
    {
        public EntityHandle Handle;
        public int Row;
        public float Fraction;
    }

    /// <summary>Opt-in V1 snapshot resource, separate from legacy Survivor byte layouts. Each sword
    /// owns one fixed history slice for its ENTIRE sortie, including outbound and return travel.</summary>
    public sealed class SvFlyingSwordState : IJobData, IResettableResource, ISnapshotResource, IDisposable
    {
        public static readonly ResourceKey<SvFlyingSwordState> Key = new ResourceKey<SvFlyingSwordState>("Sv.FlyingSwords.V1");
        const int Magic = 0x53575331;
        public readonly int HistoryCapacity;
        public readonly uint ConfigFingerprint;
        public NativeArray<SvSword> Blades;
        public NativeArray<EntityHandle> History;
        public NativeArray<HitHistoryState> Scopes;
        public NativeArray<SvSwordContact> Contacts; // scratch, overwritten before every use, not saved
        public NativeArray<int> Counters; // launches, accepted hits, history rejects, queue rejects, query candidates, swept contacts
        public int ActiveCount, Tick;
        public SvFlyingSwordState(in SvFlyingSwords settings, int enemyCapacity)
        {
            settings.Validate(); int capacity = settings.Capacity, historyPerSword = settings.HistoryPerSword;
            ConfigFingerprint = settings.Fingerprint();
            if (capacity < 1 || capacity > 64 || historyPerSword < 1 || historyPerSword > 128 || enemyCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            HistoryCapacity = historyPerSword;
            Blades = new NativeArray<SvSword>(capacity, Allocator.Persistent);
            History = new NativeArray<EntityHandle>(capacity * historyPerSword, Allocator.Persistent);
            Scopes = new NativeArray<HitHistoryState>(capacity, Allocator.Persistent);
            Contacts = new NativeArray<SvSwordContact>(enemyCapacity, Allocator.Persistent);
            Counters = new NativeArray<int>(6, Allocator.Persistent);
        }
        public void OnReset()
        {
            for (int i = 0; i < Blades.Length; i++) { Blades[i] = default; Scopes[i] = default; }
            for (int i = 0; i < History.Length; i++) History[i] = default;
            for (int i = 0; i < Counters.Length; i++) Counters[i] = 0;
            ActiveCount = Tick = 0;
        }
        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Magic); w.Write(1); w.Write(Blades.Length); w.Write(HistoryCapacity); w.Write(ConfigFingerprint); w.Write(ActiveCount); w.Write(Tick);
            NativeIO.Write(w, Blades); NativeIO.Write(w, History); NativeIO.Write(w, Scopes); NativeIO.Write(w, Counters);
        }
        public void ReadSnapshot(BinaryReader r)
        {
            if (r.ReadInt32() != Magic || r.ReadInt32() != 1 || r.ReadInt32() != Blades.Length || r.ReadInt32() != HistoryCapacity || r.ReadUInt32() != ConfigFingerprint)
                throw new InvalidDataException("Unsupported flying-sword snapshot layout.");
            ActiveCount = r.ReadInt32(); Tick = r.ReadInt32();
            NativeIO.ReadAll(r, Blades); NativeIO.ReadAll(r, History); NativeIO.ReadAll(r, Scopes); NativeIO.ReadAll(r, Counters);
            if (ActiveCount < 0 || ActiveCount > Blades.Length || Tick < 0) throw new InvalidDataException("Invalid sword count/tick.");
            for (int i = 0; i < Blades.Length; i++)
            {
                var b = Blades[i]; var scope = Scopes[i];
                if (b.Active != (i < ActiveCount) || b.Phase > SvSwordPhase.Returning || b.Age < 0 ||
                    !Finite(b.Position) || !Finite(b.Previous) || !Finite(b.Direction) ||
                    !HitHistory.IsValid(History, i * HistoryCapacity, HistoryCapacity, scope) || !scope.Owner.IsNull ||
                    (scope.Pulse == 0 && scope.Count != 0) ||
                    (b.Phase != SvSwordPhase.Orbit && (!b.Timeline.Running || b.Timeline.PulseId == 0 || scope.Pulse != b.Timeline.PulseId)) ||
                    b.Timeline.Tick < 0 || b.Timeline.PreviousTick < -1 || b.Timeline.PreviousTick > b.Timeline.Tick)
                    throw new InvalidDataException("Invalid flying-sword state.");
                for (int j = 0; j < scope.Count; j++)
                {
                    var h = History[i * HistoryCapacity + j];
                    if (h.Index < 0 || h.Generation <= 0) throw new InvalidDataException("Invalid sword history handle.");
                    for (int k = 0; k < j; k++) if (History[i * HistoryCapacity + k] == h) throw new InvalidDataException("Duplicate sword history handle.");
                }
            }
            for (int i = 0; i < Counters.Length; i++) if (Counters[i] < 0) throw new InvalidDataException("Invalid sword counter.");
        }
        static bool Finite(float2 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y);
        public void Dispose() { Blades.Dispose(); History.Dispose(); Scopes.Dispose(); Contacts.Dispose(); Counters.Dispose(); }
    }

    public static class SvSwordRules
    {
        public static int Count(in SvFlyingSwords s, SvGameState g) => math.min(s.Capacity, s.BaseCount + 4 * math.max(0, g.Level0(Upgrade.Bolt) - 1));
        public static int Interval(in SvFlyingSwords s, SvGameState g) => math.max(3, s.OrbitTicks - 3 * g.Level0(Upgrade.Spiral));
        public static int Pierce(in SvFlyingSwords s, SvGameState g) => math.min(s.HistoryPerSword, 3 + 2 * g.Level0(Upgrade.Orbit));
        public static float Radius(in SvFlyingSwords s, SvGameState g) => s.Radius + .035f * g.Level0(Upgrade.Nova);
        public static float Damage(in SvFlyingSwords s, SvGameState g) => s.Damage * (1 + .18f * g.Level0(Upgrade.Nova)) * SvRules.Might(g);
        public static readonly string[] Names = { "Sword Swarm", "Keen Edges", "Sword Tempo", "Piercing Flight", "Swiftness", "Vitality", "Magnet", "Might" };
    }
}

namespace SurvivorFoundation.Systems
{
    sealed class FlyingSwordSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;
        public override int Order => 7;
        public override void Declare(AccessDeclaration a) => a.Read(SvKeys.Enemy).Read(SvKeys.Position).Read(SvKeys.PrevPosition)
            .Read(SvKeys.Info).Read(SvKeys.EnemyGrid).Write(SvKeys.Hits).Write(SvFlyingSwordState.Key);
        public override JobHandle OnTick(in SimContext c, JobHandle dependency)
        {
            var game = c.World.Resource(SvKeys.Game);
            if (game.Flow != SvFlow.Playing) return dependency;
            var settings = c.World.Resource(SvKeys.Config).Settings.FlyingSwords;
            var state = c.Resource(SvFlyingSwordState.Key);
            state.ActiveCount = SvSwordRules.Count(settings, game);
            state.Tick = game.RunTicks;
            return new SwordJob
            {
                Blades = state.Blades, History = state.History, Scopes = state.Scopes, Contacts = state.Contacts, Counters = state.Counters,
                Handles = c.Handles(SvKeys.Enemy), Positions = c.Column(SvKeys.Position), Previous = c.Column(SvKeys.PrevPosition), Infos = c.Column(SvKeys.Info),
                Grid = c.Resource(SvKeys.EnemyGrid).AsReader(), Hits = c.Resource(SvKeys.Hits).AsWriter(), EnemyCount = c.Count(SvKeys.Enemy), Lookup = c.World.Registry.AsLookup(), EnemyTable = c.World.Table(SvKeys.Enemy).TableIndex,
                Settings = settings, Count = state.ActiveCount, HistoryCapacity = state.HistoryCapacity, Tick = state.Tick,
                Hero = game.Hero, Delta = c.Time.DeltaTime, Interval = SvSwordRules.Interval(settings, game), Pierce = SvSwordRules.Pierce(settings, game),
                Damage = SvSwordRules.Damage(settings, game), Radius = SvSwordRules.Radius(settings, game),
            }.Schedule(dependency);
        }
        struct ContactOrder : IComparer<SvSwordContact>
        {
            public int Compare(SvSwordContact a, SvSwordContact b)
            {
                int c = a.Fraction.CompareTo(b.Fraction); if (c != 0) return c;
                c = a.Handle.Index.CompareTo(b.Handle.Index); return c != 0 ? c : a.Handle.Generation.CompareTo(b.Handle.Generation);
            }
        }
        struct ContactVisitor : IGridVisitor
        {
            [ReadOnly] public NativeArray<float2> Positions, Previous;
            [ReadOnly] public NativeArray<EnemyInfo> Infos;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<SvSwordContact> Contacts;
            public float2 From, To;
            public float Radius;
            public int Count, Candidates;
            public bool Visit(in GridEntry e)
            {
                Candidates++;
                if (!Infos[e.Owner].Has(EnemyFlags.Dead) && CombatSweep.Circles(From, To, Radius, Previous[e.Owner], Positions[e.Owner], e.Radius, out float t))
                    Contacts[Count++] = new SvSwordContact { Row = e.Owner, Handle = Handles[e.Owner], Fraction = t };
                return true;
            }
        }
        struct NearestVisitor : IGridVisitor
        {
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            [ReadOnly] public NativeArray<EnemyInfo> Infos;
            public float2 From;
            public float Distance;
            public int Row;
            public bool Visit(in GridEntry e)
            {
                if (Infos[e.Owner].Has(EnemyFlags.Dead)) return true;
                float d = math.distancesq(e.Position, From);
                var h = Handles[e.Owner];
                if (d < Distance || (d == Distance && (Row < 0 || h.Index < Handles[Row].Index ||
                    (h.Index == Handles[Row].Index && h.Generation < Handles[Row].Generation))))
                { Row = e.Owner; Distance = d; }
                return true;
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        struct SwordJob : IJob
        {
            public NativeArray<SvSword> Blades;
            public NativeArray<EntityHandle> History;
            public NativeArray<HitHistoryState> Scopes;
            public NativeArray<SvSwordContact> Contacts;
            public NativeArray<int> Counters;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            [ReadOnly] public NativeArray<float2> Positions, Previous;
            [ReadOnly] public NativeArray<EnemyInfo> Infos;
            public GridReader Grid;
            public ParallelQueue<SvHit>.Writer Hits;
            public SvFlyingSwords Settings;
            public int Count, EnemyCount, HistoryCapacity, Tick, Interval, Pierce, EnemyTable;
            public EntityLookup Lookup;
            public float2 Hero;
            public float Delta, Damage, Radius;
            public void Execute()
            {
                float maxMovement = 0;
                for (int row = 0; row < EnemyCount; row++) maxMovement = math.max(maxMovement, math.distance(Positions[row], Previous[row]));
                for (int i = 0; i < Count; i++)
                {
                    var b = Blades[i]; var scope = Scopes[i];
                    float a = Tick * Delta * Settings.OrbitRadiansPerSecond + i * (2f * math.PI / Count);
                    float2 orbit = Hero + new float2(math.cos(a), math.sin(a)) * Settings.OrbitRadius;
                    if (!b.Active)
                    {
                        b.Active = true; b.Phase = SvSwordPhase.Orbit; b.Position = b.Previous = orbit;
                        b.Direction = new float2(-math.sin(a), math.cos(a)); b.Age = i * Interval / Count;
                    }
                    b.Previous = b.Position;
                    b.Age++;
                    if (b.Phase == SvSwordPhase.Orbit)
                    {
                        b.Position = orbit; b.Direction = math.normalizesafe(b.Position - b.Previous, b.Direction);
                        if (b.Age >= Interval)
                        {
                            int target = Nearest(b.Position);
                            if (target >= 0)
                            {
                                b.Target = Handles[target]; b.Phase = SvSwordPhase.Outbound; b.Age = 0;
                                b.Direction = math.normalizesafe(Positions[target] - b.Position, b.Direction);
                                b.Timeline.Begin(); HitHistory.Begin(History, i * HistoryCapacity, HistoryCapacity, ref scope, EntityHandle.Null, b.Timeline.PulseId);
                                Increment(0);
                            }
                        }
                    }
                    else
                    {
                        b.Timeline.Advance();
                        int row = Find(b.Target);
                        if (b.Phase == SvSwordPhase.Outbound && (row < 0 || b.Age >= Settings.OutboundTicks || scope.Count >= Pierce))
                        { b.Phase = SvSwordPhase.Returning; b.Age = 0; }
                        float2 destination = b.Phase == SvSwordPhase.Returning ? orbit : Positions[row];
                        float2 delta = destination - b.Position;
                        float distance = math.length(delta);
                        float speed = b.Phase == SvSwordPhase.Returning ? Settings.ReturnSpeed : Settings.Speed;
                        float2 desired = math.normalizesafe(delta, b.Direction);
                        b.Direction = math.normalizesafe(math.lerp(b.Direction, desired, math.min(1f, Settings.TurnRate * Delta)), desired);
                        // Return homes directly so a moving hero cannot leave blades in a permanent orbit.
                        if (b.Phase == SvSwordPhase.Returning) b.Direction = desired;
                        b.Position += b.Direction * math.min(speed * Delta, distance);
                        Sweep(ref scope, i, b.Previous, b.Position, maxMovement);
                        if (b.Phase == SvSwordPhase.Outbound && distance <= speed * Delta + Radius)
                        { b.Phase = SvSwordPhase.Returning; b.Age = 0; }
                        else if (b.Phase == SvSwordPhase.Returning && (distance <= speed * Delta || b.Age >= Settings.ReturnTicks))
                        {
                            // Any timeout reposition is non-damaging; never sweep a teleport.
                            b.Phase = SvSwordPhase.Orbit; b.Age = 0; b.Position = b.Previous = orbit; b.Target = default; b.Timeline.Stop();
                        }
                    }
                    Blades[i] = b; Scopes[i] = scope;
                }
            }
            int Find(EntityHandle h) => Lookup.TryResolve(h, out int table, out int row) && table == EnemyTable &&
                (uint)row < (uint)EnemyCount && !Infos[row].Has(EnemyFlags.Dead) ? row : -1;
            int Nearest(float2 from)
            {
                var visitor = new NearestVisitor { Handles = Handles, Infos = Infos, From = from, Row = -1, Distance = Settings.TargetRange * Settings.TargetRange };
                Grid.QueryCells(from - Settings.TargetRange, from + Settings.TargetRange, ref visitor);
                return visitor.Row;
            }
            void Sweep(ref HitHistoryState scope, int sword, float2 from, float2 to, float maxMovement)
            {
                var v = new ContactVisitor { Positions = Positions, Previous = Previous, Infos = Infos, Handles = Handles, Contacts = Contacts, From = from, To = to, Radius = Radius };
                float pad = Radius + Grid.MaxEntryRadius + maxMovement;
                Grid.QueryCells(math.min(from, to) - pad, math.max(from, to) + pad, ref v);
                Add(4, v.Candidates); Add(5, v.Count);
                var ordered = Contacts.GetSubArray(0, v.Count); ordered.Sort(new ContactOrder());
                for (int j = 0; j < ordered.Length; j++)
                {
                    var hit = ordered[j];
                    var result = HitHistory.Check(History, sword * HistoryCapacity, HistoryCapacity, scope, hit.Handle);
                    if (result == HitRecordResult.Duplicate) continue;
                    if (scope.Count >= Pierce || result == HitRecordResult.Full) { Increment(2); continue; }
                    if (result != HitRecordResult.Added) continue;
                    if (!Hits.TryAdd(new SvHit { Target = hit.Row, Damage = Damage })) { Increment(3); continue; }
                    HitHistory.TryRecord(History, sword * HistoryCapacity, HistoryCapacity, ref scope, hit.Handle); Increment(1);
                }
            }
            void Increment(int i) => Add(i, 1);
            void Add(int i, int count) => Counters[i] = count >= int.MaxValue - Counters[i] ? int.MaxValue : Counters[i] + count;
        }
    }
}
