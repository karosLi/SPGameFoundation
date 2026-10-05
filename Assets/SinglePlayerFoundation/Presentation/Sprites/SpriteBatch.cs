using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Presentation.Sprites
{
    /// <summary>One sprite instance: 4 x float4 (64 bytes), the layout both sprite shaders read.</summary>
    public struct SpriteInstance
    {
        public float4 PosSize;   // centre xy, size zw (negative x mirrors)
        public float4 Uv;        // atlas rect min xy, size zw
        public float4 Param;     // rotation, depth, flash, unused
        public float4 Color;     // tint, straight alpha

        public const int Stride = 64;
    }

    /// <summary>
    /// Instanced sprites from one atlas texture, on both render tiers (like <see cref="CircleBatch"/>):
    /// GPU-driven tier draws a single quad with a structured buffer of instances; the GLES 3 data-texture
    /// tier draws prefix submeshes of prebuilt quad pages that fetch instances from an RGBA32F texture.
    /// Opaque batches cut out alpha and write depth (no sorting needed); translucent / additive ones blend.
    /// Static content (tile maps) can skip re-uploading with <c>Draw(dirty: false)</c>.
    /// </summary>
    public sealed class SpriteBatch : IDisposable
    {
        public const int PageSize = 4096;
        const int TextureWidth = 2048;

        readonly RenderTier m_Tier;
        readonly Material m_Material;
        NativeArray<SpriteInstance> m_Instances;
        int m_Uploaded = -1;

        GraphicsBuffer m_Buffer, m_Args;
        NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs> m_ArgsData;
        Mesh m_Quad;
        Page[] m_Pages;

        public SpriteBatch(RenderTier tier, Texture atlas, BlendKind blend, int capacity, int queueOffset = 0)
        {
            m_Tier = tier;
            Capacity = capacity;
            m_Instances = new NativeArray<SpriteInstance>(capacity, Allocator.Persistent);
            var shader = Resources.Load<Shader>(tier == RenderTier.GpuDriven ? "SPF/SpriteGPU" : "SPF/SpriteTex");
            m_Material = RenderAssets.CreateMaterial(shader, blend, false, queueOffset);
            if (m_Material != null)
            {
                m_Material.SetTexture(Ids.MainTex, atlas);
                m_Material.SetFloat(Ids.Cutoff, blend == BlendKind.Opaque ? 0.5f : 0f);
                if (blend == BlendKind.Translucent)
                {
                    // Effects overlap themselves: no depth write, drawn after the opaque scene.
                    m_Material.SetFloat(RenderAssets.Ids.ZWrite, 0f);
                    m_Material.SetFloat(RenderAssets.Ids.ZTest, (float)CompareFunction.LessEqual);
                }
            }
            if (tier == RenderTier.GpuDriven)
            {
                m_Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, SpriteInstance.Stride);
                m_Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                m_ArgsData = new NativeArray<GraphicsBuffer.IndirectDrawIndexedArgs>(1, Allocator.Persistent);
                m_Quad = CreateQuad();
                m_Material?.SetBuffer(Ids.Sprites, m_Buffer);
            }
            else
                m_Pages = new Page[(capacity + PageSize - 1) / PageSize];
        }

        public int Capacity { get; }
        public int Count { get; set; }
        public NativeArray<SpriteInstance> Instances => m_Instances;
        public Material Material => m_Material;

        public void Clear() => Count = 0;

        /// <summary>Appends a sprite; returns false when full. Negative <paramref name="size"/>.x mirrors it.</summary>
        public bool Add(float2 center, float2 size, float4 uv, float depth, float4 color, float rotation = 0f, float flash = 0f)
        {
            if (Count >= Capacity) return false;
            m_Instances[Count++] = new SpriteInstance
            {
                PosSize = new float4(center, size),
                Uv = uv,
                Param = new float4(rotation, depth, flash, 0f),
                Color = color,
            };
            return true;
        }

        public void Draw(Bounds bounds, int layer = 0, bool dirty = true)
        {
            if (m_Material == null || Count <= 0) { m_Uploaded = -1; return; }
            int count = math.min(Count, Capacity);
            bool upload = dirty || m_Uploaded != count;
            m_Uploaded = count;
            var rp = new RenderParams(m_Material) { worldBounds = bounds, layer = layer };
            if (m_Tier == RenderTier.GpuDriven)
            {
                if (upload)
                {
                    m_Buffer.SetData(m_Instances, 0, 0, count);
                    m_ArgsData[0] = new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = 6, instanceCount = (uint)count };
                    m_Args.SetData(m_ArgsData);
                }
                Graphics.RenderMeshIndirect(rp, m_Quad, m_Args);
                return;
            }
            for (int p = 0; p * PageSize < count; p++)
            {
                var page = m_Pages[p] ??= new Page(math.min(PageSize, Capacity - p * PageSize));
                int n = math.min(PageSize, count - p * PageSize);
                if (upload) page.Upload(m_Instances, p * PageSize, n);
                rp.matProps = page.Properties;
                Graphics.RenderMesh(rp, page.Mesh, DiscMesh.PrefixFor(n, page.Instances), Matrix4x4.identity);
            }
        }

        static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "SPF Sprite Quad", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) });
            mesh.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
            return mesh;
        }

        public void Dispose()
        {
            if (m_Instances.IsCreated) m_Instances.Dispose();
            if (m_ArgsData.IsCreated) m_ArgsData.Dispose();
            m_Buffer?.Dispose();
            m_Args?.Dispose();
            if (m_Quad != null) RenderObjects.Destroy(m_Quad);
            if (m_Material != null) RenderObjects.Destroy(m_Material);
            if (m_Pages != null) foreach (var p in m_Pages) p?.Dispose();
        }

        static class Ids
        {
            public static readonly int MainTex = Shader.PropertyToID("_MainTex");
            public static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
            public static readonly int Sprites = Shader.PropertyToID("_Sprites");
        }

        /// <summary>Data-texture page: 4 RGBA32F texels per sprite + a quad mesh with prefix submeshes.</summary>
        sealed class Page : IDisposable
        {
            readonly Texture2D m_Texture;
            int m_LastCount;

            public Page(int instances)
            {
                Instances = instances;
                int width = math.min(TextureWidth, math.ceilpow2(instances * 4));
                int height = (instances * 4 + width - 1) / width;
                m_Texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var texels = m_Texture.GetPixelData<float4>(0);
                for (int i = 0; i < texels.Length; i++) texels[i] = float4.zero;
                m_Texture.Apply(false, false);
                Mesh = CreatePageMesh(instances);
                Properties = new MaterialPropertyBlock();
                Properties.SetTexture(RenderAssets.Ids.DataTex, m_Texture);
            }

            public int Instances { get; }
            public Mesh Mesh { get; }
            public MaterialPropertyBlock Properties { get; }

            public void Upload(NativeArray<SpriteInstance> source, int start, int count)
            {
                var texels = m_Texture.GetPixelData<float4>(0);
                NativeArray<float4>.Copy(source.Reinterpret<float4>(SpriteInstance.Stride), start * 4, texels, 0, count * 4);
                // Unused slots collapse to zero-size quads.
                for (int i = count; i < m_LastCount; i++) texels[i * 4] = float4.zero;
                m_LastCount = count;
                m_Texture.Apply(false, false);
            }

            static Mesh CreatePageMesh(int instances)
            {
                var vertices = new Vector3[instances * 4];
                var indices = new int[instances * 6];
                for (int i = 0; i < instances; i++)
                {
                    vertices[i * 4] = new Vector3(-0.5f, -0.5f, i);
                    vertices[i * 4 + 1] = new Vector3(0.5f, -0.5f, i);
                    vertices[i * 4 + 2] = new Vector3(-0.5f, 0.5f, i);
                    vertices[i * 4 + 3] = new Vector3(0.5f, 0.5f, i);
                    int v = i * 4, k = i * 6;
                    indices[k] = v; indices[k + 1] = v + 1; indices[k + 2] = v + 2;
                    indices[k + 3] = v + 2; indices[k + 4] = v + 1; indices[k + 5] = v + 3;
                }
                var mesh = new Mesh { name = "SPF Sprite Page", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                // Non-overlapping prefix submeshes (each its own copy of the indices), see DiscMesh.CreateIndexed.
                int prefixes = DiscMesh.PrefixCount(instances), total = 0;
                for (int p = 0; p < prefixes; p++) total += math.min(DiscMesh.MinPrefix << p, instances) * 6;
                var all = new int[total];
                for (int p = 0, offset = 0; p < prefixes; p++)
                {
                    int count = math.min(DiscMesh.MinPrefix << p, instances) * 6;
                    Array.Copy(indices, 0, all, offset, count);
                    offset += count;
                }
                const MeshUpdateFlags Quiet = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;
                mesh.SetIndexBufferParams(total, IndexFormat.UInt32);
                mesh.SetIndexBufferData(all, 0, 0, total, Quiet);
                mesh.subMeshCount = prefixes;
                for (int p = 0, offset = 0; p < prefixes; p++)
                {
                    int count = math.min(DiscMesh.MinPrefix << p, instances) * 6;
                    mesh.SetSubMesh(p, new SubMeshDescriptor(offset, count), Quiet);
                    offset += count;
                }
                mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
                mesh.UploadMeshData(true);
                return mesh;
            }

            public void Dispose()
            {
                RenderObjects.Destroy(m_Texture);
                RenderObjects.Destroy(Mesh);
            }
        }
    }
}
