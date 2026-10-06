using System;
using System.IO;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Mathematics;

namespace BrawlerFoundation
{
    /// <summary>Opt-in bounded stable-identity attacks. Fighter movement/combat still use pairwise
    /// loops, so this example deliberately caps at 128 fighters; it is not a dense-horde solution.</summary>
    [Serializable]
    public struct BwSharedCombatConfig
    {
        public int Fighters, TargetsPerAttack;
        public static BwSharedCombatConfig Default => new BwSharedCombatConfig { Fighters = 64, TargetsPerAttack = 64 };
        public void Validate()
        {
            if (Fighters < 1 || Fighters > 128 || TargetsPerAttack < 1 || TargetsPerAttack > 128)
                throw new ArgumentOutOfRangeException(nameof(Fighters), "Shared brawler supports 1..128 fighters and targets per attack.");
        }
    }

    public struct BwSharedAttack
    {
        public HitHistoryState History;
        public ActionTimeline Timeline;
        public AttackKind Attack;
    }

    /// <summary>One scope per simultaneous fighter action, keyed by FULL source handle, never fighter
    /// row/registry slot. Linear bounded lookup avoids assumptions about other modules' registry slots.
    /// Removed/interrupted owners are released before acquisition. State is level-scoped and versioned.</summary>
    public sealed class BwSharedCombatState : IDisposable, IResettableResource, ISnapshotResource
    {
        const int Magic = 0x42534332;
        public NativeArray<BwSharedAttack> Attacks;
        public NativeArray<EntityHandle> Targets;
        public readonly int TargetsPerAttack;
        public int RejectedHits, RejectedScopes;

        public BwSharedCombatState(BwSharedCombatConfig config)
        {
            config.Validate();
            TargetsPerAttack = config.TargetsPerAttack;
            Attacks = new NativeArray<BwSharedAttack>(config.Fighters, Allocator.Persistent);
            Targets = new NativeArray<EntityHandle>(config.Fighters * config.TargetsPerAttack, Allocator.Persistent);
        }

        public void Prune(SimWorld world)
        {
            var table = world.Table(BwKeys.Fighter);
            var info = world.Column(BwKeys.Info);
            for (int i = 0; i < Attacks.Length; i++)
            {
                var scope = Attacks[i];
                if (scope.History.Pulse == 0) continue;
                if (world.Registry.TryResolve(scope.History.Owner, out int tableIndex, out int row) &&
                    tableIndex == table.TableIndex && info[row].State == FighterState.Attack) continue;
                HitHistory.Release(Targets, i * TargetsPerAttack, TargetsPerAttack, ref scope.History);
                Attacks[i] = default;
            }
        }

        /// <summary>Called exactly once per fighter per fixed tick by the existing combat loop.
        /// Every real StartAttack sets StateTime to 0, including same-kind restarts and combo changes.</summary>
        public int Track(EntityHandle owner, in FighterInfo fighter, float dt)
        {
            int slot = -1, free = -1;
            for (int i = 0; i < Attacks.Length; i++)
            {
                if (Attacks[i].History.Pulse == 0) { if (free < 0) free = i; }
                else if (Attacks[i].History.Owner == owner) { slot = i; break; }
            }
            bool fresh = slot < 0;
            if (fresh) slot = free;
            if (slot < 0) { if (RejectedScopes < int.MaxValue) RejectedScopes++; return -1; }
            var scope = Attacks[slot];
            if (fresh || scope.Attack != fighter.Attack || fighter.StateTime == 0f)
            {
                scope.Timeline.Begin();
                // Scripted setup may enter midway; ordinary StartAttack always enters at tick zero.
                if (fresh && fighter.StateTime > 0f) scope.Timeline.Advance((int)math.round(fighter.StateTime / dt));
                HitHistory.Begin(Targets, slot * TargetsPerAttack, TargetsPerAttack, ref scope.History, owner, scope.Timeline.PulseId);
                scope.Attack = fighter.Attack;
            }
            else scope.Timeline.Advance();
            Attacks[slot] = scope;
            return slot;
        }

