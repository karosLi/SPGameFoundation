using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Presentation
{
    /// <summary>Unit polygon fans approximating a disc (vertex.xy = corner on the unit circle).</summary>
    public static class DiscMesh
    {
        /// <summary>Default (high) detail. <see cref="LowSegments"/> is used by adaptive quality.</summary>
        public const int Segments = 16;
        public const int LowSegments = 8;
        public const int VerticesPerDisc = Segments + 1;
        public const int IndicesPerDisc = Segments * 3;

        public static int VerticesFor(int segments) => segments + 1;
        public static int IndicesFor(int segments) => segments * 3;

        /// <summary>
        /// Corner radius for a fan with this many segments. 16+: inscribed (edge within 2% of the circle,
        /// no fragment outside it). Fewer: equal-area polygon, so small discs keep their apparent size
        /// (an inscribed octagon is 10% smaller) and the rim shading still saturates at the corners.
        /// </summary>
        public static float CornerScale(int segments) =>
            segments >= 16 ? 1f : math.sqrt(math.PI / (segments * 0.5f * math.sin(2f * math.PI / segments)));

        /// <summary>One disc, for GPU instancing (instance data from a buffer).</summary>
        public static Mesh CreateSingle(int segments = Segments)
        {
            var vertices = new Vector3[VerticesFor(segments)];
            var indices = new int[IndicesFor(segments)];
            Fill(vertices, indices, 0, 0f, segments);
            var mesh = new Mesh { name = "SPF Disc " + segments, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
            return mesh;
        }

        /// <summary>
        /// Many discs in one mesh, each carrying its instance id in vertex.z (data-texture path).
        /// Draw a prefix of it by shrinking the submesh index count.
        /// </summary>
        public static Mesh CreateIndexed(int instances, int segments = Segments)
        {
            int vpd = VerticesFor(segments), ipd = IndicesFor(segments);
            var vertices = new Vector3[instances * vpd];
            var indices = new int[instances * ipd];
            for (int i = 0; i < instances; i++)
                Fill(vertices, indices, i, i, segments);
            var mesh = new Mesh { name = "SPF Disc Page " + segments, hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
            mesh.UploadMeshData(true);
            return mesh;
        }

        static void Fill(Vector3[] vertices, int[] indices, int instance, float id, int segments)
        {
            int v0 = instance * VerticesFor(segments);
            int i0 = instance * IndicesFor(segments);
            vertices[v0] = new Vector3(0f, 0f, id);
            // Inscribed 16-gon: its edge deviates from the circle by < 2%, invisible with rim shading,
            // and no fragment lies outside the disc (no discard needed, keeps early-Z / HSR working).
            float scale = CornerScale(segments);
            for (int s = 0; s < segments; s++)
            {
                float a = s * 2f * math.PI / segments;
                vertices[v0 + 1 + s] = new Vector3(math.cos(a) * scale, math.sin(a) * scale, id);
                indices[i0 + s * 3] = v0;
                indices[i0 + s * 3 + 1] = v0 + 1 + s;
                indices[i0 + s * 3 + 2] = v0 + 1 + (s + 1) % segments;
            }
        }
    }
}
