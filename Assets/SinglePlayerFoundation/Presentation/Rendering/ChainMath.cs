using System.Runtime.CompilerServices;
using SPF.L1.Body;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Presentation
{
    /// <summary>CPU twin of SPFChainSample.hlsl, used by the data-texture tier and by tests.</summary>
    public static class ChainMath
    {
        public static TrailState ToTrail(in ChainHeader h, NativeArray<float2> points) => new TrailState
        {
            Start = (int)h.TrailStart,
            Capacity = (int)h.TrailMask + 1,
            Pushed = h.Newest + 1,
            Count = (int)h.Count,
            Last = points[(int)(h.TrailStart + (h.Newest & h.TrailMask))],
            Spacing = h.Spacing,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BodyLength(in ChainHeader h, float alpha) =>
            math.max(math.lerp(h.ArcPrev, h.ArcCurr, alpha), 0f) + math.max((int)h.Count - 1, 0) * h.Spacing;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NodeCount(float bodyLength, float nodeSpacing, float stride) =>
            (int)(bodyLength / math.max(nodeSpacing * stride, 1e-4f)) + 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Taper(float t) => math.lerp(1f, 0.55f, math.saturate((t - 0.75f) / 0.25f));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 Sample(in ChainHeader h, in TrailState trail, NativeArray<float2> points, float alpha, float distance)
        {
            float headArc = math.lerp(h.ArcPrev, h.ArcCurr, alpha);
            float2 head = math.lerp(h.HeadPrev, h.HeadCurr, alpha);
            return TrailMath.SampleAtArc(trail, points, head, headArc, distance);
        }
    }
}
