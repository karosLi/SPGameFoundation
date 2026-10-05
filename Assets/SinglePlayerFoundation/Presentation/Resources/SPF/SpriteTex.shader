// Tier B (GLES 3.0) sprites: packed instances from an RGBA32UI data texture (2 texels, 32 bytes per
// instance, read with texelFetch), fetched by an instance id baked into the quad page mesh (vertex.z).
// Devices without 32-bit integer textures use SPF/SpriteTexFloat (4 RGBA32F texels per instance).
Shader "SPF/SpriteTex"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
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

            Texture2D<uint4> _PackedTex;
            float _PackedWidth;

            uint4 FetchPacked(uint index)
            {
                uint w = (uint)_PackedWidth;
                return _PackedTex.Load(int3(index % w, index / w, 0));
            }

            struct appdata { float3 vertex : POSITION; };

            v2f vert(appdata v)
            {
                uint base = (uint)v.vertex.z * 2u;
                return SPFSpriteVertexPacked(v.vertex.xy, FetchPacked(base), FetchPacked(base + 1u));
            }

            float4 frag(v2f i) : SV_Target { return SPFSpriteFragment(i); }
            ENDCG
        }
    }
}
