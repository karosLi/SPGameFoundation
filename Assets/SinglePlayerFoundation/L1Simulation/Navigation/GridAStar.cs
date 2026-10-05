using System;
using SPF.L1.Spatial;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.L1.Navigation
{
    /// <summary>
    /// Reusable A* working memory for one map size. Nodes carry a search stamp instead of being cleared, so
    /// a search costs only what it expands (no O(map) reset), which keeps many small searches per tick cheap
    /// and cache friendly. One scratch per thread (jobs allocate their own with Allocator.Temp).
    /// </summary>
    public struct AStarScratch : IDisposable
    {
        public NativeArray<float> G;
        public NativeArray<float> F;
        public NativeArray<int> Parent;
        public NativeArray<int> HeapIndex;   // position in the open heap, -1 = closed
        public NativeArray<uint> Stamp;      // search id that last touched the node
        public NativeArray<int> Heap;
        public uint Search;

        public AStarScratch(int cells, Allocator allocator)
        {
            G = new NativeArray<float>(cells, allocator, NativeArrayOptions.UninitializedMemory);
            F = new NativeArray<float>(cells, allocator, NativeArrayOptions.UninitializedMemory);
            Parent = new NativeArray<int>(cells, allocator, NativeArrayOptions.UninitializedMemory);
            HeapIndex = new NativeArray<int>(cells, allocator, NativeArrayOptions.UninitializedMemory);
            Stamp = new NativeArray<uint>(cells, allocator);
            Heap = new NativeArray<int>(cells, allocator, NativeArrayOptions.UninitializedMemory);
            Search = 0;
        }

        public bool IsCreated => G.IsCreated;

        public void Dispose()
        {
            if (G.IsCreated) G.Dispose();
            if (F.IsCreated) F.Dispose();
            if (Parent.IsCreated) Parent.Dispose();
            if (HeapIndex.IsCreated) HeapIndex.Dispose();
            if (Stamp.IsCreated) Stamp.Dispose();
            if (Heap.IsCreated) Heap.Dispose();
        }
    }

    /// <summary>
    /// Grid A* on a <see cref="TileMapView"/> (non-solid tiles walkable, optional extra blocked mask), 4- or
    /// 8-connected without cutting corners, octile heuristic (optimal paths). Burst-compatible and
    /// allocation-free with an <see cref="AStarScratch"/>. Use it for individual agents (RTS units, NPC
    /// errands), reachability checks (tower placement) and path previews; use <see cref="FlowField"/> when
    /// many agents share one goal.
    /// </summary>
    public static class GridAStar
    {
        const float Diagonal = 1.41421356f;

        /// <summary>
        /// Finds a path from <paramref name="start"/> to <paramref name="goal"/> (cells). Writes the cells
        /// start..goal into <paramref name="path"/> and returns their count, or 0 when unreachable, when the
        /// path does not fit, or when more than <paramref name="maxExpanded"/> nodes were expanded.
        /// </summary>
        public static int FindPath(in TileMapView map, int2 start, int2 goal, NativeArray<int2> path, ref AStarScratch scratch,
            bool diagonal = true, int maxExpanded = int.MaxValue, NativeArray<byte> blocked = default)
        {
            if (!Walkable(map, start, blocked) || !Walkable(map, goal, blocked)) return 0;
            int width = map.Size.x;
            uint search = ++scratch.Search;
            if (search == 0)
            {
                // Stamp wrap-around (after 4 billion searches): clear once.
                for (int i = 0; i < scratch.Stamp.Length; i++) scratch.Stamp[i] = 0;
                search = scratch.Search = 1;
            }
            int heapCount = 0;
            int s = start.y * width + start.x, g = goal.y * width + goal.x;
            Touch(ref scratch, s, search);
            scratch.G[s] = 0f;
            scratch.F[s] = Heuristic(start, goal, diagonal);
            scratch.Parent[s] = -1;
            Push(ref scratch, ref heapCount, s);
            int expanded = 0;
            while (heapCount > 0)
            {
                int current = Pop(ref scratch, ref heapCount);
                if (current == g) return Reconstruct(ref scratch, g, width, path);
                if (++expanded > maxExpanded) return 0;
                int2 c = new int2(current % width, current / width);
                int neighbours = diagonal ? 8 : 4;
                for (int k = 0; k < neighbours; k++)
                {
                    int2 d = k switch { 0 => new int2(1, 0), 1 => new int2(-1, 0), 2 => new int2(0, 1), 3 => new int2(0, -1), 4 => new int2(1, 1), 5 => new int2(-1, 1), 6 => new int2(1, -1), _ => new int2(-1, -1) };
                    int2 n = c + d;
                    if (!Walkable(map, n, blocked)) continue;
                    // No corner cutting: both orthogonal neighbours must be open for a diagonal step.
                    if (k >= 4 && (!Walkable(map, new int2(c.x + d.x, c.y), blocked) || !Walkable(map, new int2(c.x, c.y + d.y), blocked))) continue;
                    int ni = n.y * width + n.x;
                    float cost = scratch.G[current] + (k >= 4 ? Diagonal : 1f);
                    bool fresh = scratch.Stamp[ni] != search;
                    if (fresh) Touch(ref scratch, ni, search);
                    else if (scratch.HeapIndex[ni] < 0 || cost >= scratch.G[ni]) continue;   // closed, or no better
                    scratch.G[ni] = cost;
                    scratch.F[ni] = cost + Heuristic(n, goal, diagonal);
                    scratch.Parent[ni] = current;
                    if (fresh) Push(ref scratch, ref heapCount, ni);
                    else SiftUp(ref scratch, scratch.HeapIndex[ni]);
                }
            }
            return 0;
        }

        /// <summary>True when a path exists (reachability, e.g. "does this tower still leave a way through?").</summary>
        public static bool Reachable(in TileMapView map, int2 start, int2 goal, ref AStarScratch scratch, NativeArray<byte> blocked = default)
        {
            var one = new NativeArray<int2>(map.Size.x * map.Size.y, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            bool found = FindPath(map, start, goal, one, ref scratch, false, int.MaxValue, blocked) > 0;
            one.Dispose();
            return found;
        }

        /// <summary>
        /// String pulling: removes waypoints that have a clear line to a later one (tile line of sight).
        /// Keeps the first and last cells; returns the new count (in place).
        /// </summary>
        public static int Smooth(in TileMapView map, NativeArray<int2> path, int count)
        {
            if (count <= 2) return count;
            int write = 1, anchor = 0;
            for (int i = 2; i < count; i++)
            {
                if (map.LineOfSight(map.CenterOf(path[anchor]), map.CenterOf(path[i]))) continue;
                path[write++] = path[i - 1];
                anchor = i - 1;
            }
            path[write++] = path[count - 1];
            return write;
        }

        static bool Walkable(in TileMapView map, int2 cell, NativeArray<byte> blocked) =>
            !map.IsSolid(cell) && (!blocked.IsCreated || blocked[map.Index(cell)] == 0);

        static float Heuristic(int2 a, int2 b, bool diagonal)
        {
            int2 d = math.abs(a - b);
            return diagonal ? math.cmax(d) + (Diagonal - 1f) * math.cmin(d) : d.x + d.y;
        }

        static void Touch(ref AStarScratch s, int i, uint search)
        {
            s.Stamp[i] = search;
            s.HeapIndex[i] = -1;
        }

        static int Reconstruct(ref AStarScratch s, int goal, int width, NativeArray<int2> path)
        {
            int count = 0;
            for (int i = goal; i >= 0; i = s.Parent[i]) count++;
            if (count > path.Length) return 0;
            int k = count;
            for (int i = goal; i >= 0; i = s.Parent[i]) path[--k] = new int2(i % width, i / width);
            return count;
        }

        // Binary min-heap on F (ties: lower G first is not needed for optimality; index order keeps it deterministic).
        static bool Less(ref AStarScratch s, int a, int b) => s.F[a] < s.F[b] || (s.F[a] == s.F[b] && a < b);

        static void Push(ref AStarScratch s, ref int count, int node)
        {
            s.Heap[count] = node;
            s.HeapIndex[node] = count;
            SiftUp(ref s, count++);
        }

        static int Pop(ref AStarScratch s, ref int count)
        {
            int top = s.Heap[0];
            s.HeapIndex[top] = -1;   // closed
            int last = s.Heap[--count];
            if (count > 0)
            {
                s.Heap[0] = last;
                s.HeapIndex[last] = 0;
                SiftDown(ref s, 0, count);
            }
            return top;
        }

        static void SiftUp(ref AStarScratch s, int i)
        {
            int node = s.Heap[i];
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                int p = s.Heap[parent];
                if (!Less(ref s, node, p)) break;
                s.Heap[i] = p;
                s.HeapIndex[p] = i;
                i = parent;
            }
            s.Heap[i] = node;
            s.HeapIndex[node] = i;
        }

        static void SiftDown(ref AStarScratch s, int i, int count)
        {
            int node = s.Heap[i];
            while (true)
            {
                int l = 2 * i + 1;
                if (l >= count) break;
                int r = l + 1;
                int child = r < count && Less(ref s, s.Heap[r], s.Heap[l]) ? r : l;
                if (!Less(ref s, s.Heap[child], node)) break;
                s.Heap[i] = s.Heap[child];
                s.HeapIndex[s.Heap[i]] = i;
                i = child;
            }
            s.Heap[i] = node;
            s.HeapIndex[node] = i;
        }
    }

    /// <summary>One path request of a batch (see <see cref="AStarBatchJob"/>).</summary>
    public struct PathRequest
    {
        public int2 Start, Goal;
    }

    /// <summary>
    /// Many A* searches in parallel (RTS orders, NPC errands): request i writes up to <see cref="MaxLength"/>
    /// cells into Paths[i * MaxLength ..] and its (smoothed) length into Lengths[i] (0 = no path). Scheduled
    /// over chunks of <see cref="ChunkSize"/> requests (<see cref="Schedule"/>), each with its own Temp scratch.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct AStarBatchJob : IJobParallelFor
    {
        public const int ChunkSize = 16;

        public TileMapView Map;
        [ReadOnly] public NativeArray<PathRequest> Requests;
        [NativeDisableParallelForRestriction] public NativeArray<int2> Paths;
        // Each chunk writes only its own requests' entries (disjoint ranges), not just its job index.
        [NativeDisableParallelForRestriction] public NativeArray<int> Lengths;
        public int MaxLength;
        public bool Diagonal, SmoothPaths;
        public int MaxExpanded;

        /// <summary>Schedules every request (one parallel index per chunk).</summary>
        public JobHandle Schedule(JobHandle dependency = default)
        {
            int chunks = (Requests.Length + ChunkSize - 1) / ChunkSize;
            return IJobParallelForExtensions.Schedule(this, chunks, 1, dependency);
        }

        public void Execute(int chunk)
        {
            int startIndex = chunk * ChunkSize, end = math.min(startIndex + ChunkSize, Requests.Length);
            var scratch = new AStarScratch(Map.Size.x * Map.Size.y, Allocator.Temp);
            for (int i = startIndex; i < end; i++)
            {
                var slice = Paths.GetSubArray(i * MaxLength, MaxLength);
                int n = GridAStar.FindPath(Map, Requests[i].Start, Requests[i].Goal, slice, ref scratch, Diagonal, MaxExpanded > 0 ? MaxExpanded : int.MaxValue);
                if (n > 0 && SmoothPaths) n = GridAStar.Smooth(Map, slice, n);
                Lengths[i] = n;
            }
            scratch.Dispose();
        }
    }
}
