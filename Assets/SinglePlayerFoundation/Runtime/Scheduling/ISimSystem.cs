using SPF.Contracts;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Jobs;

namespace SPF.Runtime.Scheduling
{
    /// <summary>
    /// Stateless unit of simulation logic. A system declares which keys it reads and writes once at
    /// build time; every tick it receives a dependency covering exactly those keys and returns the
    /// handle of the jobs it scheduled (or the dependency unchanged when it did main-thread work).
    /// </summary>
    public interface ISimSystem
    {
        SimPhase Phase { get; }

        /// <summary>Ordering inside the phase; lower runs first.</summary>
        int Order { get; }

        void Declare(AccessDeclaration access);
        void OnCreate(SimWorld world);
        JobHandle OnTick(in SimContext context, JobHandle dependency);
        void OnDestroy(SimWorld world);
    }

    /// <summary>Optional: reset per-session state on restart without reallocation.</summary>
    public interface IResettableSystem
    {
        void OnReset(SimWorld world);
    }

    /// <summary>
    /// Optional: a system that keeps simulation-relevant state between ticks (caches keyed on versions,
    /// timers) saves it in world snapshots so a restored session continues identically.
    /// </summary>
    public interface ISnapshotSystem
    {
        void WriteSnapshot(System.IO.BinaryWriter writer);
        void ReadSnapshot(System.IO.BinaryReader reader, SimWorld world);
    }

    /// <summary>Convenience base with empty lifecycle hooks.</summary>
    public abstract class SimSystemBase : ISimSystem
    {
        public abstract SimPhase Phase { get; }
        public virtual int Order => 0;
        public abstract void Declare(AccessDeclaration access);
        public virtual void OnCreate(SimWorld world) { }
        public abstract JobHandle OnTick(in SimContext context, JobHandle dependency);
        public virtual void OnDestroy(SimWorld world) { }
    }

    /// <summary>Per-tick view passed to systems.</summary>
    public readonly struct SimContext
    {
        public readonly SimWorld World;
        public readonly TickTime Time;

        public SimContext(SimWorld world, TickTime time)
        {
            World = world;
            Time = time;
        }

        public uint Seed => World.Seed;
        public int Count(TableKey table) => World.Table(table).Count;
        public NativeArray<T> Column<T>(ColumnKey<T> key) where T : unmanaged => World.Column(key);
        public NativeArray<EntityHandle> Handles(TableKey table) => World.Table(table).Handles;
        public T Resource<T>(ResourceKey<T> key) where T : class => World.Resource(key);
    }
}
