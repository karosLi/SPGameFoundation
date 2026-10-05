using NUnit.Framework;
using SPF.L1.Body;
using SPF.Presentation;
using Unity.Jobs;
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
                TrailMath.Advance(ref trail, store.Points, head, 60);
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
                float2 expected = TrailMath.SampleBehind(trail, store.Points, head, s);
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
            TrailMath.Advance(ref trail, store.Points, head, 20);
            var header = new ChainHeader
            {
                HeadPrev = prevHead,
                HeadCurr = head,
                ArcPrev = TrailMath.PreviousArc(pushedBefore, gapBefore, trail),
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

        [Test]
        public void InstanceDataIsExactlyTwoDataTextureTexels()
        {
            // CircleBatch pages memcpy instances straight into RGBA32F texels: (x, y, radius, depth), (color).
            var instances = new Unity.Collections.NativeArray<InstanceData>(1, Unity.Collections.Allocator.Temp);
            try
            {
                instances[0] = new InstanceData(new float2(1, 2), 3, 4, new float4(5, 6, 7, 8));
                var texels = instances.Reinterpret<float4>(InstanceData.Stride);
                Assert.AreEqual(2, texels.Length);
                Assert.AreEqual(new float4(1, 2, 3, 4), texels[0]);
                Assert.AreEqual(new float4(5, 6, 7, 8), texels[1]);
            }
            finally
            {
                instances.Dispose();
            }
        }

        [Test]
        public void GridEntryIsSixteenBytesAndPacksOwnerAndData()
        {
            Assert.AreEqual(16, System.Runtime.InteropServices.Marshal.SizeOf<SPF.L1.Spatial.GridEntry>());
            var e = new SPF.L1.Spatial.GridEntry { Owner = 65535, Data = 0x8000 | 1234 };
            Assert.AreEqual(65535, e.Owner);
            Assert.AreEqual(0x8000 | 1234, e.Data);
            e.Owner = 7;
            Assert.AreEqual(0x8000 | 1234, e.Data, "setting the owner keeps the data");
        }

        [Test]
        public void StripBuilderMatchesPerSegmentSampling()
        {
            // A 40-point trail along a wave, head slightly ahead of the newest point.
            var points = new Unity.Collections.NativeArray<float2>(64, Unity.Collections.Allocator.TempJob);
            for (int g = 0; g < 40; g++) points[g] = new float2(g * 0.5f, math.sin(g * 0.3f) * 2f);
            var header = new ChainHeader
            {
                HeadPrev = points[39] + new float2(0.2f, 0f), HeadCurr = points[39] + new float2(0.4f, 0f),
                ArcPrev = 0.2f, ArcCurr = 0.4f,
                TrailStart = 0, TrailMask = 63, Newest = 39, Count = 40,
                Spacing = 0.5f, NodeSpacing = 0.6f, NodeStride = 1f,
                NodeOffset = 3, NodeCount = 25, Radius = 1.5f, Depth = 2f,
                ColorA = new float4(1, 0, 0, 1), ColorB = new float4(0, 1, 0, 1), Stripe = 3,
            };
            int segments = (int)header.NodeCount - 1;
            int total = (int)header.NodeOffset + segments;
            var vertices = new Unity.Collections.NativeArray<ChainRenderer.StripVertex>(total * 4, Unity.Collections.Allocator.TempJob);
            var indices = new Unity.Collections.NativeArray<uint>(total * 6, Unity.Collections.Allocator.TempJob);
            var headers = new Unity.Collections.NativeArray<ChainHeader>(1, Unity.Collections.Allocator.TempJob);
            headers[0] = header;
            const float alpha = 0.5f;
            try
            {
                new ChainRenderer.BuildStripsJob { Headers = headers, Points = points, Vertices = vertices, Indices = indices, Alpha = alpha }
                    .Schedule(1, 1).Complete();

                // Reference: the original per-segment algorithm (3 samples per endpoint).
                var trail = ChainMath.ToTrail(header, points);
                float step = header.NodeSpacing * header.NodeStride;
                for (int k = 0; k < segments; k++)
                {
                    int v = ((int)header.NodeOffset + k) * 4;
                    for (int e = 0; e < 2; e++)
                    {
                        int pt = k + e;
                        float sArc = pt * step;
                        float2 p = ChainMath.Sample(header, trail, points, alpha, sArc);
                        float2 prev = ChainMath.Sample(header, trail, points, alpha, math.max(sArc - step, 0f));
                        float2 next = ChainMath.Sample(header, trail, points, alpha, sArc + step);
                        float2 tangent = math.normalizesafe(next - prev, new float2(1f, 0f));
                        float2 normal = new float2(-tangent.y, tangent.x);
                        float radius = header.Radius * ChainMath.Taper((float)pt / header.NodeCount);
                        float2 left = vertices[v + e * 2].Position.xy, right = vertices[v + e * 2 + 1].Position.xy;
                        Assert.AreEqual((p - normal * radius).x, left.x, 1e-3f, $"segment {k} end {e}");
                        Assert.AreEqual((p - normal * radius).y, left.y, 1e-3f, $"segment {k} end {e}");
                        Assert.AreEqual((p + normal * radius).x, right.x, 1e-3f, $"segment {k} end {e}");
                        Assert.AreEqual((p + normal * radius).y, right.y, 1e-3f, $"segment {k} end {e}");
                        Assert.AreEqual(sArc, vertices[v + e * 2].Uv.x, 1e-4f);
                    }
                    int i = ((int)header.NodeOffset + k) * 6;
                    CollectionAssert.AreEqual(new uint[] { (uint)v, (uint)v + 1, (uint)v + 2, (uint)v + 2, (uint)v + 1, (uint)v + 3 },
                        new[] { indices[i], indices[i + 1], indices[i + 2], indices[i + 3], indices[i + 4], indices[i + 5] });
                }
            }
            finally
            {
                points.Dispose();
                vertices.Dispose();
                indices.Dispose();
                headers.Dispose();
            }
        }

        [Test]
        public void PagePrefixSubmeshesCoverTheUsedDiscs()
        {
            Assert.AreEqual(5, DiscMesh.PrefixCount(4096));   // 256, 512, 1024, 2048, 4096
            Assert.AreEqual(1, DiscMesh.PrefixCount(200));
            Assert.AreEqual(3, DiscMesh.PrefixCount(700));    // 256, 512, 700
            for (int instances = 1; instances <= 4096; instances += 37)
                for (int count = 1; count <= instances; count += 13)
                {
                    int p = DiscMesh.PrefixFor(count, instances);
                    Assert.Less(p, DiscMesh.PrefixCount(instances));
                    int drawn = System.Math.Min(DiscMesh.MinPrefix << p, instances);
                    Assert.GreaterOrEqual(drawn, count);
                    Assert.LessOrEqual(drawn, System.Math.Max(2 * count, DiscMesh.MinPrefix));
                }
        }
    }
}
