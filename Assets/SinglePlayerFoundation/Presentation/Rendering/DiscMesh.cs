using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Presentation
{
    /// <summary>Unit polygon fans approximating a disc (vertex.xy = corner on the unit circle).</summary>
    public static class DiscMesh
    {
        public const int Segments = 16;
        public const int VerticesPerDisc = Segments + 1;
        public const int IndicesPerDisc = Segments * 3;

        /// <summary>One disc, for GPU instancing (instance data from a buffer).</summary>
        public static Mesh CreateSingle()
        {
            var vertices = new Vector3[VerticesPerDisc];
            var indices = new int[IndicesPerDisc];
            Fill(vertices, indices, 0, 0f);
            var mesh = new Mesh { name = "SPF Disc", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
            return mesh;
        }

        /// <summary>
        /// Many discs in one mesh, each carrying its instance id in vertex.z (data-texture path).
        /// Draw a prefix of it by shrinking the submesh index count.
        /// </summary>
        public static Mesh CreateIndexed(int instances)
        {
            var vertices = new Vector3[instances * VerticesPerDisc];
            var indices = new int[instances * IndicesPerDisc];
            for (int i = 0; i < instances; i++)
                Fill(vertices, indices, i, i);
            var mesh = new Mesh { name = "SPF Disc Page", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
            mesh.UploadMeshData(true);
            return mesh;
        }

        static void Fill(Vector3[] vertices, int[] indices, int instance, float id)
        {
            int v0 = instance * VerticesPerDisc;
            int i0 = instance * IndicesPerDisc;
            vertices[v0] = new Vector3(0f, 0f, id);
            // Inscribed 16-gon: its edge deviates from the circle by < 2%, invisible with rim shading,
            // and no fragment lies outside the disc (no discard needed, keeps early-Z / HSR working).
            const float scale = 1f;
            for (int s = 0; s < Segments; s++)
            {
                float a = s * 2f * math.PI / Segments;
                vertices[v0 + 1 + s] = new Vector3(math.cos(a) * scale, math.sin(a) * scale, id);
                indices[i0 + s * 3] = v0;
                indices[i0 + s * 3 + 1] = v0 + 1 + s;
                indices[i0 + s * 3 + 2] = v0 + 1 + (s + 1) % Segments;
            }
        }
    }
}
