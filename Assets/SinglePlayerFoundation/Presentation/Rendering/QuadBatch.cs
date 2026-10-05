using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Presentation
{
    /// <summary>
    /// Axis-aligned coloured quads rebuilt on the CPU every frame (health bars, selection boxes, tiles of
    /// a static map when built once). Small counts (hundreds to a few thousand); one draw call, works on
    /// every tier. Fill with <see cref="Add"/> between <see cref="Clear"/> and <see cref="Draw"/>.
    /// </summary>
    public sealed class QuadBatch : IDisposable
    {
        readonly Vector3[] m_Vertices;
        readonly Color32[] m_Colors;
        readonly int[] m_Indices;
        readonly Mesh m_Mesh;
        readonly Material m_Material;
        int m_Uploaded = -1;

        /// <param name="overlay">Alpha blended and drawn over the scene (bars); otherwise opaque with depth.</param>
        public QuadBatch(int capacity, bool overlay)
        {
            Capacity = capacity;
            m_Vertices = new Vector3[capacity * 4];
            m_Colors = new Color32[capacity * 4];
            m_Indices = new int[capacity * 6];
            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4, k = i * 6;
                m_Indices[k] = v; m_Indices[k + 1] = v + 1; m_Indices[k + 2] = v + 2;
                m_Indices[k + 3] = v + 2; m_Indices[k + 4] = v + 1; m_Indices[k + 5] = v + 3;
            }
            m_Mesh = new Mesh { name = "SPF Quads", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            m_Mesh.MarkDynamic();
            var shader = Resources.Load<Shader>("SPF/VertexColor");
            if (shader == null) shader = Shader.Find("SPF/VertexColor");
            if (shader != null)
            {
                m_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                m_Material.SetFloat(RenderAssets.Ids.SrcBlend, (float)(overlay ? BlendMode.SrcAlpha : BlendMode.One));
                m_Material.SetFloat(RenderAssets.Ids.DstBlend, (float)(overlay ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
                m_Material.SetFloat(RenderAssets.Ids.ZWrite, overlay ? 0f : 1f);
                m_Material.SetFloat(RenderAssets.Ids.ZTest, (float)(overlay ? CompareFunction.Always : CompareFunction.LessEqual));
                m_Material.renderQueue = overlay ? 3500 : 1900;
            }
        }

        public int Capacity { get; }
        public int Count { get; private set; }
        public Mesh Mesh => m_Mesh;

        public void Clear() => Count = 0;

        /// <summary>Adds a quad (min corner, size, depth = world z, colour); false when full.</summary>
        public bool Add(float2 min, float2 size, float depth, Color32 color)
        {
            if (Count >= Capacity) return false;
            int v = Count * 4;
            m_Vertices[v] = new Vector3(min.x, min.y, depth);
            m_Vertices[v + 1] = new Vector3(min.x + size.x, min.y, depth);
            m_Vertices[v + 2] = new Vector3(min.x, min.y + size.y, depth);
            m_Vertices[v + 3] = new Vector3(min.x + size.x, min.y + size.y, depth);
            m_Colors[v] = m_Colors[v + 1] = m_Colors[v + 2] = m_Colors[v + 3] = color;
            Count++;
            return true;
        }

        /// <summary>Uploads the quads (only when <paramref name="dirty"/> or the count changed) and draws them.</summary>
        public void Draw(Bounds bounds, int layer = 0, bool dirty = true)
        {
            if (dirty || m_Uploaded != Count)
            {
                m_Mesh.Clear();
                m_Mesh.SetVertices(m_Vertices, 0, Count * 4);
                m_Mesh.SetColors(m_Colors, 0, Count * 4);
                m_Mesh.SetIndices(m_Indices, 0, Count * 6, MeshTopology.Triangles, 0, false);
                m_Mesh.bounds = bounds;
                m_Uploaded = Count;
            }
            if (Count > 0 && m_Material != null)
                Graphics.RenderMesh(new RenderParams(m_Material) { worldBounds = bounds, layer = layer }, m_Mesh, 0, Matrix4x4.identity);
        }

        public void Dispose()
        {
            if (m_Mesh != null) UnityEngine.Object.Destroy(m_Mesh);
            if (m_Material != null) UnityEngine.Object.Destroy(m_Material);
        }
    }
}