        public bool CrossedActive(int slot, float from, float to, float dt)
        {
            // Authored seconds become a closed set of fixed ticks, without float accumulation drift.
            var window = new ActionWindow((int)math.ceil(from / dt), (int)math.floor(to / dt) + 1);
            return Attacks[slot].Timeline.Crossed(window);
        }

        public HitRecordResult Record(int slot, EntityHandle target)
        {
            var scope = Attacks[slot];
            var result = HitHistory.TryRecord(Targets, slot * TargetsPerAttack, TargetsPerAttack, ref scope.History, target);
            Attacks[slot] = scope;
            if (result == HitRecordResult.Full && RejectedHits < int.MaxValue) RejectedHits++;
            return result;
        }

        public void OnReset()
        {
            for (int i = 0; i < Attacks.Length; i++) Attacks[i] = default;
            for (int i = 0; i < Targets.Length; i++) Targets[i] = default;
            RejectedHits = RejectedScopes = 0;
        }

        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Magic); w.Write(1); w.Write(Attacks.Length); w.Write(TargetsPerAttack);
            w.Write(RejectedHits); w.Write(RejectedScopes);
            for (int i = 0; i < Attacks.Length; i++)
            {
                var a = Attacks[i];
                NativeIO.Write(w, a.History.Owner); w.Write(a.History.Pulse); w.Write(a.History.Count);
                w.Write(a.Timeline.PreviousTick); w.Write(a.Timeline.Tick); w.Write(a.Timeline.PulseId); w.Write(a.Timeline.Running);
                w.Write((byte)a.Attack);
            }
            NativeIO.Write(w, Targets);
        }

        public void ReadSnapshot(BinaryReader r)
        {
            if (r.ReadInt32() != Magic || r.ReadInt32() != 1 || r.ReadInt32() != Attacks.Length || r.ReadInt32() != TargetsPerAttack)
                throw new InvalidDataException("Unsupported shared-brawler snapshot layout.");
            RejectedHits = r.ReadInt32(); RejectedScopes = r.ReadInt32();
            if (RejectedHits < 0 || RejectedScopes < 0) throw new InvalidDataException("Invalid brawler rejection count.");
            for (int i = 0; i < Attacks.Length; i++)
            {
                var a = new BwSharedAttack
                {
                    History = new HitHistoryState { Owner = NativeIO.ReadHandle(r), Pulse = r.ReadUInt32(), Count = r.ReadInt32() },
                    Timeline = new ActionTimeline { PreviousTick = r.ReadInt32(), Tick = r.ReadInt32(), PulseId = r.ReadUInt32(), Running = r.ReadBoolean() },
                    Attack = (AttackKind)r.ReadByte(),
                };
                if (!HitHistory.IsValid(Targets, i * TargetsPerAttack, TargetsPerAttack, a.History) ||
                    (a.History.Pulse == 0 ? a.History.Count != 0 || !a.History.Owner.IsNull :
                        a.History.Owner.Index < 0 || a.History.Owner.Generation <= 0 || a.History.Pulse != a.Timeline.PulseId ||
                        !a.Timeline.Running || a.Attack < AttackKind.Jab || a.Attack > AttackKind.Kick) ||
                    a.Timeline.Tick < 0 || a.Timeline.PreviousTick < -1 || a.Timeline.PreviousTick > a.Timeline.Tick)
                    throw new InvalidDataException("Invalid shared-brawler attack scope.");
                Attacks[i] = a;
            }
            NativeIO.ReadAll(r, Targets);
            for (int i = 0; i < Attacks.Length; i++)
                for (int j = 0; j < Attacks[i].History.Count; j++)
                {
                    var target = Targets[i * TargetsPerAttack + j];
                    if (target.Index < 0 || target.Generation <= 0) throw new InvalidDataException("Invalid saved brawler target.");
                }
        }

        public void Dispose() { Attacks.Dispose(); Targets.Dispose(); }
    }
}
