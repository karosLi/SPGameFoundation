using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Samples.DriftSmoke
{
    /// <summary>
    /// M1 smoke test for the foundation: thousands of points that spawn on the main thread, move in a
    /// Burst job, get randomly destroyed from a parallel job through the destroy queue, and publish a
    /// snapshot for presentation. Exercises every M1 mechanism without any gameplay.
    /// </summary>
    [CreateAssetMenu(menuName = "SPF/Samples/Drift Smoke Module", fileName = "DriftSmokeModule")]
    public sealed class DriftSmokeModule : GameplayModuleAsset
    {
        [SerializeField, Min(1)] int m_Count = 10000;
        [SerializeField, Min(1f)] float m_WorldSize = 200f;
        [SerializeField, Min(0f)] float m_MaxSpeed = 8f;
        [SerializeField, Range(0f, 0.1f)] float m_RecycleChancePerTick = 0.002f;

        public static DriftSmokeModule CreateRuntime(int count, float worldSize, float maxSpeed, float recycleChance)
        {
            var module = CreateInstance<DriftSmokeModule>();
            module.m_Count = count;
            module.m_WorldSize = worldSize;
            module.m_MaxSpeed = maxSpeed;
            module.m_RecycleChancePerTick = recycleChance;
            return module;
        }

        public override void DeclareData(WorldLayout layout)
        {
            layout.Table(DriftKeys.Drifter, m_Count)
                .Column(DriftKeys.Position)
                .Column(DriftKeys.Velocity);
            layout.Resource(DriftKeys.Snapshot, new SnapshotBuffer<float2>(m_Count));
        }

        public override void RegisterSystems(SystemRegistry registry)
        {
            registry
                .Add(new DriftSpawnSystem(m_Count, m_WorldSize, m_MaxSpeed))
                .Add(new DriftMoveSystem(m_WorldSize))
                .Add(new DriftRecycleSystem(m_RecycleChancePerTick))
                .Add(new DriftSnapshotSystem());
        }
    }

    public static class DriftKeys
    {
        public static readonly TableKey Drifter = new TableKey("Drifter");
        public static readonly ColumnKey<float2> Position = new ColumnKey<float2>(Drifter, "Position");
        public static readonly ColumnKey<float2> Velocity = new ColumnKey<float2>(Drifter, "Velocity");
        public static readonly ResourceKey<SnapshotBuffer<float2>> Snapshot = new ResourceKey<SnapshotBuffer<float2>>("Drifter.Snapshot");
    }

    /// <summary>Keeps the population at its target (main thread, structural).</summary>
    sealed class DriftSpawnSystem : SimSystemBase
    {
        readonly int m_Target;
        readonly float m_WorldSize;
        readonly float m_MaxSpeed;

        public DriftSpawnSystem(int target, float worldSize, float maxSpeed)
        {
            m_Target = target;
            m_WorldSize = worldSize;
            m_MaxSpeed = maxSpeed;
        }

        public override SimPhase Phase => SimPhase.ApplyCommands;

        public override void Declare(AccessDeclaration access) =>
            access.Write(DriftKeys.Drifter).Write(DriftKeys.Position).Write(DriftKeys.Velocity);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var table = world.Table(DriftKeys.Drifter);
            int missing = m_Target - table.Count;
            if (missing <= 0)
                return dependency;

            var positions = table.Column(DriftKeys.Position);
            var velocities = table.Column(DriftKeys.Velocity);
            for (int i = 0; i < missing; i++)
            {
                var handle = world.CreateEntity(DriftKeys.Drifter, out int row);
                if (handle.IsNull)
                    break;
                var random = SimRandom.Create(context.Seed, context.Time.Tick, (uint)handle.Index);
                positions[row] = random.NextFloat2(new float2(-m_WorldSize), new float2(m_WorldSize)) * 0.5f;
                velocities[row] = random.NextFloat2Direction() * random.NextFloat(0.2f, 1f) * m_MaxSpeed;
            }
            return dependency;
        }
    }

    sealed class DriftMoveSystem : SimSystemBase
    {
        readonly float m_HalfSize;

        public DriftMoveSystem(float worldSize) => m_HalfSize = worldSize * 0.5f;

        public override SimPhase Phase => SimPhase.Move;

        public override void Declare(AccessDeclaration access) =>
            access.Write(DriftKeys.Position).Write(DriftKeys.Velocity);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            int count = context.Count(DriftKeys.Drifter);
            return new MoveJob
            {
                Positions = context.WriteColumn(DriftKeys.Position),
                Velocities = context.WriteColumn(DriftKeys.Velocity),
                DeltaTime = context.Time.DeltaTime,
                HalfSize = m_HalfSize,
            }.Schedule(count, 256, dependency);
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        struct MoveJob : IJobParallelFor
        {
            public NativeArray<float2> Positions;
            public NativeArray<float2> Velocities;
            public float DeltaTime;
            public float HalfSize;

            public void Execute(int index)
            {
                float2 position = Positions[index] + Velocities[index] * DeltaTime;
                float2 velocity = Velocities[index];
                bool2 outside = math.abs(position) > HalfSize;
                velocity = math.select(velocity, -velocity, outside);
                position = math.clamp(position, -HalfSize, HalfSize);
                Positions[index] = position;
                Velocities[index] = velocity;
            }
        }
    }

    /// <summary>Randomly destroys entities from a parallel job to exercise the destroy queue.</summary>
    sealed class DriftRecycleSystem : SimSystemBase
    {
        readonly float m_Chance;

        public DriftRecycleSystem(float chance) => m_Chance = chance;

        public override SimPhase Phase => SimPhase.Resolve;

        public override void Declare(AccessDeclaration access) =>
            access.Read(DriftKeys.Drifter).Write(SimWorld.DestroyQueueKey);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            if (m_Chance <= 0f)
                return dependency;
            int count = context.Count(DriftKeys.Drifter);
            return new RecycleJob
            {
                Handles = context.Handles(DriftKeys.Drifter),
                Destroy = context.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                Seed = context.Seed,
                Tick = context.Time.Tick,
                Chance = m_Chance,
            }.Schedule(count, 512, dependency);
        }

        [BurstCompile]
        struct RecycleJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public SPF.Contracts.Collections.ParallelQueue<EntityHandle>.Writer Destroy;
            public uint Seed;
            public uint Tick;
            public float Chance;

            public void Execute(int index)
            {
                var handle = Handles[index];
                var random = SimRandom.Create(Seed, Tick, (uint)handle.Index ^ 0x9E3779B9u);
                if (random.NextFloat() < Chance)
                    Destroy.TryAdd(handle);
            }
        }
    }

    sealed class DriftSnapshotSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Snapshot;

        public override void Declare(AccessDeclaration access) =>
            access.Read(DriftKeys.Position).Write(DriftKeys.Snapshot);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var snapshot = context.Resource(DriftKeys.Snapshot);
            return new CopyJob
            {
                Source = context.Column(DriftKeys.Position),
                Destination = snapshot.Write,
                WriteCount = snapshot.WriteCount,
                Count = context.Count(DriftKeys.Drifter),
            }.Schedule(dependency);
        }

        [BurstCompile]
        struct CopyJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Source;
            [WriteOnly] public NativeArray<float2> Destination;
            [WriteOnly] public NativeArray<int> WriteCount;
            public int Count;

            public void Execute()
            {
                int count = math.min(Count, Destination.Length);
                NativeArray<float2>.Copy(Source, Destination, count);
                WriteCount[0] = count;
            }
        }
    }
}
