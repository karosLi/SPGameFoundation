using System;
using SPF.L1.Spatial;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.L1.Navigation
{
    /// <summary>
    /// Distance field over a <see cref="TileMap"/> towards one or more goal tiles (breadth-first search,
    /// 4-connected, in tile steps): every agent heading to the same goal (the player, an exit) reads its
    /// direction in O(1) instead of running its own path search. Rebuilt by a Burst job when the goal tile
    /// changes; <see cref="MaxDistance"/> bounds the search for large maps.
    /// </summary>
    public sealed class FlowField : IDisposable
    {
        public const ushort Unreachable = ushort.MaxValue;

        NativeArray<ushort> m_Distance;
        NativeArray<int> m_Queue;
        NativeArray<int2> m_Goals;

        public FlowField(int2 size, int maxGoals = 8)
        {
            Size = size;
            m_Distance = new NativeArray<ushort>(size.x * size.y, Allocator.Persistent);
            m_Queue = new NativeArray<int>(size.x * size.y, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Goals = new NativeArray<int2>(maxGoals, Allocator.Persistent);
            for (int i = 0; i < m_Distance.Length; i++) m_Distance[i] = Unreachable;
        }

        public int2 Size { get; }
        public NativeArray<ushort> Distance => m_Distance;
        public int MaxDistance { get; set; } = Unreachable - 1;

        /// <summary>Goal tiles of the last scheduled build.</summary>
        public int GoalCount { get; private set; }
        public int2 Goal(int i) => m_Goals[i];

        public FlowFieldView AsView(in TileMapView map) => new FlowFieldView(m_Distance, map);

        /// <summary>Schedules a rebuild towards the given goal tiles (main thread; copies the goals).</summary>
        public JobHandle ScheduleBuild(in TileMapView map, ReadOnlySpan<int2> goals, JobHandle dependency)
        {
            GoalCount = math.min(goals.Length, m_Goals.Length);
            for (int i = 0; i < GoalCount; i++) m_Goals[i] = goals[i];
            return new BuildJob
            {
                Map = map,
                Goals = m_Goals,
                GoalCount = GoalCount,
                Distance = m_Distance,
                Queue = m_Queue,
                MaxDistance = MaxDistance,
            }.Schedule(dependency);
        }

        public void Dispose()
        {
            if (m_Distance.IsCreated) m_Distance.Dispose();
            if (m_Queue.IsCreated) m_Queue.Dispose();
            if (m_Goals.IsCreated) m_Goals.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct BuildJob : IJob
        {
            public TileMapView Map;
            [ReadOnly] public NativeArray<int2> Goals;
            public int GoalCount;
            public NativeArray<ushort> Distance;
            public NativeArray<int> Queue;
            public int MaxDistance;

            public void Execute()
            {
                for (int i = 0; i < Distance.Length; i++) Distance[i] = Unreachable;
                int head = 0, tail = 0;
                for (int g = 0; g < GoalCount; g++)
                {
                    int2 cell = Goals[g];
                    if (Map.IsSolid(cell)) continue;
                    int index = Map.Index(cell);
                    if (Distance[index] == 0) continue;
                    Distance[index] = 0;
                    Queue[tail++] = index;
                }
                int width = Map.Size.x;
                while (head < tail)
                {
                    int index = Queue[head++];
                    int d = Distance[index] + 1;
                    if (d > MaxDistance) continue;
                    int2 cell = new int2(index % width, index / width);
                    Visit(cell + new int2(1, 0), d, ref tail);
                    Visit(cell + new int2(-1, 0), d, ref tail);
                    Visit(cell + new int2(0, 1), d, ref tail);
                    Visit(cell + new int2(0, -1), d, ref tail);
                }
            }

            void Visit(int2 cell, int d, ref int tail)
            {
                if (Map.IsSolid(cell)) return;
                int index = Map.Index(cell);
                if (Distance[index] != Unreachable) return;
                Distance[index] = (ushort)d;
                Queue[tail++] = index;
            }
        }
    }

    /// <summary>Read-only job view: direction towards the goal from any position.</summary>
    public struct FlowFieldView
    {
        [ReadOnly] NativeArray<ushort> m_Distance;
        TileMapView m_Map;

        public FlowFieldView(NativeArray<ushort> distance, in TileMapView map)
        {
            m_Distance = distance;
            m_Map = map;
        }

        public ushort DistanceAt(int2 cell) => m_Map.InBounds(cell) ? m_Distance[m_Map.Index(cell)] : FlowField.Unreachable;

        /// <summary>
        /// Unit direction from <paramref name="position"/> to the centre of the best neighbouring tile
        /// (8 neighbours, no diagonal corner cutting; straight moves win ties). Zero at the goal or when
        /// unreachable.
        /// </summary>
        public float2 Direction(float2 position)
        {
            int2 cell = m_Map.CellOf(position);
            int best = DistanceAt(cell);
            if (best == FlowField.Unreachable || best == 0) return float2.zero;
            int2 target = cell;
            for (int k = 0; k < 8; k++)
            {
                int2 o = k switch { 0 => new int2(1, 0), 1 => new int2(-1, 0), 2 => new int2(0, 1), 3 => new int2(0, -1),
                    4 => new int2(1, 1), 5 => new int2(-1, 1), 6 => new int2(1, -1), _ => new int2(-1, -1) };
                if (k >= 4 && (m_Map.IsSolid(cell + new int2(o.x, 0)) || m_Map.IsSolid(cell + new int2(0, o.y)))) continue;
                int d = DistanceAt(cell + o);
                // Straight neighbours are tried first, so a diagonal only wins when strictly closer.
                if (d < best) { best = d; target = cell + o; }
            }
            if (math.all(target == cell)) return float2.zero;
            return math.normalizesafe(m_Map.CenterOf(target) - position);
        }
    }
}
