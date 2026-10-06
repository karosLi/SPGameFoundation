// Test-only: each quad occupies exactly one pixel in an RGBAFloat target.
// uv2/TEXCOORD2 holds predetermined clip x/y at each quad corner. All four corners
// carry the SAME bind point/weights, so returned color is an exact vertex-stage probe.
Shader "Hidden/SPF/Characters/BatComputedProbe"
{
 SubShader { Pass {
  Cull Off ZWrite Off ZTest Always Blend Off
  HLSLPROGRAM
  #pragma target 4.5
  #pragma vertex vert
  #pragma fragment frag
  #include "UnityCG.cginc"
  #include "BatComputedSkinning.hlsl"
  struct Input { float3 vertex:POSITION; float4 skin:TEXCOORD1; float2 probeClip:TEXCOORD2; uint instance:SV_InstanceID; };
  struct Varying { float4 pos:SV_POSITION; float3 measured:TEXCOORD0; };
  Varying vert(Input input)
  {
   Varying o; o.pos=float4(input.probeClip,0,1);
   o.measured=BatSkinComputed(input.vertex.xy,input.skin,_BatInstances[input.instance],input.instance); return o;
  }
  float4 frag(Varying i):SV_Target { return float4(i.measured,1); }
  ENDHLSL
 } }
}
