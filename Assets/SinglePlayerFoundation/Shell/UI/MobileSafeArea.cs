using UnityEngine;
using Unity.Mathematics;

namespace SPF.Shell.UI
{
    public static class MobileSafeArea
    {
        /// <summary>Bounds of an axis-aligned safe-root child (or a child of a full-stretch panel),
        /// in normalized camera viewport coordinates. Uses the HUD's reference-pixel scale, including
        /// preview render targets. No world-corner array, Canvas update or screen mutation is needed.</summary>
        public static float4 ChildViewport(Rect safe, int width, int height, float4 anchors, float4 offsets)
        {
            Anchors(safe, width, height, out var min, out var max);
            float2 viewport = new float2(math.max(1, width), math.max(1, height));
            float scale = width >= height ? height / 720f : width / 720f;
            float2 lo = new float2(min.x, min.y), span = new float2(max.x - min.x, max.y - min.y);
            return new float4(lo + anchors.xy * span + offsets.xy * scale / viewport,
                lo + anchors.zw * span + offsets.zw * scale / viewport);
        }

        /// <summary>Safe-area pixels to screen anchors. Invalid platform reports conservatively use
        /// the full screen. This is layout only and never changes orientation or simulation settings.</summary>
        public static void Anchors(Rect safe, int width, int height, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero; max = Vector2.one;
            if (width <= 0 || height <= 0 || safe.width <= 0 || safe.height <= 0 ||
                float.IsNaN(safe.x) || float.IsNaN(safe.y) || float.IsInfinity(safe.x) || float.IsInfinity(safe.y) ||
                float.IsNaN(safe.width) || float.IsNaN(safe.height) || float.IsInfinity(safe.width) || float.IsInfinity(safe.height)) return;
            min = new Vector2(Mathf.Clamp01(safe.xMin / width), Mathf.Clamp01(safe.yMin / height));
            max = new Vector2(Mathf.Clamp01(safe.xMax / width), Mathf.Clamp01(safe.yMax / height));
            if (max.x <= min.x || max.y <= min.y) { min = Vector2.zero; max = Vector2.one; }
        }
    }
}
