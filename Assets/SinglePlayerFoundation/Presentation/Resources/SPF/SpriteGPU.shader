// Tier A sprites: packed instances (2 x uint4, 32 bytes) from a StructuredBuffer, one quad mesh, indirect draw.
// Atlas sampled with point filtering (pixel art). Opaque mode: alpha cut-out with depth write, so
// overlapping sprites need no sorting; translucent / additive modes for effects.
Shader "SPF/SpriteGPU"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _Cutoff ("Alpha cut-off (0 = blend)", Float) = 0.5
        _NormalTex ("Normals (SPF_LIT)", 2D) = "bump" {}
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
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local __ SPF_LIT
            #include "UnityCG.cginc"
            #include "../../Shaders/SPFSprite.hlsl"

            struct SpriteData { uint4 a; uint4 b; };
            StructuredBuffer<SpriteData> _Sprites;

            struct appdata { float3 vertex : POSITION; uint instanceID : SV_InstanceID; };

            v2f vert(appdata v)
            {
                SpriteData d = _Sprites[v.instanceID];
                return SPFSpriteVertexPacked(v.vertex.xy, d.a, d.b);
            }

            float4 frag(v2f i) : SV_Target { return SPFSpriteFragment(i); }
            ENDCG
        }
    }
}
