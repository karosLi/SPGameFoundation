#if !SPF_DOTNET_HARNESS
using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Presentation.Characters
{
    [StructLayout(LayoutKind.Sequential,Pack=4)]
    public struct BatCpuVertex { public float3 Position; public float4 Color; public const int Stride=28; }

    [BurstCompile]
    public struct BatCpuSkinJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BatVertex> BindVertices;
        [ReadOnly] public NativeArray<BatRows> Palette;
        [ReadOnly] public NativeArray<BatInstance> Instances;
        public float4 Shape;
        [WriteOnly] public NativeArray<BatCpuVertex> Output;
        public void Execute(int index)
        {
            var v=BindVertices[index%BindVertices.Length];var i=Instances[index/BindVertices.Length];
            Output[index]=new BatCpuVertex { Position=BatSkinning.Skin(v,i,Palette,Shape),Color=v.Color*i.Tint };
        }
    }

    /// <summary>One bounded batch; warm allocations happen in constructor. Never owns the shared asset.</summary>
    public sealed class BatCharacterBatch : IDisposable
    {
        readonly BatClipSet m_Asset;
        NativeArray<BatInstance> m_Instances;
        NativeArray<BatCpuVertex> m_CpuVertices;
        readonly Material m_Material;
        readonly MaterialPropertyBlock m_Properties;
        readonly GraphicsBuffer m_Buffer;
        readonly Mesh[] m_Pages;
        int m_Count;bool m_Dirty=true,m_Disposed;
        readonly bool m_DrawSupported;
        Bounds m_Bounds;
        public int Capacity {get;}
        public int Count=>m_Count;
        public BatBackend Backend {get;}
        public BatPrecision Precision {get;}
        public long BytesUploaded {get;private set;}
        public long PaletteBytes => Backend==BatBackend.GpuVertex?2L*BatLimits.Bones*m_Asset.FrameCount*(Precision==BatPrecision.Half?8:16):0;
        public int DrawCalls {get;private set;}
        public Material Material=>m_Material;
        public Bounds WorldBounds=>m_Bounds;
        public BatCharacterBatch(BatClipSet asset,int capacity,bool forceCpu=false,bool allowHalf=true)
        {
            if(asset==null)throw new ArgumentNullException(nameof(asset));asset.CheckAlive();BatLimits.Capacity(capacity);
            m_Asset=asset;Capacity=capacity;
            var gpuShader=Resources.Load<Shader>("SPF/Characters/BatWeighted");
            var capabilities=BatGraphics.Capabilities(gpuShader);
            Backend=capabilities.Select(forceCpu,allowHalf&&asset.HalfAccepted,asset.FrameCount,capacity,out var precision);Precision=precision;
            m_Instances=new NativeArray<BatInstance>(capacity,Allocator.Persistent);
            m_DrawSupported=capabilities.Graphics;
            if(!m_DrawSupported)return;
            var shader=Backend==BatBackend.GpuVertex?gpuShader:Resources.Load<Shader>("SPF/Characters/BatCpu");
            if(shader==null||!shader.isSupported){m_Instances.Dispose();throw new NotSupportedException("BAT weighted fallback shader unavailable on this graphics backend.");}
            try
            {
            m_Material=new Material(shader){name="BAT "+Backend,enableInstancing=Backend==BatBackend.GpuVertex};
            m_Material.SetTexture("_MainTex",Texture2D.whiteTexture);
            m_Properties=new MaterialPropertyBlock();m_Properties.SetVector("_IkShape",BatGraphics.Vector(asset.IkShape));
            if(Backend==BatBackend.GpuVertex)
            {
                m_Buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,capacity,BatInstance.Stride);
                m_Properties.SetBuffer("_BatInstances",m_Buffer);m_Properties.SetTexture("_BoneTexture",asset.Texture(Precision));
                _=asset.BindMesh;
            }
            else
            {
                m_CpuVertices=new NativeArray<BatCpuVertex>(checked(capacity*asset.VertexCount),Allocator.Persistent);
                m_Pages=new Mesh[(capacity+BatLimits.CpuPageSize-1)/BatLimits.CpuPageSize];
                for(int p=0;p<m_Pages.Length;p++)m_Pages[p]=CreatePage(math.min(BatLimits.CpuPageSize,capacity-p*BatLimits.CpuPageSize));
            }
            }
            catch{Dispose();throw;}
        }
        public void Clear(){Check();m_Count=0;m_Dirty=true;}
        public bool Add(in BatInstance instance)
        {
            Check();BatLimits.Instance(instance,m_Asset.FrameCount);if(m_Count==Capacity)return false;
            m_Instances[m_Count++]=instance;m_Dirty=true;return true;
        }
        public void Set(int index,in BatInstance instance)
        {
            Check();if(index<0||index>=m_Count)throw new ArgumentOutOfRangeException(nameof(index));
            BatLimits.Instance(instance,m_Asset.FrameCount);m_Instances[index]=instance;m_Dirty=true;
        }
        public BatInstance Get(int index){Check();if(index<0||index>=m_Count)throw new ArgumentOutOfRangeException(nameof(index));return m_Instances[index];}
        public void Prepare()
        {
            Check();BytesUploaded=0;DrawCalls=0;if(m_Count==0||!m_Dirty||!m_DrawSupported)return;
            float3 low=new float3(float.MaxValue),high=new float3(float.MinValue);
            for(int i=0;i<m_Count;i++)
            {
                var data=m_Instances[i];float r=m_Asset.ModelRadius*data.Placement.z;
                float3 center=new float3(data.Placement.xy,data.Frames.w);
                low=math.min(low,center-new float3(r,r,.01f));high=math.max(high,center+new float3(r,r,.01f));
            }
            m_Bounds=new Bounds(new Vector3((low.x+high.x)*.5f,(low.y+high.y)*.5f,(low.z+high.z)*.5f),new Vector3(high.x-low.x,high.y-low.y,high.z-low.z));
            if(Backend==BatBackend.GpuVertex){m_Buffer.SetData(m_Instances,0,0,m_Count);BytesUploaded=(long)m_Count*BatInstance.Stride;}
            else
            {
                new BatCpuSkinJob { BindVertices=m_Asset.Vertices,Palette=Precision==BatPrecision.Half?m_Asset.HalfRows:m_Asset.FloatRows,Instances=m_Instances,Shape=m_Asset.IkShape,Output=m_CpuVertices }.Schedule(m_Count*m_Asset.VertexCount,64).Complete();
                for(int p=0;p<m_Pages.Length&&p*BatLimits.CpuPageSize<m_Count;p++)
                {
                    int n=math.min(BatLimits.CpuPageSize,m_Count-p*BatLimits.CpuPageSize);
                    var mesh=m_Pages[p];mesh.SetVertexBufferData(m_CpuVertices,p*BatLimits.CpuPageSize*m_Asset.VertexCount,0,n*m_Asset.VertexCount,0,MeshUpdateFlags.DontRecalculateBounds);
                    mesh.SetSubMesh(0,new SubMeshDescriptor(0,n*m_Asset.IndexCount,MeshTopology.Triangles){bounds=m_Bounds,vertexCount=n*m_Asset.VertexCount},MeshUpdateFlags.DontRecalculateBounds);
                    mesh.bounds=m_Bounds;
                }
                BytesUploaded=(long)m_Count*m_Asset.VertexCount*BatCpuVertex.Stride;
            }
            m_Dirty=false;
        }
        public void Draw(Camera camera=null)
        {
            Prepare();if(m_Count==0||!m_DrawSupported)return;
            if(Precision==BatPrecision.Half&&(camera==null||!camera.orthographic||camera.pixelHeight/(2f*camera.orthographicSize)>BatLimits.MaxPixelsPerUnit))
                throw new InvalidOperationException("Half BAT requires an explicit orthographic camera within 256 pixels/world-unit; construct with allowHalf:false outside that validated envelope.");
            if(Backend==BatBackend.GpuVertex)
            {
                var rp=new RenderParams(m_Material){camera=camera,worldBounds=m_Bounds,matProps=m_Properties,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false,lightProbeUsage=LightProbeUsage.Off};
                Graphics.RenderMeshPrimitives(in rp,m_Asset.BindMesh,0,m_Count);DrawCalls=1;
            }
            else for(int p=0;p<m_Pages.Length&&p*BatLimits.CpuPageSize<m_Count;p++)
            {Graphics.DrawMesh(m_Pages[p],Matrix4x4.identity,m_Material,0,camera,0,null,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);DrawCalls++;}
        }
        /// <summary>Deterministic validation only; same production mesh/material/data. Caller chooses target and camera matrices.</summary>
        public void Record(CommandBuffer commands)
        {
            Prepare();if(m_Count==0||!m_DrawSupported)return;
            if(Backend==BatBackend.GpuVertex){commands.DrawMeshInstancedProcedural(m_Asset.BindMesh,0,m_Material,0,m_Count,m_Properties);DrawCalls=1;}
            else for(int p=0;p<m_Pages.Length&&p*BatLimits.CpuPageSize<m_Count;p++){commands.DrawMesh(m_Pages[p],Matrix4x4.identity,m_Material,0,0);DrawCalls++;}
        }
        /// <summary>Test hook reuses actual uploaded ABI and palette; performs no readback during normal frames.</summary>
        public void RecordProbe(CommandBuffer commands,Mesh probe,Material probeMaterial)
        {
            Prepare();if(Backend!=BatBackend.GpuVertex||m_Count!=1)throw new InvalidOperationException("Probe requires exactly one GPU instance.");
            commands.DrawMeshInstancedProcedural(probe,0,probeMaterial,0,1,m_Properties);
        }
        Mesh CreatePage(int count)
        {
            int vertexCount=checked(count*m_Asset.VertexCount);
            var mesh=new Mesh{name="BAT CPU weighted page"};mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertexCount,new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.Float32,4,0),new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,1));
            var uv=new Vector2[vertexCount];var indices=new ushort[checked(count*m_Asset.IndexCount)];
            for(int c=0;c<count;c++)
            {
                for(int v=0;v<m_Asset.VertexCount;v++){var source=m_Asset.Vertex(v);uv[c*m_Asset.VertexCount+v]=new Vector2(source.Uv.x,source.Uv.y);}
                for(int i=0;i<m_Asset.IndexCount;i++)indices[c*m_Asset.IndexCount+i]=checked((ushort)(c*m_Asset.VertexCount+m_Asset.Index(i)));
            }
            mesh.SetVertexBufferData(uv,0,0,vertexCount,1);mesh.SetIndexBufferParams(indices.Length,IndexFormat.UInt16);mesh.SetIndexBufferData(indices,0,0,indices.Length);
            mesh.subMeshCount=1;mesh.SetSubMesh(0,new SubMeshDescriptor(0,0));return mesh;
        }
        void Check(){if(m_Disposed)throw new ObjectDisposedException(nameof(BatCharacterBatch));m_Asset.CheckAlive();}
        public void Dispose()
        {
            if(m_Disposed)return;m_Disposed=true;if(m_Instances.IsCreated)m_Instances.Dispose();if(m_CpuVertices.IsCreated)m_CpuVertices.Dispose();m_Buffer?.Dispose();
            if(m_Pages!=null)foreach(var mesh in m_Pages)BatGraphics.Destroy(mesh);BatGraphics.Destroy(m_Material);
        }
    }
}
#endif
