using Unity.Mathematics;

namespace SPF.Presentation.Sprites
{
    /// <summary>
    /// One sprite instance in 32 bytes (two uint4), half of a float layout: the per-frame upload is the
    /// main GPU bandwidth cost of instanced 2D rendering on mobile. Both sprite shaders decode it
    /// (Shaders/SPFSprite.hlsl, SPFUnpackSprite). Burst-compatible: jobs can write instances directly.
    /// <code>
    /// A.x, A.y  centre (float bits)        A.z  size x | size y (half)    A.w  depth (float bits)
    /// B.x       uv min x | y (unorm16)     B.y  uv size x | y (unorm16)
    /// B.z       colour RGBA8 (rgb 0..2 so tints can brighten, alpha 0..1)
    /// B.w       rotation (half, wrapped to ±π) | flash (unorm8) &lt;&lt; 16
    /// </code>
    /// Negative size x mirrors the sprite.
    /// </summary>
    public struct PackedSprite
    {
        public uint4 A;
        public uint4 B;

        public const int Stride = 32;

        public static PackedSprite Pack(float2 center, float2 size, float4 uv, float depth, float4 color, float rotation = 0f, float flash = 0f)
        {
            uint2 halfSize = math.f32tof16(size);
            uint4 uv16 = (uint4)math.round(math.saturate(uv) * 65535f);
            uint4 c8 = (uint4)math.round(math.saturate(color * new float4(0.5f, 0.5f, 0.5f, 1f)) * 255f);
            if (rotation > math.PI || rotation < -math.PI)
                rotation -= 2f * math.PI * math.floor((rotation + math.PI) / (2f * math.PI));
            uint flash8 = (uint)math.round(math.saturate(flash) * 255f);
            return new PackedSprite
            {
                A = new uint4(math.asuint(center.x), math.asuint(center.y), halfSize.x | (halfSize.y << 16), math.asuint(depth)),
                B = new uint4(uv16.x | (uv16.y << 16), uv16.z | (uv16.w << 16), c8.x | (c8.y << 8) | (c8.z << 16) | (c8.w << 24), math.f32tof16(rotation) | (flash8 << 16)),
            };
        }

        // Decoding (tests, tools, the float fallback upload); mirrors SPFUnpackSprite.
        public float2 Center => new float2(math.asfloat(A.x), math.asfloat(A.y));
        public float2 Size => math.f16tof32(new uint2(A.z & 0xFFFF, A.z >> 16));
        public float Depth => math.asfloat(A.w);
        public float4 Uv => new float4(B.x & 0xFFFF, B.x >> 16, B.y & 0xFFFF, B.y >> 16) / 65535f;
        public float4 Color => new float4(B.z & 0xFF, (B.z >> 8) & 0xFF, (B.z >> 16) & 0xFF, B.z >> 24) / 255f * new float4(2f, 2f, 2f, 1f);
        public float Rotation => math.f16tof32(B.w & 0xFFFF);
        public float Flash => ((B.w >> 16) & 0xFF) / 255f;

        /// <summary>The 64-byte float layout (posSize, uv, param, colour) of the fallback data-texture path.</summary>
        public void Unpack(out float4 posSize, out float4 uv, out float4 param, out float4 color)
        {
            posSize = new float4(Center, Size);
            uv = Uv;
            param = new float4(Rotation, Depth, Flash, 0f);
            color = Color;
        }
    }
}
