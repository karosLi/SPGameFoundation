using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation
{
    /// <summary>
    /// GPU-resident disc pool for large, mostly static sets (food). The CPU uploads only changed rows
    /// (<see cref="StageRow"/>); every frame the GPU culls the pool against the view, appending visible
    /// discs and their count straight into the indirect arguments — no CPU readback, no per-frame
    /// full upload. GPU-driven tier only; the data-texture tier culls on the CPU into a CircleBatch.
    /// </summary>
    public sealed class PointCloudRenderer : IDisposable
    {
        readonly ComputeShader m_Compute;
        readonly int m_ScatterKernel;
        readonly int m_CullKernel;
        readonly Material m_Material;
        readonly Mesh m_Disc;
        readonly GraphicsBuffer m_Pool;
        readonly GraphicsBuffer m_Visible;
        readonly GraphicsBuffer m_Deltas;
        readonly GraphicsBuffer m_Args;
        NativeArray<RowDelta> m_Staged;
        NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs> m_ArgsReset;
        int m_StagedCount;

        public PointCloudRenderer(RenderAssets assets, BlendKind blend, int capacity, int maxDeltasPerFrame)
        {
            if (assets.Tier != RenderTier.GpuDriven)
                throw new InvalidOperationException("PointCloudRenderer requires the GPU-driven tier.");
            Capacity = capacity;
            m_Compute = assets.PointCloud;
            m_ScatterKernel = m_Compute != null ? m_Compute.FindKernel("ScatterRows") : -1;
            m_CullKernel = m_Compute != null ? m_Compute.FindKernel("Cull") : -1;
            m_Material = RenderAssets.CreateMaterial(assets.InstancedShader, blend);
            m_Disc = DiscMesh.CreateSingle();
            m_Pool = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, InstanceData.Stride);
            m_Visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, InstanceData.Stride);
            m_Deltas = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxDeltasPerFrame, RowDelta.Stride);
            // Raw (byte address) + indirect: the compute kernel bumps instanceCount with an atomic.
            m_Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            m_Staged = new NativeArray<RowDelta>(maxDeltasPerFrame, Allocator.Persistent);
            m_ArgsReset = new NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs>(1, Allocator.Persistent);
            m_ArgsReset[0] = new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = DiscMesh.IndicesPerDisc };
            m_Material?.SetBuffer(RenderAssets.Ids.Instances, m_Visible);
        }

        public int Capacity { get; }
        public int MaxDeltas => m_Staged.Length;

        /// <summary>Rows used by the pool (rows ≥ count are ignored by the cull).</summary>
        public int PoolCount { get; set; }

        /// <summary>True when more rows changed than fit in one frame; the caller should re-stage everything over time or next frame.</summary>
        public bool StagingFull => m_StagedCount >= m_Staged.Length;

        public bool StageRow(int row, in InstanceData data)
        {
            if (m_StagedCount >= m_Staged.Length) return false;
            m_Staged[m_StagedCount++] = new RowDelta { Row = (uint)row, Data = data };
            return true;
        }

        public void Draw(float4 viewRect, Bounds bounds, int layer = 0)
        {
            if (m_Compute == null || m_Material == null)
                return;

            if (m_StagedCount > 0)
            {
                m_Deltas.SetData(m_Staged, 0, 0, m_StagedCount);
                m_Compute.SetBuffer(m_ScatterKernel, RenderAssets.Ids.Deltas, m_Deltas);
                m_Compute.SetBuffer(m_ScatterKernel, RenderAssets.Ids.Pool, m_Pool);
                m_Compute.SetInt(RenderAssets.Ids.DeltaCount, m_StagedCount);
                m_Compute.Dispatch(m_ScatterKernel, (m_StagedCount + 63) / 64, 1, 1);
                m_StagedCount = 0;
            }

            m_Args.SetData(m_ArgsReset);
            int count = math.min(PoolCount, Capacity);
            if (count > 0)
            {
                m_Compute.SetBuffer(m_CullKernel, RenderAssets.Ids.PoolIn, m_Pool);
                m_Compute.SetBuffer(m_CullKernel, RenderAssets.Ids.Visible, m_Visible);
                m_Compute.SetBuffer(m_CullKernel, RenderAssets.Ids.Args, m_Args);
                m_Compute.SetInt(RenderAssets.Ids.PoolCount, count);
                m_Compute.SetVector(RenderAssets.Ids.ViewRect, viewRect);
                m_Compute.Dispatch(m_CullKernel, (count + 63) / 64, 1, 1);
            }
            Graphics.RenderMeshIndirect(new RenderParams(m_Material) { worldBounds = bounds, layer = layer }, m_Disc, m_Args);
        }

        public void Dispose()
        {
            if (m_Staged.IsCreated) m_Staged.Dispose();
            if (m_ArgsReset.IsCreated) m_ArgsReset.Dispose();
            m_Pool.Dispose();
            m_Visible.Dispose();
            m_Deltas.Dispose();
            m_Args.Dispose();
            if (m_Disc != null) UnityEngine.Object.Destroy(m_Disc);
            if (m_Material != null) UnityEngine.Object.Destroy(m_Material);
        }
    }
}
