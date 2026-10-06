#if !SPF_DOTNET_HARNESS
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SPF.Presentation.Characters
{
    public static class BatGraphics
    {
        public static BatCapabilities Capabilities(Shader shader, Shader computeRenderShader=null, ComputeShader compute=null)
        {
            var api=SystemInfo.graphicsDeviceType;
            // GLES requires a separately validated non-SSBO implementation. CPU is the supported lower tier.
            bool supported=api==GraphicsDeviceType.Direct3D11||api==GraphicsDeviceType.Direct3D12||api==GraphicsDeviceType.Metal||api==GraphicsDeviceType.Vulkan||api==GraphicsDeviceType.OpenGLCore;
            bool computeKernel=false;
            if(supported&&SystemInfo.supportsComputeShaders&&compute!=null&&compute.HasKernel("BuildPalette"))
            {
                int kernel=compute.FindKernel("BuildPalette");
                compute.GetKernelThreadGroupSizes(kernel,out uint x,out uint y,out uint z);
                computeKernel=compute.IsSupported(kernel)&&x==BatLimits.ComputeGroupSize&&y==1&&z==1;
            }
            return new BatCapabilities { Graphics=api!=GraphicsDeviceType.Null,SupportedApi=supported,Shader=shader!=null&&shader.isSupported,
                Compute=SystemInfo.supportsComputeShaders,ComputeRenderShader=computeRenderShader!=null&&computeRenderShader.isSupported,ComputeKernel=computeKernel,
                ComputeBuffers=SystemInfo.maxComputeBufferInputsCompute,ComputeGroupSize=Math.Min(SystemInfo.maxComputeWorkGroupSize,SystemInfo.maxComputeWorkGroupSizeX),
                Instancing=SystemInfo.supportsInstancing,ShaderLevel=SystemInfo.graphicsShaderLevel,VertexBuffers=SystemInfo.maxComputeBufferInputsVertex,
                MaxTextureSize=SystemInfo.maxTextureSize,MaxBufferBytes=SystemInfo.maxGraphicsBufferSize,
                HalfSample=SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat,FormatUsage.Sample),FloatSample=SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat,FormatUsage.Sample) };
        }
        internal static void Destroy(UnityEngine.Object o)
        {
            if(o==null)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);
        }
        internal static Vector4 Vector(float4 v)=>new Vector4(v.x,v.y,v.z,v.w);
    }

    public sealed partial class BatClipSet
    {
        Mesh m_BindMesh;
        Texture2D m_HalfTexture,m_FloatTexture;
        internal Mesh BindMesh
        {
            get
            {
                CheckAlive();if(m_BindMesh!=null)return m_BindMesh;
                var positions=new Vector3[VertexCount];var uv=new Vector2[VertexCount];var skin=new List<Vector4>(VertexCount);var colors=new Color[VertexCount];
                for(int i=0;i<VertexCount;i++)
                {
                    var v=Vertices[i];positions[i]=new Vector3(v.Position.x,v.Position.y,0);uv[i]=new Vector2(v.Uv.x,v.Uv.y);
                    skin.Add(BatGraphics.Vector(v.Skin));colors[i]=new Color(v.Color.x,v.Color.y,v.Color.z,v.Color.w);
                }
                m_BindMesh=new Mesh { name="BAT original weighted bind mesh" };
                m_BindMesh.vertices=positions;m_BindMesh.uv=uv;m_BindMesh.colors=colors;m_BindMesh.SetUVs(1,skin);m_BindMesh.triangles=m_Indices;
                m_BindMesh.bounds=new Bounds(Vector3.zero,Vector3.one*(2*ModelRadius));
                return m_BindMesh;
            }
        }
        internal Texture2D Texture(BatPrecision precision)
        {
            CheckAlive();
            if(precision==BatPrecision.Half&& !HalfAccepted)throw new InvalidOperationException("Half precision exceeds this asset's pixel budget.");
            var existing=precision==BatPrecision.Half?m_HalfTexture:m_FloatTexture;if(existing!=null)return existing;
            int texels=checked(2*BatLimits.Bones*FrameCount),words=checked(texels*4);
            var texture=new Texture2D(2*BatLimits.Bones,FrameCount,precision==BatPrecision.Half?TextureFormat.RGBAHalf:TextureFormat.RGBAFloat,false,true)
                { name="Immutable BAT "+precision,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,anisoLevel=0 };
            if(precision==BatPrecision.Half)
            {
                var bits=new ushort[words];
                for(int row=0;row<FloatRows.Length;row++)for(int c=0;c<4;c++)
                {bits[row*8+c]=(ushort)math.f32tof16(FloatRows[row].Row0[c]);bits[row*8+4+c]=(ushort)math.f32tof16(FloatRows[row].Row1[c]);}
                texture.SetPixelData(bits,0);
            }
            else
            {
                var data=new float[words];for(int row=0;row<FloatRows.Length;row++)for(int c=0;c<4;c++){data[row*8+c]=FloatRows[row].Row0[c];data[row*8+4+c]=FloatRows[row].Row1[c];}
                texture.SetPixelData(data,0);
            }
            texture.Apply(false,true);
            if(precision==BatPrecision.Half)m_HalfTexture=texture;else m_FloatTexture=texture;
            return texture;
        }
        partial void ReleaseGraphics(){BatGraphics.Destroy(m_BindMesh);BatGraphics.Destroy(m_HalfTexture);BatGraphics.Destroy(m_FloatTexture);}
    }
}
#endif
