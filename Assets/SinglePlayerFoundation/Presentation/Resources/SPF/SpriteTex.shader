// Tier B (GLES 3.0) sprites: instance data from an RGBA32F data texture (4 texels per instance),
// fetched by an instance id baked into the quad page mesh (vertex.z).
Shader "SPF/SpriteTex"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _DataTex ("Data", 2D) = "black" {}
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
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Shaders/SPFSprite.hlsl"

            sampler2D _DataTex;
            float4 _DataTex_TexelSize;

            float4 FetchTexel(float index)
            {
                float w = _DataTex_TexelSize.z;
                float y = floor(index / w);
                float x = index - y * w;
                return tex2Dlod(_DataTex, float4((x + 0.5) * _DataTex_TexelSize.x, (y + 0.5) * _DataTex_TexelSize.y, 0, 0));
            }

            struct appdata { float3 vertex : POSITION; };

            v2f vert(appdata v)
            {
                float base = v.vertex.z * 4.0;
                return SPFSpriteVertex(v.vertex.xy, FetchTexel(base), FetchTexel(base + 1.0), FetchTexel(base + 2.0), FetchTexel(base + 3.0));
            }

            float4 frag(v2f i) : SV_Target { return SPFSpriteFragment(i); }
            ENDCG
        }
    }
}
