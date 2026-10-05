// Tier B (GLES 3.0) sprites: packed instances (32 bytes) from an RGBA8 data texture holding one 32-bit
// word per texel (8 texels per instance), decoded exactly (each channel is a byte); fetched by an
// instance id baked into the quad page mesh (vertex.z). RGBA8 is sampleable everywhere, unlike 32-bit
// integer formats, and uploads half the bytes of a float layout.
Shader "SPF/SpriteTex"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _PackedTex ("Packed instances", 2D) = "black" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _Cutoff ("Alpha cut-off (0 = blend)", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "IgnoreProjector" = "True" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull Off

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Shaders/SPFSprite.hlsl"

            sampler2D _PackedTex;
            float4 _PackedTex_TexelSize;

            uint FetchWord(float index)
            {
                float w = _PackedTex_TexelSize.z;
                float y = floor(index / w);
                float x = index - y * w;
                float4 c = tex2Dlod(_PackedTex, float4((x + 0.5) * _PackedTex_TexelSize.x, (y + 0.5) * _PackedTex_TexelSize.y, 0, 0));
                uint4 b = (uint4)round(c * 255.0);
                return b.x | (b.y << 8) | (b.z << 16) | (b.w << 24);
            }

            struct appdata { float3 vertex : POSITION; };

            v2f vert(appdata v)
            {
                float base = v.vertex.z * 8.0;
                uint4 a = uint4(FetchWord(base), FetchWord(base + 1.0), FetchWord(base + 2.0), FetchWord(base + 3.0));
                uint4 b = uint4(FetchWord(base + 4.0), FetchWord(base + 5.0), FetchWord(base + 6.0), FetchWord(base + 7.0));
                return SPFSpriteVertexPacked(v.vertex.xy, a, b);
            }

            float4 frag(v2f i) : SV_Target { return SPFSpriteFragment(i); }
            ENDCG
        }
    }
}
