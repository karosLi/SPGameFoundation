using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Presentation
{
    public enum RenderTier
    {
        /// <summary>Compute + vertex-stage structured buffers + indirect draws (Metal, Vulkan, GLES 3.1+ with SSBO in VS).</summary>
        GpuDriven = 0,
        /// <summary>GLES 3.0 and weak drivers: CPU expansion into data textures + static index meshes.</summary>
        DataTexture = 1,
    }

    public static class RenderCapabilities
    {
        /// <summary>Set before renderers are created to force a tier (tests, device blacklists, QA).</summary>
        public static RenderTier? Override;

        public static RenderTier Detect()
        {
            if (Override.HasValue)
                return Override.Value;
            if (!SystemInfo.supportsComputeShaders)
                return RenderTier.DataTexture;
            // Several Mali GLES 3.1 drivers expose zero SSBOs to the vertex stage.
            if (SystemInfo.maxComputeBufferInputsVertex < 4)
                return RenderTier.DataTexture;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return RenderTier.DataTexture;
            return RenderTier.GpuDriven;
        }
    }
}
