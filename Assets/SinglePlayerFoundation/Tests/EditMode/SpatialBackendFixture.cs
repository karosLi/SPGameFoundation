using System;
using SPF.L1.Spatial;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public enum SpatialTestBackend { Grid, PrunedGrid, LayeredGrid, Quadtree }

    /// <summary>
    /// Test-only ownership/dispatch fixture over actual implementations. There is no new production
    /// interface or hot-loop delegate. It deliberately does not normalize different traversal orders,
    /// invent a shared early-exit return value, or hide per-build dropped-entry accounting.
    /// </summary>
    public sealed class SpatialBackendFixture : IDisposable
    {
        readonly SpatialGrid m_Grid;
        readonly BoundedQuadtreeReference m_Tree;
        public readonly SpatialTestBackend Backend;
        public SpatialBackendFixture(SpatialTestBackend backend, int2 dimensions, float cell, float2 origin,
            int capacity, int bucketSize = 16)
        {
            Backend = backend;
            if (backend == SpatialTestBackend.Quadtree)
                m_Tree = new BoundedQuadtreeReference(origin, (float2)dimensions * cell, capacity, bucketSize);
            else
                m_Grid = new SpatialGrid(dimensions, cell, capacity,
                    backend == SpatialTestBackend.LayeredGrid ? .75f : 0) { Origin = origin };
        }
        public int Capacity => m_Tree != null ? m_Tree.Capacity : m_Grid.Capacity;
        public int EntryCount => m_Tree != null ? m_Tree.EntryCount : m_Grid.EntryCount;
        public int Dropped => m_Tree != null ? m_Tree.RejectedCount : m_Grid.DroppedLastBuild;
        public void Set(int index, GridEntry entry)
        {
            if (m_Tree != null) { var staging = m_Tree.Staging; staging[index] = entry; }
            else { var staging = m_Grid.Staging; staging[index] = entry; }
        }
        public void Build(int requested)
        {
            if (m_Tree != null) m_Tree.Build(requested);
            else { var count = m_Grid.StagingCount; count[0] = requested; m_Grid.ScheduleBuild(default).Complete(); }
        }
        public void Query<T>(float2 center, float radius, ref T visitor) where T : struct, IGridVisitor
        {
            if (m_Tree != null) m_Tree.AsReader().Query(center, radius, ref visitor);
            else if (Backend == SpatialTestBackend.PrunedGrid) m_Grid.AsReader().QueryPruned(center, radius, ref visitor);
            else m_Grid.AsReader().Query(center, radius, ref visitor);
        }
        public void QueryMeasured<T>(float2 center, float radius, ref T visitor, ref GridQueryStats stats)
            where T : struct, IGridVisitor
        {
            if (m_Tree != null) m_Tree.AsReader().QueryMeasured(center, radius, ref visitor, ref stats);
            else m_Grid.AsReader().QueryMeasured(center, radius, ref visitor, ref stats, Backend == SpatialTestBackend.PrunedGrid);
        }
        public void Dispose() { m_Grid?.Dispose(); m_Tree?.Dispose(); }
    }
}
