using System;
using System.Runtime.CompilerServices;
using SPF.L1.Spatial;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    /// <summary>
    /// Test-only, fixed-allocation bucket quadtree for comparison with <see cref="SpatialGrid"/>.
    /// Centers are accepted in the half-open window and stably partitioned into quadrants. Each
    /// accepted entry belongs to exactly one contiguous leaf range; internal nodes only hold ranges
    /// and a maximum radius. Oversized leaves remain queryable when a split reaches a bound.
    /// Build and query are iterative and allocate nothing. As with SpatialGrid, circles and query
    /// radii are expected to be nonnegative and finite. Dispose only after dependent jobs complete.
    /// </summary>
    public sealed class BoundedQuadtreeReference : IDisposable
    {
        const int StatEntries = 0;
        const int StatRejected = 1;
        const int StatSaturated = 2;
        const int StatNodes = 3;
        const int StatDepth = 4;
        const int StatCount = 5;

        NativeArray<GridEntry> m_Staging;
        NativeArray<GridEntry> m_Entries;
        NativeArray<GridEntry> m_Scratch;
        NativeArray<Node> m_Nodes;
        NativeArray<int> m_Stats;

        // Next is the next node after this entire subtree, not necessarily the next array element.
        // These threaded links permit read-only, stackless depth-first traversal in parallel jobs.
        internal struct Node
        {
            public float2 Min;
            public float2 Max;
            public float MaxRadius;
            public int Start;
            public int Count;
            public int FirstChild;
            public int Next;
            public int Depth;
        }

        public BoundedQuadtreeReference(float2 origin, float2 size, int capacity,
            int bucketSize = 16, int maxDepth = 12, int nodeCapacity = 0)
        {
            if (!math.all(math.isfinite(origin))) throw new ArgumentOutOfRangeException(nameof(origin));
            if (!math.all(math.isfinite(size)) || math.any(size <= 0f)
                || !math.all(math.isfinite(origin + size)) || math.any(origin + size <= origin))
                throw new ArgumentOutOfRangeException(nameof(size));
            if (capacity < 0 || capacity > GridEntry.MaxOwner + 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be in [0, 65536]");
            if (bucketSize < 1) throw new ArgumentOutOfRangeException(nameof(bucketSize));
            if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            if (nodeCapacity < 0) throw new ArgumentOutOfRangeException(nameof(nodeCapacity));

            Origin = origin;
            Size = size;
            BucketSize = bucketSize;
            MaxDepth = maxDepth;
            // A bounded comparison budget, not a promise that every possible distribution fits.
            // An explicitly smaller budget is useful for testing oversized-leaf fallback.
            if (nodeCapacity == 0)
                nodeCapacity = 1 + 4 * (capacity == 0 ? 0 : 1 + (capacity - 1) / bucketSize);
            m_Staging = new NativeArray<GridEntry>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Entries = new NativeArray<GridEntry>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Scratch = new NativeArray<GridEntry>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Nodes = new NativeArray<Node>(nodeCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Stats = new NativeArray<int>(StatCount, Allocator.Persistent);
        }

        public float2 Origin { get; }
        public float2 Size { get; }
        public int BucketSize { get; }
        public int MaxDepth { get; }
        public int Capacity => m_Staging.Length;
        public int NodeCapacity => m_Nodes.Length;
        public NativeArray<GridEntry> Staging => m_Staging;
        public int EntryCount => m_Stats[StatEntries];
        public int RejectedCount => m_Stats[StatRejected];

        /// <summary>Oversized leaves stopped by the node budget, maximum depth, or float precision.</summary>
        public int SaturatedSplits => m_Stats[StatSaturated];
        public int NodeCount => m_Stats[StatNodes];

        /// <summary>Maximum populated node depth, with the root at zero (also zero for an empty tree).</summary>
        public int Depth => m_Stats[StatDepth];

        /// <summary>Native element bytes, including staging and stable-partition scratch; excludes allocator overhead.</summary>
        public long AllocatedBytes => 3L * Capacity * UnsafeUtility.SizeOf<GridEntry>()
            + (long)NodeCapacity * UnsafeUtility.SizeOf<Node>() + StatCount * sizeof(int);

        public void Build(int count) => AsBuilder().Build(count);

        /// <summary>Single-writer value view suitable as a Burst job field. Its stats are shared with readers.</summary>
        public Builder AsBuilder() => new Builder
        {
            Staging = m_Staging,
            Entries = m_Entries,
            Scratch = m_Scratch,
            Nodes = m_Nodes,
            Stats = m_Stats,
            Origin = Origin,
            Max = Origin + Size,
            BucketSize = BucketSize,
            MaxDepth = MaxDepth,
        };

        /// <summary>May be obtained before a scheduled build; use only after the build dependency completes.</summary>
        public Reader AsReader() => new Reader(m_Entries, m_Nodes, m_Stats, Origin, Origin + Size);

        public void Dispose()
        {
            if (m_Staging.IsCreated) m_Staging.Dispose();
            if (m_Entries.IsCreated) m_Entries.Dispose();
            if (m_Scratch.IsCreated) m_Scratch.Dispose();
            if (m_Nodes.IsCreated) m_Nodes.Dispose();
            if (m_Stats.IsCreated) m_Stats.Dispose();
        }

        public struct Builder
        {
            [ReadOnly] internal NativeArray<GridEntry> Staging;
            internal NativeArray<GridEntry> Entries;
            internal NativeArray<GridEntry> Scratch;
            internal NativeArray<Node> Nodes;
            internal NativeArray<int> Stats;
            internal float2 Origin;
            internal float2 Max;
            internal int BucketSize;
            internal int MaxDepth;

            /// <summary>
            /// Obtain inside Execute after Build when a single job builds and queries. This avoids
            /// scheduling the same writable containers twice as separate Builder and Reader fields.
            /// </summary>
            public Reader AsReader() => new Reader(Entries, Nodes, Stats, Origin, Max);

            /// <summary>
            /// Rebuilds from the first count staging slots. Excess requested slots and centers outside
            /// the half-open window are rejected; an exhausted split budget never rejects an entry.
            /// The staging array is not modified. Negative counts produce an empty tree.
            /// </summary>
            public void Build(int count)
            {
                int available = math.min(math.max(count, 0), Staging.Length);
                int rejected = math.max(count - available, 0);
                int accepted = 0;
                float maxRadius = 0f;
                for (int i = 0; i < available; i++)
                {
                    GridEntry entry = Staging[i];
                    if (!math.all(entry.Position >= Origin) || !math.all(entry.Position < Max))
                    {
                        rejected++;
                        continue;
                    }
                    Entries[accepted++] = entry;
                    maxRadius = math.max(maxRadius, entry.Radius);
                }

                Stats[StatEntries] = accepted;
                Stats[StatRejected] = rejected;
                Stats[StatSaturated] = 0;
                Stats[StatNodes] = 0;
                Stats[StatDepth] = 0;
                if (accepted == 0) return;

                Nodes[0] = new Node
                {
                    Min = Origin, Max = Max, MaxRadius = maxRadius,
                    Start = 0, Count = accepted, FirstChild = -1, Next = -1, Depth = 0,
                };
                int nodeCount = 1;
                int deepest = 0;
                int saturated = 0;
                // Nodes are appended breadth-first. Child entry ranges are disjoint, so subsequent
                // stable partitions cannot disturb another node's range or its recorded radius.
                for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
                {
                    Node node = Nodes[nodeIndex];
                    if (node.Count <= BucketSize) continue;
                    float2 middle = node.Min + (node.Max - node.Min) * 0.5f;
                    if (node.Depth >= MaxDepth || math.any(middle <= node.Min) || math.any(middle >= node.Max))
                    {
                        saturated++;
                        continue;
                    }

                    int4 counts = default;
                    float4 radii = default;
                    int end = node.Start + node.Count;
                    for (int i = node.Start; i < end; i++)
                    {
                        GridEntry entry = Entries[i];
                        int quadrant = Quadrant(entry.Position, middle);
                        counts[quadrant] = counts[quadrant] + 1;
                        radii[quadrant] = math.max(radii[quadrant], entry.Radius);
                    }

                    int children = (counts.x > 0 ? 1 : 0) + (counts.y > 0 ? 1 : 0)
                        + (counts.z > 0 ? 1 : 0) + (counts.w > 0 ? 1 : 0);
                    if (children > Nodes.Length - nodeCount)
                    {
                        saturated++;
                        continue;
                    }

                    int4 starts = new int4(node.Start, node.Start + counts.x,
                        node.Start + counts.x + counts.y, node.Start + counts.x + counts.y + counts.z);
                    int4 cursors = starts;
                    for (int i = node.Start; i < end; i++)
                    {
                        GridEntry entry = Entries[i];
                        int quadrant = Quadrant(entry.Position, middle);
                        Scratch[cursors[quadrant]] = entry;
                        cursors[quadrant] = cursors[quadrant] + 1;
                    }
                    for (int i = node.Start; i < end; i++) Entries[i] = Scratch[i];

                    node.FirstChild = nodeCount;
                    Nodes[nodeIndex] = node;
                    int childEnd = nodeCount + children;
                    int childDepth = node.Depth + 1;
                    deepest = math.max(deepest, childDepth);
                    for (int quadrant = 0; quadrant < 4; quadrant++)
                    {
                        if (counts[quadrant] == 0) continue;
                        bool right = (quadrant & 1) != 0;
                        bool upper = (quadrant & 2) != 0;
                        Nodes[nodeCount] = new Node
                        {
                            Min = new float2(right ? middle.x : node.Min.x, upper ? middle.y : node.Min.y),
                            Max = new float2(right ? node.Max.x : middle.x, upper ? node.Max.y : middle.y),
                            MaxRadius = radii[quadrant],
                            Start = starts[quadrant], Count = counts[quadrant], FirstChild = -1,
                            Next = nodeCount + 1 < childEnd ? nodeCount + 1 : node.Next,
                            Depth = childDepth,
                        };
                        nodeCount++;
                    }
                }
                Stats[StatSaturated] = saturated;
                Stats[StatNodes] = nodeCount;
                Stats[StatDepth] = deepest;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int Quadrant(float2 position, float2 middle) =>
                (position.x >= middle.x ? 1 : 0) | (position.y >= middle.y ? 2 : 0);
        }

        /// <summary>Read-only value view; independent readers need no scratch or traversal stack.</summary>
        public struct Reader
        {
            [ReadOnly] NativeArray<GridEntry> m_Entries;
            [ReadOnly] NativeArray<Node> m_Nodes;
            [ReadOnly] NativeArray<int> m_Stats;
            float2 m_Origin;
            float2 m_Max;

            internal Reader(NativeArray<GridEntry> entries, NativeArray<Node> nodes, NativeArray<int> stats,
                float2 origin, float2 max)
            {
                m_Entries = entries;
                m_Nodes = nodes;
                m_Stats = stats;
                m_Origin = origin;
                m_Max = max;
            }

            public float2 Origin => m_Origin;
            public float2 Max => m_Max;
            public int EntryCount => m_Stats[StatEntries];
            public float MaxEntryRadius => m_Stats[StatNodes] == 0 ? 0f : m_Nodes[0].MaxRadius;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Covers(float2 position) => math.all(position >= m_Origin) && math.all(position < m_Max);

            /// <summary>Strict circle overlap, matching GridReader. False means the visitor stopped early.</summary>
            public bool Query<TVisitor>(float2 center, float radius, ref TVisitor visitor)
                where TVisitor : struct, IGridVisitor
            {
                int nodeIndex = m_Stats[StatNodes] == 0 ? -1 : 0;
                while (nodeIndex >= 0)
                {
                    Node node = m_Nodes[nodeIndex];
                    if (!MayOverlap(node, center, radius))
                    {
                        nodeIndex = node.Next;
                        continue;
                    }
                    if (node.FirstChild >= 0)
                    {
                        nodeIndex = node.FirstChild;
                        continue;
                    }
                    int end = node.Start + node.Count;
                    for (int i = node.Start; i < end; i++)
                    {
                        GridEntry entry = m_Entries[i];
                        float reach = radius + entry.Radius;
                        if (math.distancesq(center, entry.Position) < reach * reach && !visitor.Visit(entry))
                            return false;
                    }
                    nodeIndex = node.Next;
                }
                return true;
            }

            /// <summary>
            /// Counted query, kept separate so timed Query has no instrumentation. Counts accumulate;
            /// CellsVisited/CellsPruned mean nodes for this backend. False means visitor early stop.
            /// </summary>
            public bool QueryMeasured<TVisitor>(float2 center, float radius, ref TVisitor visitor,
                ref GridQueryStats stats) where TVisitor : struct, IGridVisitor
            {
                int nodeIndex = m_Stats[StatNodes] == 0 ? -1 : 0;
                while (nodeIndex >= 0)
                {
                    Node node = m_Nodes[nodeIndex];
                    stats.CellsVisited++;
                    if (!MayOverlap(node, center, radius))
                    {
                        stats.CellsPruned++;
                        nodeIndex = node.Next;
                        continue;
                    }
                    if (node.FirstChild >= 0)
                    {
                        nodeIndex = node.FirstChild;
                        continue;
                    }
                    int end = node.Start + node.Count;
                    for (int i = node.Start; i < end; i++)
                    {
                        GridEntry entry = m_Entries[i];
                        stats.EntriesExamined++;
                        float reach = radius + entry.Radius;
                        if (math.distancesq(center, entry.Position) < reach * reach)
                        {
                            stats.VisitorCalls++;
                            if (!visitor.Visit(entry)) return false;
                        }
                    }
                    nodeIndex = node.Next;
                }
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool MayOverlap(Node node, float2 center, float radius)
            {
                // The bounds contain centers, so expand by this subtree's maximum entry radius.
                // Inclusive broadphase preserves strict overlap at floating-point boundaries.
                float reach = radius + node.MaxRadius;
                return math.distancesq(center, math.clamp(center, node.Min, node.Max)) <= reach * reach;
            }
        }
    }
}
