// Standalone managed arithmetic diagnostic, compiled against the real Mathematics DLL and the
// unchanged production source. This does not run Jobs/Burst and cannot establish native parity.
using System;
using BrawlerFoundation.Tests;
using SPF.L2.Combat;
using Unity.Mathematics;

static class SeparationArithmeticProbe
{
    static void Equal(float2 expected, float2 actual, string label)
    {
        if (!math.all(math.asuint(expected) == math.asuint(actual)))
            throw new Exception(label + ": " + math.asuint(expected) + " != " + math.asuint(actual));
    }
    static uint seed = 0x1A297C31u;
    static float Next()
    {
        seed = unchecked(seed * 1664525u + 1013904223u);
        return math.asfloat(0x3F800000u | (seed & 0x7FFFFFu)) - 1.5f;
    }
    static void Main()
    {
        var evaluation = BwBeltSeparationArithmetic.DetectManagedEvaluation();
        Console.WriteLine("Managed evaluation=" + evaluation);
        const int count = 200000;
        for (int i = 0; i < count; i++)
        {
            float2 self = new float2(Next(), Next()), other = new float2(Next(), Next());
            if (i < 64)
            {
                self = float2.zero;
                uint center = i < 32 ? math.asuint(.64f) : math.asuint(.001f);
                float adjacent = math.asfloat(center + (uint)(i % 16) - 8u);
                other = i % 2 == 0 ? new float2(adjacent, 0) : new float2(0, -adjacent);
            }
            if (i % 127 == 0) other = self;
            if ((math.abs(self.x - other.x) < .65f) != BwBeltSeparationArithmetic.SameHeight(self.x, other.x, evaluation))
                throw new Exception("height=" + i);
            Equal(GroundCombatQueries.Separation(self, other, .64f, i % 2, 1 - i % 2),
                BwBeltSeparationArithmetic.Contribution(self, other, .64f, i % 2, 1 - i % 2, evaluation), "pair=" + i);
        }
        foreach (int size in new[] { 32, 128 }) for (int row = 0; row < size; row++)
        {
            float2 self = new float2(row % 8 * .04f, row / 8 * .04f), reference = float2.zero, candidate = float2.zero;
            if (!(row % 13 == 0 && row > 0)) for (int other = 0; other < size; other++)
            {
                if (other == row || (other % 13 == 0 && other > 0) || ((other % 7 == 0) != (row % 7 == 0))) continue;
                float2 position = new float2(other % 8 * .04f, other / 8 * .04f);
                reference += GroundCombatQueries.Separation(self, position, .64f, row, other);
                candidate += BwBeltSeparationArithmetic.Contribution(self, position, .64f, row, other, evaluation);
                Equal(reference, candidate, "accumulation=" + size + "/" + row + "/" + other);
            }
            float a = math.length(reference), b = BwBeltSeparationArithmetic.Length(candidate, evaluation);
            if (a > .16f) reference *= .16f / a;
            if (b > .16f) candidate *= .16f / b;
            Equal(self + reference, self + candidate, "clamp=" + size + "/" + row);
            if ((size == 32 && row == 0) || (size == 128 && row == 2))
                Console.WriteLine("dense=" + size + "; row=" + row + "; production=" + math.asuint(self + reference) + "; candidate=" + math.asuint(self + candidate));
        }
        Console.WriteLine("PASS: " + count + " exact pairs and all 160 dense accumulation/clamp rows; managed diagnostic only.");
    }
}
