#ifndef SPF_SPRITE_INCLUDED
#define SPF_SPRITE_INCLUDED

// Shared sprite vertex / fragment logic for both tiers.
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
