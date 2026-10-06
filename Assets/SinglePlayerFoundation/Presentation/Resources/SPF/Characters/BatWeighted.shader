Shader "SPF/Characters/BatWeighted"
{
 Properties { _MainTex("Smooth character atlas",2D)="white"{} _Cutoff("Alpha cutoff",Range(0,1))=0.01 }
 SubShader
 {
  Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
  Pass
  {
   Cull Off ZWrite On ZTest LEqual Blend Off
   HLSLPROGRAM
   #pragma target 4.5
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "BatSkinning.hlsl"
   sampler2D _MainTex; float _Cutoff;
   struct Input { float3 vertex:POSITION; float2 uv:TEXCOORD0; float4 skin:TEXCOORD1; float4 color:COLOR; uint instance:SV_InstanceID; };
   struct Varying { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
   Varying vert(Input input)
   {
    BatInstance i = _BatInstances[input.instance];
    float3 world = BatSkin(input.vertex.xy,input.skin,i);
    Varying output; output.pos=mul(UNITY_MATRIX_VP,float4(world,1));
    output.uv=input.uv; output.color=input.color*i.tint; return output;
   }
   float4 frag(Varying input):SV_Target
   {
    float4 color=tex2D(_MainTex,input.uv)*input.color; clip(color.a-_Cutoff); return color;
   }
   ENDHLSL
  }
 }
}
