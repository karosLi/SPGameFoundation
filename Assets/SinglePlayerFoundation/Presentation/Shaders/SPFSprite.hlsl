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

#ifdef SPF_LIT
// Lit sprites (keyword SPF_LIT): a normal atlas laid out like the colour atlas, up to 8 point lights
// set globally by SpriteLighting, plus ambient. Lighting costs fragment work only; instances are unchanged.
#define SPF_MAX_LIGHTS 8
sampler2D _NormalTex;
float4 _SPFLightPos[SPF_MAX_LIGHTS];     // xy world position, z height above the sprite plane, w 1 / radius^2
float4 _SPFLightColor[SPF_MAX_LIGHTS];   // rgb colour * intensity
float4 _SPFAmbient;                      // rgb ambient, w light count
#endif

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
    float flash : TEXCOORD1;
#ifdef SPF_LIT
    float4 world : TEXCOORD2;    // xy world position, zw (cos, sin) of the rotation
    float2 mirror : TEXCOORD3;   // sign of the size: mirrored sprites flip their normals
#endif
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
#ifdef SPF_LIT
    o.world = float4(posSize.xy + rotated, c, s);
    o.mirror = sign(posSize.zw);
#endif
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
#ifdef SPF_LIT
    float3 n = tex2D(_NormalTex, i.uv).xyz * 2.0 - 1.0;
    n.xy *= i.mirror;
    n.xy = float2(n.x * i.world.z - n.y * i.world.w, n.x * i.world.w + n.y * i.world.z);
    float3 light = _SPFAmbient.rgb;
    int count = (int)_SPFAmbient.w;
    for (int k = 0; k < SPF_MAX_LIGHTS; k++)
    {
        if (k >= count) break;
        float3 d = float3(_SPFLightPos[k].xy - i.world.xy, _SPFLightPos[k].z);
        float falloff = saturate(1.0 - dot(d.xy, d.xy) * _SPFLightPos[k].w);
        light += _SPFLightColor[k].rgb * (falloff * falloff * saturate(dot(n, normalize(d))));
    }
    c.rgb *= light;
#endif
    c.rgb = lerp(c.rgb, float3(1.0, 1.0, 1.0), i.flash);
    c.rgb *= c.a;   // premultiplied: opaque (One Zero), translucent (One OneMinusSrcAlpha), additive (One One)
    return c;
}

#endif
