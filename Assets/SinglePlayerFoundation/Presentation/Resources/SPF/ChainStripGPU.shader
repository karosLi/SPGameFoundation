// Tier A continuous-strip chains: fully procedural. SV_VertexID → (chain, segment, corner); the
// vertex shader samples the trail mirror directly, so no strip geometry is ever built on the CPU.
Shader "SPF/ChainStripGPU"
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
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Shaders/SPFCommon.hlsl"
            #include "../../Shaders/SPFChainSample.hlsl"

            StructuredBuffer<ChainHeader> _Headers;
            StructuredBuffer<float2> _Trail;
            uint _HeaderCount;
            float _Alpha;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 uv : TEXCOORD0;      // along (world units), across (-1..1), stripe period
                float4 colorA : COLOR0;
                float4 colorB : COLOR1;
            };

            uint FindHeader(uint segment)
            {
                uint lo = 0, hi = _HeaderCount - 1;
                while (lo < hi)
                {
                    uint mid = (lo + hi + 1) >> 1;
                    if (_Headers[mid].nodeOffset <= segment) lo = mid; else hi = mid - 1;
                }
                return lo;
            }

            v2f vert(uint vid : SV_VertexID)
            {
                // 6 vertices per segment: (k,-1) (k,+1) (k+1,-1) | (k+1,-1) (k,+1) (k+1,+1)
                uint segment = vid / 6;
                uint corner = vid % 6;
                ChainHeader h = _Headers[FindHeader(segment)];
                uint k = segment - h.nodeOffset;
                uint pt = k + ((corner == 2 || corner == 3 || corner == 5) ? 1 : 0);
                float side = (corner == 1 || corner == 4 || corner == 5) ? 1.0 : -1.0;

                float stepLen = h.nodeSpacing * h.nodeStride;
                float s = pt * stepLen;
                float2 p = SPFSampleChain(_Trail, h, _Alpha, s);
                float2 prev = SPFSampleChain(_Trail, h, _Alpha, max(s - stepLen, 0.0));
                float2 next = SPFSampleChain(_Trail, h, _Alpha, s + stepLen);
                float2 tangent = next - prev;
                tangent = dot(tangent, tangent) > 1e-10 ? normalize(tangent) : float2(1, 0);
                float2 normal = float2(-tangent.y, tangent.x);

                float t = h.nodeCount > 1 ? (float)pt / h.nodeCount : 0.0;
                float radius = h.radius * SPFTaper(t);
                float depth = (h.flags & SPF_FLAG_TRANSLUCENT) != 0 ? h.depth : h.depth + pt * 1e-4;

                v2f o;
                o.pos = UnityWorldToClipPos(float3(p + normal * radius * side, depth));
                o.uv = float3(s, side, max(h.stripe, 1u) * stepLen * (h.stripe > 0 ? 1.0 : 1e6));
                o.colorA = h.colorA;
                o.colorB = h.colorB;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                bool stripe = frac(i.uv.x / (2.0 * i.uv.z)) >= 0.5;
                float4 c = stripe ? i.colorB : i.colorA;
                float across = abs(i.uv.y);
                c.rgb *= 1.0 - across * across * 0.35;   // tube shading
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
