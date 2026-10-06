Shader "SPF/Characters/BatCpu"
{
 Properties { _MainTex("Character atlas",2D)="white"{} _Cutoff("Alpha cutoff",Range(0,1))=0.01 }
 SubShader { Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
 Pass { Cull Off ZWrite On ZTest LEqual Blend Off
 CGPROGRAM
 #pragma target 2.0
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _MainTex; float _Cutoff;
 struct Input { float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; };
 struct Varying { float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; };
 Varying vert(Input i){Varying o;o.pos=mul(UNITY_MATRIX_VP,i.vertex);o.uv=i.uv;o.color=i.color;return o;}
 float4 frag(Varying i):SV_Target {float4 c=tex2D(_MainTex,i.uv)*i.color;clip(c.a-_Cutoff);return c;}
 ENDCG
 } }
}
