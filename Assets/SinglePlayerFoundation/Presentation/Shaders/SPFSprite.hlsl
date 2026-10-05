#ifndef SPF_SPRITE_INCLUDED
#define SPF_SPRITE_INCLUDED

// Shared sprite vertex / fragment logic for both tiers.
// Instances arrive packed in two uint4 (see PackedSprite.cs) and are decoded by SPFUnpackSprite into:
// posSize: centre xy, size zw (negative x size mirrors the sprite)
// uv:      atlas rect min xy, size zw
// param:   rotation (radians), depth (world z), white flash 0..1, unused
// color:   tint (straight alpha)

sampler2D _MainTex;
float _Cutoff;

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
    float flash : TEXCOORD1;
};

void SPFUnpackSprite(uint4 a, uint4 b, out float4 posSize, out float4 uv, out float4 param, out float4 color)
{
    posSize = float4(asfloat(a.x), asfloat(a.y), f16tof32(a.z & 0xFFFF), f16tof32(a.z >> 16));
    uv = float4(b.x & 0xFFFF, b.x >> 16, b.y & 0xFFFF, b.y >> 16) * (1.0 / 65535.0);
    color = float4(b.z & 0xFF, (b.z >> 8) & 0xFF, (b.z >> 16) & 0xFF, b.z >> 24) * (1.0 / 255.0) * float4(2.0, 2.0, 2.0, 1.0);
    param = float4(f16tof32(b.w & 0xFFFF), asfloat(a.w), ((b.w >> 16) & 0xFF) * (1.0 / 255.0), 0.0);
}

v2f SPFSpriteVertex(float2 corner, float4 posSize, float4 uv, float4 param, float4 color)
{
    float2 local = corner * posSize.zw;
    float s = sin(param.x), c = cos(param.x);
    float2 rotated = float2(local.x * c - local.y * s, local.x * s + local.y * c);
    v2f o;
    o.pos = UnityWorldToClipPos(float3(posSize.xy + rotated, param.y));
    o.uv = uv.xy + (corner + 0.5) * uv.zw;
    o.color = color;
    o.flash = param.z;
    return o;
}

v2f SPFSpriteVertexPacked(float2 corner, uint4 a, uint4 b)
{
    float4 posSize, uv, param, color;
    SPFUnpackSprite(a, b, posSize, uv, param, color);
    return SPFSpriteVertex(corner, posSize, uv, param, color);
}

float4 SPFSpriteFragment(v2f i)
{
    float4 texel = tex2D(_MainTex, i.uv);
    float4 c = texel * i.color;
    if (_Cutoff > 0.0)
        clip(c.a - _Cutoff);
    c.rgb = lerp(c.rgb, float3(1.0, 1.0, 1.0), i.flash);
    c.rgb *= c.a;   // premultiplied: opaque (One Zero), translucent (One OneMinusSrcAlpha), additive (One One)
    return c;
}

#endif
