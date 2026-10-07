using System;
using SPF.Presentation.Sprites;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
#if !SPF_DOTNET_HARNESS
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#endif

namespace SPF.Presentation.Particles
{
    /// <summary>Owns simulation and render resources. RenderTier chooses the CPU sprite output independently of simulation selection.</summary>
    public sealed class ParticleRenderer : IDisposable
    {
        public ParticlePool Pool { get; }
        public ParticleBackend Backend { get; private set; }
        public RenderTier Tier { get; }
        public long BytesUploaded { get; private set; }
        public int DrawCalls { get; private set; }
        public int DispatchCalls { get; private set; }
        public bool IsDisposed { get; private set; }
        public long SimulationBufferBytes => IsDisposed || Backend != ParticleBackend.GpuCompute ? 0 : (long)Pool.Capacity * ParticleState.Stride;
        readonly SpriteBatch m_CpuBatch;
        readonly Texture2D m_Atlas;
#if !SPF_DOTNET_HARNESS
        ComputeShader m_Compute;
        GraphicsBuffer m_States, m_Spawns, m_Sockets, m_Sprites, m_Args;
        Material m_Material;
        Mesh m_Quad;
        int m_Scatter, m_Step;
        bool m_Graphics;
        static readonly int StatesId=Shader.PropertyToID("_States"),SpawnsId=Shader.PropertyToID("_Spawns"),SocketsId=Shader.PropertyToID("_Sockets"),SpritesId=Shader.PropertyToID("_Sprites");
        static readonly int CountId=Shader.PropertyToID("_Count"),SpawnCountId=Shader.PropertyToID("_SpawnCount"),DtId=Shader.PropertyToID("_Dt"),ScaleId=Shader.PropertyToID("_DrawScale");
#endif
        public ParticleRenderer(RenderTier tier, bool lowQuality=false, bool forceCpu=false, bool disableComputeAsset=false)
        {
            Tier=tier; Pool=new ParticlePool(lowQuality); Backend=ParticleBackend.CpuBurst;
            try
            {
#if !SPF_DOTNET_HARNESS
                var compute=disableComputeAsset?null:Resources.Load<ComputeShader>("SPF/Particles/WeaponParticles");
                var shader=Resources.Load<Shader>("SPF/SpriteGPU");
                var capability=Capabilities(compute,shader); m_Graphics=capability.Graphics;
                // Compute simulation can be selected only when its SSBO draw route exists. A requested
                // lower renderer remains CPU even on desktop, making GLES fallback directly testable.
                Backend=capability.Select(forceCpu || tier!=RenderTier.GpuDriven,Pool.Capacity);
                if(!m_Graphics)return;
#endif
                m_Atlas=CreateAtlas();
#if !SPF_DOTNET_HARNESS
                if(Backend==ParticleBackend.GpuCompute)
                {
                    m_Compute=UnityEngine.Object.Instantiate(compute);m_Compute.name="SPF owned weapon particle simulation";
                    m_Scatter=m_Compute.FindKernel("Scatter");m_Step=m_Compute.FindKernel("Simulate");
                    m_States=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Pool.Capacity,ParticleState.Stride);
                    m_Spawns=new GraphicsBuffer(GraphicsBuffer.Target.Structured,ParticleLimits.SpawnsPerFrame,ParticleSpawn.Stride);
                    m_Sockets=new GraphicsBuffer(GraphicsBuffer.Target.Structured,ParticleLimits.Emitters,ParticleSocket.Stride);
                    m_Sprites=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Pool.Capacity,PackedSprite.Stride);
                    m_Args=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,1,GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    m_Args.SetData(new[]{new GraphicsBuffer.IndirectDrawIndexedArgs{indexCountPerInstance=6,instanceCount=(uint)Pool.Capacity}});
                    m_Material=RenderAssets.CreateMaterial(shader,BlendKind.Additive,false,3);
                    m_Material.name="SPF bounded weapon particles";m_Material.SetTexture("_MainTex",m_Atlas);m_Material.SetFloat("_Cutoff",0);m_Material.SetBuffer(SpritesId,m_Sprites);
                    m_Quad=new Mesh{name="SPF particle tight quad"};
                    m_Quad.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0)};
                    m_Quad.triangles=new[]{0,1,2,2,1,3};m_Quad.bounds=new Bounds(Vector3.zero,Vector3.one*100000);
                    m_Compute.SetBuffer(m_Scatter,StatesId,m_States);m_Compute.SetBuffer(m_Scatter,SpawnsId,m_Spawns);
                    m_Compute.SetBuffer(m_Step,StatesId,m_States);m_Compute.SetBuffer(m_Step,SocketsId,m_Sockets);m_Compute.SetBuffer(m_Step,SpritesId,m_Sprites);
                    m_Compute.SetInt(CountId,Pool.Capacity);m_States.SetData(Pool.CpuStates);
                }
                else
