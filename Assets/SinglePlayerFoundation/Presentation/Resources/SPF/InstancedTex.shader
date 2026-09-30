// Tier B (GLES 3.0): instance data fetched from an RGBA32F data texture by an instance id baked
// into the index mesh (vertex.z). Two texels per instance: (x, y, radius, depth) and colour.
Shader "SPF/InstancedTex"
{
    Properties
    {
        _DataTex ("Data", 2D) = "black" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _Shade ("Disc shading", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }
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
            #include "../../Shaders/SPFCommon.hlsl"

            sampler2D _DataTex;
            float4 _DataTex_TexelSize;   // 1/w, 1/h, w, h
            float _Shade;

            struct appdata { float3 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            float4 FetchTexel(float index)
            {
                float w = _DataTex_TexelSize.z;
                float y = floor(index / w);
                float x = index - y * w;
                return tex2Dlod(_DataTex, float4((x + 0.5) * _DataTex_TexelSize.x, (y + 0.5) * _DataTex_TexelSize.y, 0, 0));
            }

            v2f vert(appdata v)
            {
                float id = v.vertex.z;
                float4 a = FetchTexel(id * 2.0);
                float4 color = FetchTexel(id * 2.0 + 1.0);
                v2f o;
                float3 world = float3(a.xy + v.vertex.xy * a.z, a.w);
                o.pos = UnityWorldToClipPos(world);
                o.uv = v.vertex.xy;
                o.color = color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 c = _Shade > 0.5 ? SPFShadeDisc(i.uv, i.color) : i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
