using NUnit.Framework;
using SPF.L1.Body;
using SPF.Presentation;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class PresentationMathTests
    {
        [Test]
        public void ChainSamplingMatchesTheSimulationAtAlphaOne()
        {
            using var store = new BodyStore(4096, 1024);
            store.TryAllocate(128, out var trail);
            TrailMath.Reset(ref trail, store.Points, float2.zero, new float2(-1, 0), 0.4f, 40);
            float2 head = float2.zero;
            for (int i = 0; i < 30; i++)
            {
                head += new float2(0.13f, 0.07f * math.sin(i * 0.3f));
                TrailMath.Advance(ref trail, store.Points, head, 0.4f, 60);
            }
            var header = new ChainHeader
            {
                HeadPrev = head - new float2(0.13f, 0f),
                HeadCurr = head,
                ArcPrev = 0f,
                ArcCurr = math.length(head - trail.Last),
                TrailStart = (uint)trail.Start,
                TrailMask = (uint)(trail.Capacity - 1),
                Newest = trail.Pushed - 1,
                Count = (uint)trail.Count,
                Spacing = 0.4f,
            };
            var fromHeader = ChainMath.ToTrail(header, store.Points);
            for (float s = 0f; s < 20f; s += 0.77f)
            {
                float2 expected = TrailMath.SampleBehind(trail, store.Points, head, s, 0.4f);
                float2 actual = ChainMath.Sample(header, fromHeader, store.Points, 1f, s);
                Assert.AreEqual(expected.x, actual.x, 1e-4f);
                Assert.AreEqual(expected.y, actual.y, 1e-4f);
            }
        }

        [Test]
        public void InterpolationStartsAtThePreviousHead()
        {
            using var store = new BodyStore(1024, 256);
            store.TryAllocate(64, out var trail);
            TrailMath.Reset(ref trail, store.Points, float2.zero, new float2(-1, 0), 0.5f, 20);
            float2 prevHead = float2.zero;
            uint pushedBefore = trail.Pushed;
            float gapBefore = math.length(prevHead - trail.Last);
            float2 head = new float2(1.3f, 0f);
            TrailMath.Advance(ref trail, store.Points, head, 0.5f, 20);
            var header = new ChainHeader
            {
                HeadPrev = prevHead,
                HeadCurr = head,
                ArcPrev = TrailMath.PreviousArc(pushedBefore, gapBefore, trail.Pushed, 0.5f),
                ArcCurr = math.length(head - trail.Last),
                TrailStart = (uint)trail.Start,
                TrailMask = (uint)(trail.Capacity - 1),
                Newest = trail.Pushed - 1,
                Count = (uint)trail.Count,
                Spacing = 0.5f,
            };
            var t = ChainMath.ToTrail(header, store.Points);
            float2 atZero = ChainMath.Sample(header, t, store.Points, 0f, 0f);
            Assert.AreEqual(0f, atZero.x, 1e-4f, "alpha 0 draws the head where it was last tick");
            float2 tailNow = ChainMath.Sample(header, t, store.Points, 1f, 3f);
            float2 tailBefore = ChainMath.Sample(header, t, store.Points, 0f, 3f);
            Assert.AreEqual(tailNow.x - 1.3f, tailBefore.x, 1e-3f, "the whole body slides smoothly between ticks");
        }

        [Test]
        public void GpuStructLayoutsMatchTheShaders()
        {
            Assert.AreEqual(InstanceData.Stride, System.Runtime.InteropServices.Marshal.SizeOf<InstanceData>());
            Assert.AreEqual(ChainHeader.Stride, System.Runtime.InteropServices.Marshal.SizeOf<ChainHeader>());
            Assert.AreEqual(RowDelta.Stride, System.Runtime.InteropServices.Marshal.SizeOf<RowDelta>());
            Assert.AreEqual(TrailDelta.Stride, System.Runtime.InteropServices.Marshal.SizeOf<TrailDelta>());
        }
    }
}
