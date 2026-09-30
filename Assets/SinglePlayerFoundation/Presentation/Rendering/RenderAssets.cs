using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Presentation
{
    /// <summary>Loads SPF shaders / compute shaders from Resources and creates the materials each blend mode needs.</summary>
    public sealed class RenderAssets : IDisposable
    {
        public const float OpaqueQueue = 2000;

        public RenderAssets(RenderTier tier)
        {
            Tier = tier;
            InstancedShader = Load<Shader>(tier == RenderTier.GpuDriven ? "SPF/InstancedGPU" : "SPF/InstancedTex");
            StripShader = Load<Shader>(tier == RenderTier.GpuDriven ? "SPF/ChainStripGPU" : "SPF/ChainStripMesh");
            BackgroundShader = Load<Shader>("SPF/Background");
            if (tier == RenderTier.GpuDriven)
            {
                NodeExpand = Resources.Load<ComputeShader>("SPF/NodeExpand");
                PointCloud = Resources.Load<ComputeShader>("SPF/PointCloud");
            }
        }

        public RenderTier Tier { get; }
        public Shader InstancedShader { get; }
        public Shader StripShader { get; }
        public Shader BackgroundShader { get; }
        public ComputeShader NodeExpand { get; }
        public ComputeShader PointCloud { get; }

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = Resources.Load<T>(path);
            if (asset == null && typeof(T) == typeof(Shader))
                asset = Shader.Find(path) as T;
            return asset;
        }

        /// <summary>Creates a material with the render state for a blend mode (owned by the caller).</summary>
        public static Material CreateMaterial(Shader shader, BlendKind blend, bool shaded = true, int queueOffset = 0)
        {
            if (shader == null)
                return null;
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            switch (blend)
            {
                case BlendKind.Opaque:
                    material.SetFloat(Ids.SrcBlend, (float)BlendMode.One);
                    material.SetFloat(Ids.DstBlend, (float)BlendMode.Zero);
                    material.SetFloat(Ids.ZWrite, 1f);
                    material.SetFloat(Ids.ZTest, (float)CompareFunction.LessEqual);
                    material.renderQueue = 2000 + queueOffset;
                    break;
                case BlendKind.Translucent:
                    // Equal depth per chain + strict Less: the first (front-most) fragment of a chain wins,
                    // so overlapping nodes of the same chain blend only once.
                    material.SetFloat(Ids.SrcBlend, (float)BlendMode.One);
                    material.SetFloat(Ids.DstBlend, (float)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat(Ids.ZWrite, 1f);
                    material.SetFloat(Ids.ZTest, (float)CompareFunction.Less);
                    material.renderQueue = 3000 + queueOffset;
                    break;
                case BlendKind.Additive:
                    material.SetFloat(Ids.SrcBlend, (float)BlendMode.One);
                    material.SetFloat(Ids.DstBlend, (float)BlendMode.One);
                    material.SetFloat(Ids.ZWrite, 0f);
                    material.SetFloat(Ids.ZTest, (float)CompareFunction.LessEqual);
                    material.renderQueue = 3100 + queueOffset;
                    break;
            }
            material.SetFloat(Ids.Shade, shaded ? 1f : 0f);
            return material;
        }

        public void Dispose() { }

        public static class Ids
        {
            public static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
            public static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
            public static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
            public static readonly int ZTest = Shader.PropertyToID("_ZTest");
            public static readonly int Shade = Shader.PropertyToID("_Shade");
            public static readonly int Instances = Shader.PropertyToID("_Instances");
            public static readonly int DataTex = Shader.PropertyToID("_DataTex");
            public static readonly int Headers = Shader.PropertyToID("_Headers");
            public static readonly int HeaderCount = Shader.PropertyToID("_HeaderCount");
            public static readonly int Trail = Shader.PropertyToID("_Trail");
            public static readonly int TrailOut = Shader.PropertyToID("_TrailOut");
            public static readonly int TrailDeltas = Shader.PropertyToID("_TrailDeltas");
            public static readonly int TrailDeltaCount = Shader.PropertyToID("_TrailDeltaCount");
            public static readonly int Nodes = Shader.PropertyToID("_Nodes");
            public static readonly int NodeTotal = Shader.PropertyToID("_NodeTotal");
            public static readonly int Alpha = Shader.PropertyToID("_Alpha");
            public static readonly int ViewRect = Shader.PropertyToID("_ViewRect");
            public static readonly int Deltas = Shader.PropertyToID("_Deltas");
            public static readonly int DeltaCount = Shader.PropertyToID("_DeltaCount");
            public static readonly int Pool = Shader.PropertyToID("_Pool");
            public static readonly int PoolIn = Shader.PropertyToID("_PoolIn");
            public static readonly int PoolCount = Shader.PropertyToID("_PoolCount");
            public static readonly int Visible = Shader.PropertyToID("_Visible");
            public static readonly int Args = Shader.PropertyToID("_Args");
            public static readonly int Region = Shader.PropertyToID("_Region");
            public static readonly int Cell = Shader.PropertyToID("_Cell");
        }
    }
}