#endif
                {
                    m_CpuBatch=new SpriteBatch(tier,m_Atlas,BlendKind.Additive,Pool.Capacity,3);m_CpuBatch.Warmup(Pool.Capacity);
                }
            }
            catch { Dispose(); throw; }
        }
        public void Simulate()
        {
            Check();BytesUploaded=0;DrawCalls=0;DispatchCalls=0;
#if !SPF_DOTNET_HARNESS
            if(Backend==ParticleBackend.GpuCompute)
            {
                if(Pool.SpawnCount>0)
                {
                    m_Spawns.SetData(Pool.Spawns,0,0,Pool.SpawnCount);BytesUploaded+=(long)Pool.SpawnCount*ParticleSpawn.Stride;
                    m_Compute.SetInt(SpawnCountId,Pool.SpawnCount);m_Compute.Dispatch(m_Scatter,(Pool.SpawnCount+63)/64,1,1);DispatchCalls++;
                }
                m_Sockets.SetData(Pool.Sockets);BytesUploaded+=ParticleLimits.Emitters*ParticleSocket.Stride;
                m_Compute.SetFloat(DtId,Pool.DeltaTime);m_Compute.SetFloat(ScaleId,Pool.DrawScale);
                m_Compute.Dispatch(m_Step,(Pool.Capacity+63)/64,1,1);DispatchCalls++;return;
            }
#endif
            Pool.SimulateCpu();
            m_CpuBatch?.Clear();
            for(int i=0;i<Pool.Capacity;i++)
            {
                var p=Pool.CpuStates[i];if(!p.Alive)continue;
                p.Shape.xy*=Pool.DrawScale;
                var socket=p.Attachment.x==0?default:Pool.Sockets[(int)p.Attachment.x-1];
                if(m_CpuBatch!=null){var instances=m_CpuBatch.Instances;instances[m_CpuBatch.Count++]=ParticleMath.Sprite(p,socket);}
            }
        }
        public void Draw(Bounds bounds,int layer=0)
        {
            Check();DrawCalls=0;
#if !SPF_DOTNET_HARNESS
            if(!m_Graphics || Pool.ReservedCount==0)return;
            if(Backend==ParticleBackend.GpuCompute)
            {
                Graphics.RenderMeshIndirect(new RenderParams(m_Material){worldBounds=bounds,layer=layer,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false},m_Quad,m_Args);
                DrawCalls=1;return;
            }
#endif
            if(m_CpuBatch==null || m_CpuBatch.Count==0)return;
            m_CpuBatch.Draw(bounds,layer);BytesUploaded+=m_CpuBatch.BytesUploaded;DrawCalls=(m_CpuBatch.Count+SpriteBatch.PageSize-1)/SpriteBatch.PageSize;
        }
        public void Clear()
        {
            Check();Pool.Clear();m_CpuBatch?.Clear();
#if !SPF_DOTNET_HARNESS
            if(m_States!=null)m_States.SetData(Pool.CpuStates);
#endif
        }
