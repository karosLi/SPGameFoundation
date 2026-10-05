using System;
using SPF.L1.Body;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Presentation
{
    /// <summary>One chain (snake body) to draw this frame.</summary>
    public struct ChainDesc
    {
        /// <summary>Stable slot for the GPU trail cache while the chain lives (e.g. table row).</summary>
        public int Key;
        /// <summary>Unique chain id; a key reused by another chain forces a full trail upload.</summary>
        public int Identity;
        public TrailState Trail;
        public float2 HeadPrev;
        public float2 HeadCurr;
        public float ArcPrev;
        public float ArcCurr;
        public float Spacing;
        public float Radius;
        public float NodeSpacing;
        public float NodeStride;
        public float Depth;
        public float4 ColorA;
        public float4 ColorB;
        public int Stripe;
        public BlendKind Blend;
        public ChainShape Shape;
    }

    /// <summary>
    /// Draws chains as overlapping discs ("nodes") or as continuous strips, opaque / translucent /
    /// additive. Chains must be added in draw order (translucent: back to front).
    /// GPU-driven tier: trail points live on the GPU (only newly pushed points are uploaded), a compute
    /// kernel expands and interpolates every node, strips are generated in the vertex shader.
    /// Data-texture tier: Burst jobs do the same expansion into disc pages and strip meshes.
    /// </summary>
    public sealed class ChainRenderer : IDisposable
    {
        const int Categories = 6;   // shape * 3 + blend

        readonly RenderTier m_Tier;
        readonly int m_MaxChains;
        readonly int m_MaxNodes;
        readonly NativeArray<ChainHeader>[] m_Headers = new NativeArray<ChainHeader>[Categories];
        readonly int[] m_HeaderCounts = new int[Categories];
        readonly int[] m_Totals = new int[Categories];
        NativeArray<float2> m_Points;

        // GPU-driven
        readonly ComputeShader m_Compute;
        readonly int m_ExpandKernel, m_CompactKernel, m_ScatterKernel;
        readonly GraphicsBuffer m_CompactArgs;   // raw + indirect: the compact kernel bumps instanceCount
        readonly GraphicsBuffer[] m_HeaderBuffers = new GraphicsBuffer[Categories];
        readonly GraphicsBuffer[] m_NodeBuffers = new GraphicsBuffer[3];
        readonly GraphicsBuffer[] m_NodeArgs = new GraphicsBuffer[3];
        readonly Material[] m_NodeMaterials = new Material[3];
        readonly Material[] m_StripMaterials = new Material[3];
        readonly MaterialPropertyBlock[] m_StripProps = new MaterialPropertyBlock[3];
        readonly GraphicsBuffer m_TrailMirror;
        readonly GraphicsBuffer m_TrailDeltaBuffer;
        NativeArray<TrailDelta> m_TrailDeltas;
        NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs> m_ArgsData;
        int m_TrailDeltaCount;
        Mesh m_Disc;
        int m_Segments = DiscMesh.Segments;
        bool m_HeadersDirty = true;
        readonly int[] m_CacheIdentity;
        readonly uint[] m_CacheVersion;
        readonly int[] m_CacheStart;
        readonly uint[] m_CachePushed;

        // Data texture
        readonly CircleBatch[] m_NodeBatches = new CircleBatch[3];
        readonly Mesh[] m_StripMeshes = new Mesh[3];

        public ChainRenderer(RenderAssets assets, int maxChains, int maxNodes, int trailPoolSize, int maxKeys, int maxTrailDeltasPerFrame = 65536)
        {
            m_Tier = assets.Tier;
            m_MaxChains = maxChains;
            m_MaxNodes = maxNodes;
            for (int c = 0; c < Categories; c++)
                m_Headers[c] = new NativeArray<ChainHeader>(maxChains, Allocator.Persistent);

            for (int b = 0; b < 3; b++)
            {
                m_StripMaterials[b] = RenderAssets.CreateMaterial(assets.StripShader, (BlendKind)b, true, 1);
                m_StripProps[b] = new MaterialPropertyBlock();
            }

            if (m_Tier == RenderTier.GpuDriven)
            {
                m_Compute = assets.NodeExpand;
                if (m_Compute != null)
                {
                    m_ExpandKernel = m_Compute.FindKernel("ExpandNodes");
                    m_CompactKernel = m_Compute.FindKernel("ExpandNodesCompact");
                    m_ScatterKernel = m_Compute.FindKernel("ScatterTrail");
                    try
                    {
                        m_CompactArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    }
                    catch (Exception)
                    {
                        m_CompactArgs = null;   // API without raw indirect args: keep the uncompacted path
                    }
                }
                m_Disc = DiscMesh.CreateSingle();
                m_ArgsData = new NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs>(1, Allocator.Persistent);
                for (int c = 0; c < Categories; c++)
                    m_HeaderBuffers[c] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxChains, ChainHeader.Stride);
                for (int b = 0; b < 3; b++)
                {
                    m_NodeBuffers[b] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxNodes, InstanceData.Stride);
                    m_NodeArgs[b] = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    m_NodeMaterials[b] = RenderAssets.CreateMaterial(assets.InstancedShader, (BlendKind)b);
                    m_NodeMaterials[b]?.SetBuffer(RenderAssets.Ids.Instances, m_NodeBuffers[b]);
                }
                m_TrailMirror = new GraphicsBuffer(GraphicsBuffer.Target.Structured, trailPoolSize, 8);
                m_TrailDeltaBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxTrailDeltasPerFrame, TrailDelta.Stride);
                m_TrailDeltas = new NativeArray<TrailDelta>(maxTrailDeltasPerFrame, Allocator.Persistent);
                m_CacheIdentity = new int[maxKeys];
                m_CacheVersion = new uint[maxKeys];
                m_CacheStart = new int[maxKeys];
                m_CachePushed = new uint[maxKeys];
                Invalidate();
            }
            else
            {
                for (int b = 0; b < 3; b++)
                {
                    m_NodeBatches[b] = new CircleBatch(assets, (BlendKind)b, maxNodes);
                    m_StripMeshes[b] = new Mesh { name = "SPF Strips", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                    m_StripMeshes[b].MarkDynamic();
                }
            }
        }

        public RenderTier Tier => m_Tier;

        /// <summary>
        /// GPU-driven tier: append only on-screen nodes of opaque chains (order-free thanks to per-node
        /// depth) instead of drawing off-screen nodes as zero-radius discs. Translucent / additive chains
        /// always keep node order. On by default; exposed for A/B pixel tests.
        /// </summary>
        public bool CompactOpaqueNodes { get; set; } = true;

        public bool CompactionAvailable => m_CompactArgs != null;

        /// <summary>Polygon segments per node disc (adaptive quality lowers it); meshes are rebuilt on change.</summary>
        public int DiscSegments
        {
            get => m_Segments;
            set
            {
                value = math.clamp(value, 6, 32);
                if (value == m_Segments) return;
                m_Segments = value;
                if (m_Disc != null)
                {
                    UnityEngine.Object.Destroy(m_Disc);
                    m_Disc = DiscMesh.CreateSingle(value);
                }
                foreach (var batch in m_NodeBatches)
                    if (batch != null) batch.DiscSegments = value;
            }
        }
        public int NodeCount(BlendKind blend) => m_Totals[(int)blend];
        public int StripSegments(BlendKind blend) => m_Totals[3 + (int)blend];

        /// <summary>Forgets the GPU trail cache (e.g. after a world reset); every chain is re-uploaded.</summary>
        public void Invalidate()
        {
            if (m_CacheIdentity != null)
                for (int i = 0; i < m_CacheIdentity.Length; i++) m_CacheIdentity[i] = -1;
        }

        /// <summary>
        /// Starts a new set of chains. Between Begin/Add rounds, <see cref="Draw"/> may be called any number
        /// of times (e.g. every frame with a new interpolation alpha); headers are uploaded only once.
        /// </summary>
        public void Begin(NativeArray<float2> trailPoints)
        {
            m_Points = trailPoints;
            m_HeadersDirty = true;
            Array.Clear(m_HeaderCounts, 0, Categories);
            Array.Clear(m_Totals, 0, Categories);
        }

        public bool Add(in ChainDesc chain)
        {
            int category = (int)chain.Shape * 3 + (int)chain.Blend;
            int index = m_HeaderCounts[category];
            if (index >= m_MaxChains || !chain.Trail.IsAllocated || chain.Trail.Count == 0)
                return false;

            float bodyLength = math.max(chain.ArcCurr, 0f) + (chain.Trail.Count - 1) * chain.Spacing;
            int nodes = ChainMath.NodeCount(bodyLength, chain.NodeSpacing, math.max(chain.NodeStride, 1f));
            int items = chain.Shape == ChainShape.Nodes ? nodes : math.max(nodes - 1, 0);
            int budget = chain.Shape == ChainShape.Nodes ? m_MaxNodes : m_MaxNodes / 2;
            if (m_Totals[category] + items > budget)
                return false;

            if (m_Tier == RenderTier.GpuDriven && !UploadTrail(chain))
                return false;

            var trail = chain.Trail;
            uint flags = chain.Blend == BlendKind.Translucent ? ChainHeader.FlagTranslucent
                : chain.Blend == BlendKind.Additive ? ChainHeader.FlagTranslucent | ChainHeader.FlagAdditive : 0u;
            m_Headers[category][index] = new ChainHeader
            {
                HeadPrev = chain.HeadPrev,
                HeadCurr = chain.HeadCurr,
                ArcPrev = chain.ArcPrev,
                ArcCurr = chain.ArcCurr,
                TrailStart = (uint)trail.Start,
                TrailMask = (uint)(trail.Capacity - 1),
                Newest = trail.Pushed - 1,
                Count = (uint)trail.Count,
                Spacing = chain.Spacing,
                NodeSpacing = chain.NodeSpacing,
                NodeOffset = (uint)m_Totals[category],
                NodeCount = (uint)nodes,
                Radius = chain.Radius,
                Depth = chain.Depth,
                ColorA = chain.ColorA,
                ColorB = chain.ColorB,
                Stripe = (uint)math.max(chain.Stripe, 0),
                Flags = flags,
                NodeStride = math.max(chain.NodeStride, 1f),
            };
            m_HeaderCounts[category] = index + 1;
            m_Totals[category] += items;
            return true;
        }

        bool UploadTrail(in ChainDesc chain)
        {
            var trail = chain.Trail;
            int key = chain.Key;
            if ((uint)key >= (uint)m_CacheIdentity.Length)
                return false;

            bool full = m_CacheIdentity[key] != chain.Identity || m_CacheVersion[key] != trail.Version || m_CacheStart[key] != trail.Start;
            uint fresh = full ? (uint)trail.Count : math.min(trail.Pushed - m_CachePushed[key], (uint)trail.Count);
            if (m_TrailDeltaCount + fresh > m_TrailDeltas.Length)
            {
                m_CacheIdentity[key] = -1;   // retry next frame
                return false;
            }
            for (uint i = 0; i < fresh; i++)
            {
                uint g = trail.Pushed - 1 - i;
                int slot = trail.Slot(g);
                m_TrailDeltas[m_TrailDeltaCount++] = new TrailDelta { Slot = (uint)slot, Value = m_Points[slot] };
            }
            m_CacheIdentity[key] = chain.Identity;
            m_CacheVersion[key] = trail.Version;
            m_CacheStart[key] = trail.Start;
            m_CachePushed[key] = trail.Pushed;
            return true;
        }

        public void Draw(float alpha, float4 viewRect, Bounds bounds, int layer = 0)
        {
            if (m_Tier == RenderTier.GpuDriven) DrawGpu(alpha, viewRect, bounds, layer);
            else DrawCpu(alpha, viewRect, bounds, layer);
        }

        void DrawGpu(float alpha, float4 viewRect, Bounds bounds, int layer)
        {
            if (m_Compute == null)
                return;

            if (m_TrailDeltaCount > 0)
            {
                m_TrailDeltaBuffer.SetData(m_TrailDeltas, 0, 0, m_TrailDeltaCount);
                m_Compute.SetBuffer(m_ScatterKernel, RenderAssets.Ids.TrailDeltas, m_TrailDeltaBuffer);
                m_Compute.SetBuffer(m_ScatterKernel, RenderAssets.Ids.TrailOut, m_TrailMirror);
                m_Compute.SetInt(RenderAssets.Ids.TrailDeltaCount, m_TrailDeltaCount);
                m_Compute.Dispatch(m_ScatterKernel, (m_TrailDeltaCount + 63) / 64, 1, 1);
                m_TrailDeltaCount = 0;
            }

            bool uploadHeaders = m_HeadersDirty;
            m_HeadersDirty = false;
            for (int c = 0; c < Categories; c++)
            {
                int headers = m_HeaderCounts[c];
                int total = m_Totals[c];
                if (headers == 0 || total == 0) continue;
                if (uploadHeaders)
                    m_HeaderBuffers[c].SetData(m_Headers[c], 0, 0, headers);
                int blend = c % 3;

                if (c < 3)
                {
                    bool compact = blend == (int)BlendKind.Opaque && CompactOpaqueNodes && m_CompactArgs != null;
                    int kernel = compact ? m_CompactKernel : m_ExpandKernel;
                    m_Compute.SetBuffer(kernel, RenderAssets.Ids.Headers, m_HeaderBuffers[c]);
                    m_Compute.SetBuffer(kernel, RenderAssets.Ids.Trail, m_TrailMirror);
                    m_Compute.SetBuffer(kernel, RenderAssets.Ids.Nodes, m_NodeBuffers[blend]);
                    m_Compute.SetInt(RenderAssets.Ids.HeaderCount, headers);
                    m_Compute.SetInt(RenderAssets.Ids.NodeTotal, total);
                    m_Compute.SetFloat(RenderAssets.Ids.Alpha, alpha);
                    m_Compute.SetVector(RenderAssets.Ids.ViewRect, viewRect);

                    // Compact: the kernel counts visible nodes into instanceCount (reset to 0 first).
                    m_ArgsData[0] = new GraphicsBuffer.IndirectDrawIndexedArgs
                    {
                        indexCountPerInstance = (uint)DiscMesh.IndicesFor(m_Segments),
                        instanceCount = compact ? 0u : (uint)total,
                    };
                    var args = compact ? m_CompactArgs : m_NodeArgs[blend];
                    args.SetData(m_ArgsData);
                    if (compact)
                        m_Compute.SetBuffer(kernel, RenderAssets.Ids.Args, m_CompactArgs);
                    m_Compute.Dispatch(kernel, (total + 63) / 64, 1, 1);

                    if (m_NodeMaterials[blend] != null)
                        Graphics.RenderMeshIndirect(new RenderParams(m_NodeMaterials[blend]) { worldBounds = bounds, layer = layer }, m_Disc, args);
                }
                else if (m_StripMaterials[blend] != null)
                {
                    var props = m_StripProps[blend];
                    props.SetBuffer(RenderAssets.Ids.Headers, m_HeaderBuffers[c]);
                    props.SetBuffer(RenderAssets.Ids.Trail, m_TrailMirror);
                    props.SetInteger(RenderAssets.Ids.HeaderCount, headers);
                    props.SetFloat(RenderAssets.Ids.Alpha, alpha);
                    Graphics.RenderPrimitives(new RenderParams(m_StripMaterials[blend]) { worldBounds = bounds, layer = layer, matProps = props },
                        MeshTopology.Triangles, total * 6);
                }
            }
        }

        void DrawCpu(float alpha, float4 viewRect, Bounds bounds, int layer)
        {
            JobHandle handle = default;
            for (int b = 0; b < 3; b++)
            {
                int headers = m_HeaderCounts[b];
                m_NodeBatches[b].Count = m_Totals[b];
                if (headers == 0) continue;
                handle = JobHandle.CombineDependencies(handle, new ExpandNodesJob
                {
                    Headers = m_Headers[b],
                    Points = m_Points,
                    Output = m_NodeBatches[b].Instances,
                    Alpha = alpha,
                    ViewRect = viewRect,
                }.Schedule(headers, 4));
            }

            var meshData = Mesh.AllocateWritableMeshData(3);
            for (int b = 0; b < 3; b++)
            {
                int c = 3 + b;
                int segments = m_HeaderCounts[c] > 0 ? m_Totals[c] : 0;
                var data = meshData[b];
                data.SetVertexBufferParams(segments * 4,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm8, 4));
                data.SetIndexBufferParams(segments * 6, IndexFormat.UInt32);
                if (segments > 0)
                {
                    // Chained, not parallel: the vertex / index arrays share the MeshDataArray's safety handle.
                    handle = new BuildStripsJob
                    {
                        Headers = m_Headers[c],
                        Points = m_Points,
                        Vertices = data.GetVertexData<StripVertex>(),
                        Indices = data.GetIndexData<uint>(),
                        Alpha = alpha,
                    }.Schedule(m_HeaderCounts[c], 4, handle);
                }
            }
            handle.Complete();

            for (int b = 0; b < 3; b++)
            {
                int segments = m_HeaderCounts[3 + b] > 0 ? m_Totals[3 + b] : 0;
                var data = meshData[b];
                data.subMeshCount = 1;
                data.SetSubMesh(0, new SubMeshDescriptor(0, segments * 6), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
            Mesh.ApplyAndDisposeWritableMeshData(meshData, m_StripMeshes,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);

            for (int b = 0; b < 3; b++)
            {
                m_NodeBatches[b].Draw(bounds, layer);
                if (m_Totals[3 + b] > 0 && m_StripMaterials[b] != null)
                {
                    m_StripMeshes[b].bounds = bounds;
                    Graphics.RenderMesh(new RenderParams(m_StripMaterials[b]) { worldBounds = bounds, layer = layer }, m_StripMeshes[b], 0, Matrix4x4.identity);
                }
            }
        }

        public void Dispose()
        {
            foreach (var h in m_Headers) if (h.IsCreated) h.Dispose();
            if (m_TrailDeltas.IsCreated) m_TrailDeltas.Dispose();
            if (m_ArgsData.IsCreated) m_ArgsData.Dispose();
            foreach (var b in m_HeaderBuffers) b?.Dispose();
            foreach (var b in m_NodeBuffers) b?.Dispose();
            foreach (var b in m_NodeArgs) b?.Dispose();
            m_CompactArgs?.Dispose();
            m_TrailMirror?.Dispose();
            m_TrailDeltaBuffer?.Dispose();
            foreach (var m in m_NodeMaterials) if (m != null) UnityEngine.Object.Destroy(m);
            foreach (var m in m_StripMaterials) if (m != null) UnityEngine.Object.Destroy(m);
            foreach (var m in m_StripMeshes) if (m != null) UnityEngine.Object.Destroy(m);
            foreach (var b in m_NodeBatches) b?.Dispose();
            if (m_Disc != null) UnityEngine.Object.Destroy(m_Disc);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        internal struct StripVertex
        {
            public float3 Position;
            public uint ColorA;
            public float3 Uv;
            public uint ColorB;
        }

        static uint Pack(float4 c)
        {
            uint4 b = (uint4)math.round(math.saturate(c) * 255f);
            return b.x | (b.y << 8) | (b.z << 16) | (b.w << 24);
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        struct ExpandNodesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ChainHeader> Headers;
            [ReadOnly] public NativeArray<float2> Points;
            [NativeDisableParallelForRestriction] public NativeArray<InstanceData> Output;
            public float Alpha;
            public float4 ViewRect;

            public void Execute(int c)
            {
                var h = Headers[c];
                var trail = ChainMath.ToTrail(h, Points);
                bool translucent = (h.Flags & ChainHeader.FlagTranslucent) != 0;
                for (int j = 0; j < (int)h.NodeCount; j++)
                {
                    float2 p = ChainMath.Sample(h, trail, Points, Alpha, j * h.NodeSpacing * h.NodeStride);
                    float t = h.NodeCount > 1 ? (float)j / (h.NodeCount - 1) : 0f;
                    float radius = h.Radius * ChainMath.Taper(t) * (j == 0 ? 1.12f : 1f) * (h.NodeStride > 1f && j > 0 ? 1.1f : 1f);
                    bool visible = p.x + radius >= ViewRect.x && p.y + radius >= ViewRect.y && p.x - radius <= ViewRect.z && p.y - radius <= ViewRect.w;
                    bool stripe = h.Stripe > 0 && ((j / (int)h.Stripe) & 1) == 1;
                    Output[(int)h.NodeOffset + j] = new InstanceData(p, visible ? radius : 0f,
                        translucent ? h.Depth : h.Depth + math.min(j * 1e-4f, 0.15f), stripe ? h.ColorB : h.ColorA);
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        internal struct BuildStripsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ChainHeader> Headers;
            [ReadOnly] public NativeArray<float2> Points;
            [NativeDisableParallelForRestriction] public NativeArray<StripVertex> Vertices;
            [NativeDisableParallelForRestriction] public NativeArray<uint> Indices;
            public float Alpha;

            public void Execute(int c)
            {
                var h = Headers[c];
                var trail = ChainMath.ToTrail(h, Points);
                bool translucent = (h.Flags & ChainHeader.FlagTranslucent) != 0;
                float step = h.NodeSpacing * h.NodeStride;
                int segments = math.max((int)h.NodeCount - 1, 0);
                uint colorA = Pack(h.ColorA), colorB = Pack(h.ColorB);
                float period = h.Stripe > 0 ? h.Stripe * step : 1e6f;
                if (segments == 0) return;

                // Rolling window over the sampled points: each point is sampled once and its vertex pair
                // is shared by the two segments that meet there (was 3 samples x 2 endpoints per segment).
                float2 pPrev = ChainMath.Sample(h, trail, Points, Alpha, 0f);
                float2 pCur = pPrev;
                float2 pNext = ChainMath.Sample(h, trail, Points, Alpha, step);
                for (int pt = 0; pt <= segments; pt++)
                {
                    float s = pt * step;
                    float2 tangent = math.normalizesafe(pNext - pPrev, new float2(1f, 0f));
                    float2 normal = new float2(-tangent.y, tangent.x);
                    float radius = h.Radius * ChainMath.Taper(h.NodeCount > 1 ? (float)pt / h.NodeCount : 0f);
                    float depth = translucent ? h.Depth : h.Depth + math.min(pt * 1e-4f, 0.15f);
                    var left = new StripVertex { Position = new float3(pCur - normal * radius, depth), ColorA = colorA, ColorB = colorB, Uv = new float3(s, -1f, period) };
                    var right = new StripVertex { Position = new float3(pCur + normal * radius, depth), ColorA = colorA, ColorB = colorB, Uv = new float3(s, 1f, period) };

                    if (pt < segments)
                    {
                        // Start of segment pt.
                        int v = ((int)h.NodeOffset + pt) * 4;
                        int i = ((int)h.NodeOffset + pt) * 6;
                        Vertices[v] = left;
                        Vertices[v + 1] = right;
                        Indices[i] = (uint)v;
                        Indices[i + 1] = (uint)(v + 1);
                        Indices[i + 2] = (uint)(v + 2);
                        Indices[i + 3] = (uint)(v + 2);
                        Indices[i + 4] = (uint)(v + 1);
                        Indices[i + 5] = (uint)(v + 3);
                    }
                    if (pt > 0)
                    {
                        // End of segment pt - 1.
                        int v = ((int)h.NodeOffset + pt - 1) * 4;
                        Vertices[v + 2] = left;
                        Vertices[v + 3] = right;
                    }

                    pPrev = pCur;
                    pCur = pNext;
                    if (pt < segments)
                        pNext = ChainMath.Sample(h, trail, Points, Alpha, (pt + 2) * step);
                }
            }
        }
    }
}
