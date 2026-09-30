// Tier A: instances read from a StructuredBuffer (filled by CPU or compute), drawn with RenderMeshIndirect.
// Works in the built-in pipeline and in URP (unlit, no LightMode tag → SRPDefaultUnlit).
Shader "SPF/InstancedGPU"
{
    Properties
    {
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
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Shaders/SPFCommon.hlsl"

            StructuredBuffer<InstanceData> _Instances;
            float _Shade;

            struct appdata { float3 vertex : POSITION; uint instanceID : SV_InstanceID; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            v2f vert(appdata v)
            {
                InstanceData d = _Instances[v.instanceID];
                v2f o;
                float3 world = float3(d.position + v.vertex.xy * d.radius, d.depth);
                o.pos = UnityWorldToClipPos(world);
                o.uv = v.vertex.xy;
                o.color = d.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 c = _Shade > 0.5 ? SPFShadeDisc(i.uv, i.color) : i.color;
                c.rgb *= c.a; // premultiplied alpha: opaque (One Zero), translucent (One OneMinusSrcAlpha), additive (One One)
                return c;
            }
            ENDCG
        }
    }
}