#if !SPF_DOTNET_HARNESS
        /// <summary>Tests/tools only. Production simulation and draw never call GetData or request readback.</summary>
        public void ReadbackForValidation(ParticleState[] destination)
        {
            Check();if(destination==null || destination.Length<Pool.Capacity)throw new ArgumentException("Destination must fit the whole bounded pool.");
            if(Backend!=ParticleBackend.GpuCompute)throw new InvalidOperationException("Actual GPU backend is not active.");
            m_States.GetData(destination,0,0,Pool.Capacity);
        }
        public static ParticleCapabilities Capabilities(ComputeShader compute,Shader shader)
        {
            var api=SystemInfo.graphicsDeviceType;
            bool supported=api==GraphicsDeviceType.Metal || api==GraphicsDeviceType.Vulkan || api==GraphicsDeviceType.Direct3D11 || api==GraphicsDeviceType.Direct3D12 || api==GraphicsDeviceType.OpenGLCore;
            bool kernels=false;
            if(supported && SystemInfo.supportsComputeShaders && compute!=null && compute.HasKernel("Scatter") && compute.HasKernel("Simulate"))
            {
                kernels=true;
                foreach(string name in new[]{"Scatter","Simulate"})
                {
                    int kernel=compute.FindKernel(name);compute.GetKernelThreadGroupSizes(kernel,out uint x,out uint y,out uint z);
                    kernels &= compute.IsSupported(kernel) && x==ParticleLimits.GroupSize && y==1 && z==1;
                }
            }
            return new ParticleCapabilities {Compute=SystemInfo.supportsComputeShaders,Kernels=kernels,Graphics=api!=GraphicsDeviceType.Null,SupportedApi=supported,Shader=shader!=null&&shader.isSupported,
                Instancing=SystemInfo.supportsInstancing,AtlasFormat=SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm,FormatUsage.Sample),ShaderLevel=SystemInfo.graphicsShaderLevel,
                ComputeBuffers=SystemInfo.maxComputeBufferInputsCompute,VertexBuffers=SystemInfo.maxComputeBufferInputsVertex,GroupSize=Math.Min(SystemInfo.maxComputeWorkGroupSize,SystemInfo.maxComputeWorkGroupSizeX),MaxBufferBytes=SystemInfo.maxGraphicsBufferSize};
        }
#endif
        static Texture2D CreateAtlas()
        {
            // Four original, padded, tightly fitted glyphs, built once during loading. No asset download.
            var atlas=new Texture2D(128,32,TextureFormat.RGBA32,false,true){name="SPF original weapon particle atlas",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color32[128*32];
            for(int cell=0;cell<4;cell++)for(int y=1;y<31;y++)for(int x=1;x<31;x++)
            {
                float2 p=new float2((x-15.5f)/14f,(y-15.5f)/14f);float a;
                if(cell==0)a=math.saturate((1-math.abs(p.x)-math.abs(p.y)*1.2f)*4);
                else if(cell==1)a=math.saturate((1-math.abs(p.y)/(math.max(.02f,(1-p.x)*.35f)))*3)*math.saturate((1-math.abs(p.x))*8);
                else if(cell==2){float r=math.length(p);a=math.saturate(1-math.abs(r-.74f)/.10f);if(math.abs(p.x)<.16f || math.abs(p.y)<.16f)a*=.2f;}
                else {float r=math.length(p);a=math.saturate(1-r);a*=a;}
                pixels[y*128+cell*32+x]=new Color32(255,255,255,(byte)math.round(a*255));
            }
            atlas.SetPixels32(pixels);atlas.Apply(false,true);return atlas;
        }
        public void Dispose()
        {
            if(IsDisposed)return;IsDisposed=true;m_CpuBatch?.Dispose();Pool?.Dispose();
#if !SPF_DOTNET_HARNESS
            m_States?.Dispose();m_Spawns?.Dispose();m_Sockets?.Dispose();m_Sprites?.Dispose();m_Args?.Dispose();
            Destroy(m_Compute);Destroy(m_Material);Destroy(m_Quad);
#endif
            Destroy(m_Atlas);
        }
        static void Destroy(UnityEngine.Object value) { if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value); }
        void Check(){if(IsDisposed)throw new ObjectDisposedException(nameof(ParticleRenderer));}
    }
}
