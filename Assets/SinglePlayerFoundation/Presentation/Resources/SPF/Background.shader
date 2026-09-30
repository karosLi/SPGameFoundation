// World-space background: soft hex-ish dot grid, darkened outside the region bounds with a glowing border.
Shader "SPF/Background"
{
    Properties
    {
        _ColorA ("Base", Color) = (0.07, 0.09, 0.13, 1)
        _ColorB ("Grid", Color) = (0.12, 0.15, 0.21, 1)
        _Outside ("Outside", Color) = (0.02, 0.02, 0.03, 1)
        _Border ("Border", Color) = (0.9, 0.25, 0.3, 1)
        _Region ("Region min.xy max.zw", Vector) = (-100, -100, 100, 100)
        _Cell ("Cell size", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" }
        Pass
        {
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _ColorA, _ColorB, _Outside, _Border, _Region;
            float _Cell;

            struct v2f { float4 pos : SV_POSITION; float2 world : TEXCOORD0; };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, vertex).xyz;
                o.pos = UnityWorldToClipPos(world);
                o.world = world.xy;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 cell = i.world / _Cell;
                float2 offset = float2(frac(floor(cell.y) * 0.5), 0);
                float2 f = frac(cell + offset) - 0.5;
                float dotMask = 1.0 - smoothstep(0.18, 0.24, length(f));
                float4 c = lerp(_ColorA, _ColorB, dotMask);

                float2 inside = min(i.world - _Region.xy, _Region.zw - i.world);
                float d = min(inside.x, inside.y);
                float border = 1.0 - smoothstep(0.0, 3.0, abs(d));
                c = d < 0 ? _Outside : c;
                return lerp(c, _Border, border * 0.8);
            }
            ENDCG
        }
    }
}
