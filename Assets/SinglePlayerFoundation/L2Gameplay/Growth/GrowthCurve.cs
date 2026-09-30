using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SPF.L2.Growth
{
    /// <summary>Maps mass to body length and radius.</summary>
    public struct GrowthCurve
    {
        public float BaseLength;
        public float LengthPerMass;
        public float MaxLength;
        public float BaseRadius;
        public float RadiusPerSqrtMass;
        public float MaxRadius;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Length(float mass) => math.min(BaseLength + LengthPerMass * math.max(mass, 0f), MaxLength);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Radius(float mass) => math.min(BaseRadius + RadiusPerSqrtMass * math.sqrt(math.max(mass, 0f)), MaxRadius);
    }
}
