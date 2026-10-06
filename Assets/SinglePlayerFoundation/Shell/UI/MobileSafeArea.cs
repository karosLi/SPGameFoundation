using UnityEngine;

namespace SPF.Shell.UI
{
    public static class MobileSafeArea
    {
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
