// Unlit vertex colour (straight alpha), render state from material properties: tile meshes (opaque) and
// world-space overlays such as health bars (alpha blended, drawn over everything).
Shader "SPF/VertexColor"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
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

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
