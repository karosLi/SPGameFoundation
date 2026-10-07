using System.Collections.Generic;
using SPF.Contracts.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace RpgFoundation
{
    /// <summary>Stable actor identity first, then a total payload order for equal-owner requests.
    /// Bit ordering for floats also distinguishes signed zero; ordering is not a gameplay distance.</summary>
    public struct RpgProjectileRequestOrder : IComparer<ProjectileRequest>
    {
        static int Float(float a, float b) => math.asuint(a).CompareTo(math.asuint(b));
        public int Compare(ProjectileRequest a, ProjectileRequest b)
        {
            int c = a.OwnerId.CompareTo(b.OwnerId); if (c != 0) return c;
            c = ((byte)a.Team).CompareTo((byte)b.Team); if (c != 0) return c;
            c = ((byte)a.Visual).CompareTo((byte)b.Visual); if (c != 0) return c;
            c = Float(a.Position.x, b.Position.x); if (c != 0) return c; c = Float(a.Position.y, b.Position.y); if (c != 0) return c;
            c = Float(a.Direction.x, b.Direction.x); if (c != 0) return c; c = Float(a.Direction.y, b.Direction.y); if (c != 0) return c;
            c = Float(a.Speed, b.Speed); if (c != 0) return c; c = Float(a.Radius, b.Radius); if (c != 0) return c;
            c = Float(a.Damage, b.Damage); if (c != 0) return c; c = Float(a.CritChance, b.CritChance); if (c != 0) return c;
            c = Float(a.Knockback, b.Knockback); if (c != 0) return c; c = Float(a.ExplodeRadius, b.ExplodeRadius); if (c != 0) return c;
            c = a.Pierce.CompareTo(b.Pierce); if (c != 0) return c; c = Float(a.Life, b.Life); if (c != 0) return c;
            c = ((byte)a.Status.Kind).CompareTo((byte)b.Status.Kind); if (c != 0) return c;
            c = Float(a.Status.Dps, b.Status.Dps); return c != 0 ? c : Float(a.Status.Duration, b.Status.Duration);
        }
    }

    /// <summary>Each action phase can emit at most one projectile per actor/tick. Writers therefore
    /// own a fixed row slot. Gather runs after all writers, sorts by stable identity, and applies the
    /// existing queue capacity in that order, including deterministic reject-newest overflow.
    /// Scratch is fully overwritten/flagged before use and is not future simulation state.</summary>
    [BurstCompile(CompileSynchronously = true)]
    internal struct GatherProjectileRequestsJob : IJob
    {
        public NativeArray<ProjectileRequest> Scratch;
        [ReadOnly] public NativeArray<byte> Pending;
        public ParallelQueue<ProjectileRequest>.Writer Queue;
        public int Actors;
        public void Execute()
        {
            int count = 0;
            for (int i = 0; i < Actors; i++) if (Pending[i] != 0) Scratch[count++] = Scratch[i];
            Scratch.GetSubArray(0, count).Sort(new RpgProjectileRequestOrder());
            for (int i = 0; i < count; i++) Queue.TryAdd(Scratch[i]);
        }
    }
}
