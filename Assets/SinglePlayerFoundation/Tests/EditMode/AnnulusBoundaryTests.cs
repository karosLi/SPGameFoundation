using NUnit.Framework;
using SPF.L2.Combat;
using SPF.Contracts;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class AnnulusBoundaryTests
    {
        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        struct StrictProbe : IJob
        {
            public float Radius;
            [ReadOnly] public NativeArray<float2> Points;
            public NativeArray<int> Results;
            public void Execute() { for (int i = 0; i < Points.Length; i++) Results[i] = CombatShapes.AnnulusHitsCircle(float2.zero, 2, 4, Points[i], Radius) ? 1 : 0; }
        }
        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct FastProbe : IJob
        {
            public float Radius;
            [ReadOnly] public NativeArray<float2> Points;
            public NativeArray<int> Results;
            public void Execute() { for (int i = 0; i < Points.Length; i++) Results[i] = CombatShapes.AnnulusHitsCircle(float2.zero, 2, 4, Points[i], Radius) ? 1 : 0; }
        }

        [TestCase(.1f)]
        [TestCase(.2f)]
        [TestCase(.3f)]
        [TestCase(.7f)]
        public void StoredFloatBoundariesAndTheirImmediateNeighboursAgreeInManagedStrictAndFastBurst(float radius)
        {
            float far = 4 + radius, near = 2 - radius;
            float belowFar = math.asfloat(math.asuint(far) - 1u), aboveFar = math.asfloat(math.asuint(far) + 1u);
            float belowNear = math.asfloat(math.asuint(near) - 1u), aboveNear = math.asfloat(math.asuint(near) + 1u);
            using var points = new NativeArray<float2>(24, Allocator.TempJob);
            using var strict = new NativeArray<int>(24, Allocator.TempJob);
            using var fast = new NativeArray<int>(24, Allocator.TempJob);
            for (int axis = 0; axis < 4; axis++)
            {
                float2 direction = axis == 0 ? new float2(1, 0) : axis == 1 ? new float2(-1, 0) : axis == 2 ? new float2(0, 1) : new float2(0, -1);
                int n = axis * 6;
                points.Set(n, direction * belowFar); points.Set(n + 1, direction * far); points.Set(n + 2, direction * aboveFar);
                points.Set(n + 3, direction * belowNear); points.Set(n + 4, direction * near); points.Set(n + 5, direction * aboveNear);
            }
            new StrictProbe { Radius = radius, Points = points, Results = strict }.Schedule().Complete();
            new FastProbe { Radius = radius, Points = points, Results = fast }.Schedule().Complete();
            TestContext.WriteLine("Annulus boundary radius bits=" + math.asuint(radius) + "; near bits=" + math.asuint(near) + "; far bits=" + math.asuint(far)
                + "; strict/fast edge=" + strict[1] + "/" + fast[1] + "; strict/fast next-outside=" + strict[2] + "/" + fast[2]);
            for (int i = 0; i < points.Length; i++)
            {
                int expected = i % 6 == 2 || i % 6 == 3 ? 0 : 1;
                Assert.AreEqual(expected, CombatShapes.AnnulusHitsCircle(float2.zero, 2, 4, points[i], radius) ? 1 : 0, "managed boundary sample " + i);
                Assert.AreEqual(expected, strict[i], "strict Burst boundary sample " + i);
                Assert.AreEqual(expected, fast[i], "fast/FMA-capable Burst boundary sample " + i);
            }
        }

        [Test]
        public void ReproducesMixedPrecisionRejectionWithoutAddingAnEpsilon()
        {
            float boundary = math.asfloat(0x4089999Au); // nearest binary32 value of 4f + .3f
            double exactSquare = (double)boundary * boundary;
            float roundedSquare = math.asfloat(math.asuint((float)exactSquare));
            Assert.Greater((double)roundedSquare, exactSquare, "a float dot vs promoted scalar square can reject identical stored-float bounds");
            Assert.IsTrue(CombatShapes.AnnulusHitsCircle(float2.zero, 0, 4, new float2(boundary, 0), .3f));
            Assert.IsFalse(CombatShapes.AnnulusHitsCircle(float2.zero, 0, 4, new float2(math.asfloat(0x4089999Bu), 0), .3f));
            TestContext.WriteLine("binary32 squared distance=" + ((double)roundedSquare).ToString("R") + "; promoted radius square=" + exactSquare.ToString("R"));
        }

        [Test]
        public void FiniteLargeCoordinatesDoNotTurnSquaredOverflowIntoAHit()
        {
            Assert.IsFalse(CombatShapes.AnnulusHitsCircle(float2.zero, 0, 1e20f, new float2(3e20f, 0), 0));
            Assert.IsTrue(CombatShapes.AnnulusHitsCircle(float2.zero, 0, 1e20f, new float2(1e20f, 0), 0));
        }
    }
}
