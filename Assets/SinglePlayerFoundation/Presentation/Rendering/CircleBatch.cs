using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation
{
    /// <summary>
    /// A list of disc instances drawn with one material. Fill <see cref="Instances"/> up to
    /// <see cref="Capacity"/>, set <see cref="Count"/>, call <see cref="Draw"/> once per frame.
    /// GPU-driven tier: one indirect draw from a structured buffer. Data-texture tier: pages of 4096
    /// instances (RGBA32F texture + indexed mesh), one draw per used page.
    /// </summary>
    public sealed class CircleBatch : IDisposable
    {
        public const int PageSize = 4096;

        /// <summary>Data-texture tier: draw only a prefix submesh covering the used discs (A/B tests switch it off).</summary>
        public static bool UsePrefixSubmeshes = true;
        const int TextureWidth = 2048;

        readonly RenderTier m_Tier;
        readonly Material m_Material;
        NativeArray<InstanceData> m_Instances;

        // GPU-driven
        GraphicsBuffer m_Buffer;
        GraphicsBuffer m_Args;
        NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs> m_ArgsData;
        Mesh m_Disc;

        // Data texture
        Page[] m_Pages;
        int m_Segments = DiscMesh.Segments;

        public CircleBatch(RenderAssets assets, BlendKind blend, int capacity, bool shaded = true, int queueOffset = 0)
        {
            m_Tier = assets.Tier;
            Capacity = capacity;
            m_Material = RenderAssets.CreateMaterial(assets.InstancedShader, blend, shaded, queueOffset);
            m_Instances = new NativeArray<InstanceData>(capacity, Allocator.Persistent);
            if (m_Tier == RenderTier.GpuDriven)
            {
                m_Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, InstanceData.Stride);
                m_Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                m_ArgsData = new NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs>(1, Allocator.Persistent);
                m_Disc = DiscMesh.CreateSingle();
                m_Material?.SetBuffer(RenderAssets.Ids.Instances, m_Buffer);
            }
            else
            {
                m_Pages = new Page[(capacity + PageSize - 1) / PageSize];
            }
        }

        /// <summary>Polygon segments per disc (adaptive quality lowers it); meshes are rebuilt on change.</summary>
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
                if (m_Pages != null)
                    for (int p = 0; p < m_Pages.Length; p++)
                    {
                        m_Pages[p]?.Dispose();
                        m_Pages[p] = null;
                    }
            }
        }

        public int Capacity { get; }
        public int Count { get; set; }
        public NativeArray<InstanceData> Instances => m_Instances;
        public Material Material => m_Material;

        /// <summary>Appends one instance; returns false when full.</summary>
        public bool Add(float2 position, float radius, float depth, float4 color)
        {
            if (Count >= Capacity) return false;
            m_Instances[Count++] = new InstanceData(position, radius, depth, color);
            return true;
        }

        public void Draw(Bounds bounds, int layer = 0)
        {
            if (m_Material == null || Count <= 0)
                return;
            int count = math.min(Count, Capacity);
            var rp = new RenderParams(m_Material) { worldBounds = bounds, layer = layer };

            if (m_Tier == RenderTier.GpuDriven)
            {
                m_Buffer.SetData(m_Instances, 0, 0, count);
                m_ArgsData[0] = new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = (uint)DiscMesh.IndicesFor(m_Segments),
                    instanceCount = (uint)count,
                };
                m_Args.SetData(m_ArgsData);
                Graphics.RenderMeshIndirect(rp, m_Disc, m_Args);
                return;
            }

            for (int p = 0; p * PageSize < count; p++)
            {
                // Pages hold at most what the batch can ever need, so small batches (heads, eyes)
                // upload and vertex-process a few hundred discs instead of a full 4096-disc page.
                var page = m_Pages[p] ??= new Page(TextureWidth, math.min(PageSize, Capacity - p * PageSize), m_Segments);
                int n = math.min(PageSize, count - p * PageSize);
                page.Upload(m_Instances, p * PageSize, n);
                rp.matProps = page.Properties;
                // Smallest prefix submesh covering the used discs (unused ones are zero-radius anyway).
                Graphics.RenderMesh(rp, page.Mesh, UsePrefixSubmeshes ? DiscMesh.PrefixFor(n, page.Instances) : DiscMesh.PrefixCount(page.Instances) - 1, Matrix4x4.identity);
            }
        }

        public void Dispose()
        {
            if (m_Instances.IsCreated) m_Instances.Dispose();
            if (m_ArgsData.IsCreated) m_ArgsData.Dispose();
            m_Buffer?.Dispose();
            m_Args?.Dispose();
            if (m_Disc != null) UnityEngine.Object.Destroy(m_Disc);
            if (m_Material != null) UnityEngine.Object.Destroy(m_Material);
            if (m_Pages != null)
                foreach (var page in m_Pages) page?.Dispose();
        }

        /// <summary>Data-texture page: 2 RGBA32F texels per instance + a prebuilt indexed disc mesh.</summary>
        internal sealed class Page : IDisposable
        {
            readonly Texture2D m_Texture;
            int m_LastCount;

            public Page(int width, int instances, int segments)
            {
                // 2 texels per instance; narrow pages for small batches, height rounded up.
                width = math.min(width, math.ceilpow2(instances * 2));
                int height = (instances * 2 + width - 1) / width;
                m_Texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var texels = m_Texture.GetPixelData<float4>(0);
                for (int i = 0; i < texels.Length; i++) texels[i] = float4.zero;
                m_Texture.Apply(false, false);
                Instances = instances;
                Mesh = DiscMesh.CreateIndexed(instances, segments);
                Properties = new MaterialPropertyBlock();
                Properties.SetTexture(RenderAssets.Ids.DataTex, m_Texture);
            }

            public Mesh Mesh { get; }
            public int Instances { get; }
            public MaterialPropertyBlock Properties { get; }

            /// <summary>
            /// <see cref="InstanceData"/> is exactly two texels (x, y, radius, depth) + (color), so the
            /// upload is one native memcpy into the texture's CPU copy instead of a per-field loop.
            /// </summary>
            public void Upload(NativeArray<InstanceData> source, int start, int count)
            {
                var texels = m_Texture.GetPixelData<float4>(0);
                NativeArray<float4>.Copy(source.Reinterpret<float4>(InstanceData.Stride), start * 2, texels, 0, count * 2);
                // Unused slots collapse to zero-radius discs (the mesh stays static and non-readable).
                for (int i = count; i < m_LastCount; i++)
                    texels[i * 2] = float4.zero;
                m_LastCount = count;
                m_Texture.Apply(false, false);
            }

            public void Dispose()
            {
                UnityEngine.Object.Destroy(m_Texture);
                UnityEngine.Object.Destroy(Mesh);
            }
        }
    }
}
