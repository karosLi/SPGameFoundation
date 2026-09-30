// Tier B continuous-strip chains: geometry built by a Burst job into a Mesh (same maths as the GPU path).
Shader "SPF/ChainStripMesh"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
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

            struct appdata
            {
                float3 vertex : POSITION;
                float4 colorA : COLOR;
                float3 uv : TEXCOORD0;     // along, across, stripe period
                float4 colorB : TEXCOORD1;
            };
            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; float4 colorA : COLOR0; float4 colorB : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityWorldToClipPos(v.vertex);
                o.uv = v.uv;
                o.colorA = v.colorA;
                o.colorB = v.colorB;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                bool stripe = frac(i.uv.x / (2.0 * i.uv.z)) >= 0.5;
                float4 c = stripe ? i.colorB : i.colorA;
                float across = abs(i.uv.y);
                c.rgb *= 1.0 - across * across * 0.35;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
