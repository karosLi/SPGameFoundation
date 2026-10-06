using System;
using NUnit.Framework;
using SPF.L2.Combat;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class CombatShapesTests
    {
        [Test]
        public void BeamIncludesTangencyEndCapsAndDegenerateSegment()
        {
            float2 a = new float2(0f, 0f), b = new float2(4f, 0f);
            Assert.IsTrue(CombatShapes.BeamHitsCircle(a, b, 0.5f, new float2(2f, 1f), 0.5f));
            Assert.IsFalse(CombatShapes.BeamHitsCircle(a, b, 0.5f, new float2(2f, 1.001f), 0.5f));
            Assert.IsTrue(CombatShapes.BeamHitsCircle(a, b, 0.5f, new float2(5f, 0f), 0.5f));
            Assert.IsFalse(CombatShapes.BeamHitsCircle(a, b, 0.5f, new float2(5.001f, 0f), 0.5f));
            Assert.IsTrue(CombatShapes.BeamHitsCircle(a, a, 0.5f, new float2(1f, 0f), 0.5f));
            Assert.IsFalse(CombatShapes.BeamHitsCircle(a, a, 0.5f, new float2(1.001f, 0f), 0.5f));
            Assert.IsTrue(CombatShapes.BeamHitsCircle(a, b, -1f, new float2(2f, 0f), -1f));
            Assert.AreEqual(CombatShapes.BeamHitsCircle(a, b, 0.2f, new float2(2f, 0.4f), 0.2f),
                CombatShapes.BeamHitsCircle(b, a, 0.2f, new float2(2f, 0.4f), 0.2f));
        }

        [Test]
        public void AnnulusExcludesHoleAndIncludesBothTangencies()
        {
            Assert.IsFalse(CombatShapes.AnnulusHitsCircle(float2.zero, 2f, 3f, float2.zero, 1f));
            Assert.IsTrue(CombatShapes.AnnulusHitsCircle(float2.zero, 2f, 3f, new float2(1f, 0f), 1f));
            Assert.IsFalse(CombatShapes.AnnulusHitsCircle(float2.zero, 2f, 3f, new float2(0.999f, 0f), 1f));
            Assert.IsTrue(CombatShapes.AnnulusHitsCircle(float2.zero, 2f, 3f, new float2(4f, 0f), 1f));
            Assert.IsFalse(CombatShapes.AnnulusHitsCircle(float2.zero, 2f, 3f, new float2(4.001f, 0f), 1f));
            Assert.IsTrue(CombatShapes.AnnulusHitsCircle(float2.zero, 2f, 3f, float2.zero, 4f), "target encloses the ring");
            Assert.IsTrue(CombatShapes.AnnulusHitsCircle(float2.zero, 3f, 2f, new float2(4f, 0f), 1f), "swapped inputs normalize");
            Assert.IsTrue(CombatShapes.AnnulusHitsCircle(float2.zero, -1f, -2f, float2.zero, -1f));
            Assert.IsTrue(CombatShapes.AnnulusHitsCircle(float2.zero, 2f, 2f, new float2(2f, 0f), 0f), "zero-width ring is closed");
            Assert.IsFalse(CombatShapes.AnnulusHitsCircle(float2.zero, 2f, 2f, new float2(1f, 0f), 0f));
        }

        [Test]
        public void BroadPhaseBoundsConservativelyContainEveryHitCentreAfterRadiusExpansion()
        {
            var random = new Unity.Mathematics.Random(9173);
            for (int n = 0; n < 2000; n++)
            {
                float2 a = random.NextFloat2(-8f, 8f), b = random.NextFloat2(-8f, 8f), p = random.NextFloat2(-12f, 12f);
                float width = random.NextFloat(0f, 3f), radius = random.NextFloat(0f, 2f);
                CombatShapes.BeamBounds(a, b, width, out var min, out var max);
                if (CombatShapes.BeamHitsCircle(a, b, width, p, radius))
                    Assert.IsTrue(math.all(p >= min - radius) && math.all(p <= max + radius));
                float inner = random.NextFloat(0f, 5f), outer = random.NextFloat(0f, 5f);
                CombatShapes.AnnulusBounds(a, inner, outer, out min, out max);
                if (CombatShapes.AnnulusHitsCircle(a, inner, outer, p, radius))
                    Assert.IsTrue(math.all(p >= min - radius) && math.all(p <= max + radius));
            }
        }

        [Test]
        public void ShapeTestsAndBoundsAllocateNoManagedMemory()
        {
            int hits = 0;
            for (int n = 0; n < 16; n++) Run(n, ref hits);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int n = 0; n < 1000; n++) Run(n, ref hits);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, bytes);
            Assert.Greater(hits, 0);
        }

        static void Run(int n, ref int hits)
        {
            float2 target = new float2(n % 10, 0f);
            if (CombatShapes.BeamHitsCircle(float2.zero, new float2(4f, 0f), 0.5f, target, 0.5f)) hits++;
            if (CombatShapes.AnnulusHitsCircle(float2.zero, 1f, 3f, target, 0.5f)) hits++;
            CombatShapes.BeamBounds(float2.zero, target, 0.5f, out _, out _);
            CombatShapes.AnnulusBounds(target, 1f, 3f, out _, out _);
        }
    }
}
