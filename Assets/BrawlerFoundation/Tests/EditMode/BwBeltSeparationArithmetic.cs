using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    // Test candidate only. The frozen production expression has two managed evaluation profiles:
    // .NET / Mono float32 rounds each scalar operation; Mono without float32 retains double
    // intermediates within expressions, rounding at the math/float2 method boundaries.
    // Select once from primitive probes, never from a scene's expected output or a Burst result.
    internal enum SeparationEvaluation { Float32, ExtendedScalar }

    internal static class BwBeltSeparationArithmetic
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float ManagedScale(float minimum, float distance) => (minimum - distance) * .5f / distance;

        public static SeparationEvaluation DetectManagedEvaluation()
        {
            float scale = ManagedScale(math.asfloat(1059313418u), math.asfloat(1035414974u)); // .64 and sqrt(.08²+.04²)
            float square = math.lengthsq(new float2(math.asfloat(3195388559u), math.asfloat(3173242634u)));
            uint scaleBits = math.asuint(scale), squareBits = math.asuint(square);
            if (scaleBits == 1078262062u && squareBits == 1030912947u) return SeparationEvaluation.Float32;
            if (scaleBits == 1078262063u && squareBits == 1030912946u) return SeparationEvaluation.ExtendedScalar;
            throw new InvalidOperationException("Unsupported managed arithmetic profile: scale=" + scaleBits + "; square=" + squareBits);
        }

        // A double-to-single conversion is an explicit rounding boundary in both evaluators.
        // Merely assigning to a float local is NOT such a boundary on legacy Mono.
        static float Round(double x) => (float)x;
        public static float Square(float2 delta, SeparationEvaluation evaluation)
        {
            if (evaluation == SeparationEvaluation.ExtendedScalar)
                return Round((double)delta.x * delta.x + (double)delta.y * delta.y);
            float x = Round((double)delta.x * delta.x), y = Round((double)delta.y * delta.y);
            return Round((double)x + y);
        }
        public static float Distance(float square) => (float)Math.Sqrt((double)square);
        public static float Scale(float minimum, float distance, SeparationEvaluation evaluation)
        {
            if (evaluation == SeparationEvaluation.ExtendedScalar)
                return Round(((double)minimum - distance) * .5d / distance);
            float difference = Round((double)minimum - distance);
            float half = Round((double)difference * .5d);
            return Round((double)half / distance);
        }
        public static float2 Contribution(float2 self, float2 other, float minimum, int selfOrder, int otherOrder,
            SeparationEvaluation evaluation)
        {
            float2 delta = self - other;
            float square = Square(delta, evaluation);
            double threshold = (double)minimum * minimum;
            if (evaluation == SeparationEvaluation.Float32) threshold = Round(threshold);
            if (square >= threshold) return float2.zero;
            if (square < .000001f) return new float2(selfOrder < otherOrder ? -minimum * .5f : minimum * .5f, 0f);
            float distance = Distance(square);
            return delta * Scale(minimum, distance, evaluation);
        }
        public static float Length(float2 push, SeparationEvaluation evaluation) => Distance(Square(push, evaluation));
        public static bool SameHeight(float self, float other, SeparationEvaluation evaluation)
        {
            // math.abs(float) observes the float argument bits, so this call boundary rounds
            // even when the subtraction was evaluated with extended scalar intermediates.
            float difference = Round((double)self - other);
            return Math.Abs((double)difference) < (double).65f;
        }
    }
}
