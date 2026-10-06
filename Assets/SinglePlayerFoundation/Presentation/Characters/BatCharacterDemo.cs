#if !SPF_DOTNET_HARNESS
using SPF.L1.Skeleton;
using SPF.Presentation.Sprites;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Characters
{
    /// <summary>Standalone inspectable weighted-character scene. Presentation clocks never mutate simulation/snapshots.</summary>
    public sealed class BatCharacterDemo : MonoBehaviour
    {
        [Range(1,256)] public int ActorCount=64;
        public bool ForceCpu;
        public bool EnableIk=true;
        public bool ContactShadows=true;
        public float BendSign=1;
        public Vector2 ModelTarget=new Vector2(1,1.8f);
        public Camera OutputCamera;
        public BatInstance ReadInstance(int index)=>m_Batch.Get(index);
        public BatBackend ActiveBackend=>m_Batch!=null?m_Batch.Backend:BatBackend.CpuWeighted;
        BatClipSet m_Asset;
        BatCharacterBatch m_Batch;
        SpriteBatch m_Shadows,m_Markers;
        SpriteSheet m_Sheet;
        SkeletonAsset m_Rig;
        NativeArray<BoneLocal> m_Pose;
        NativeArray<BoneWorld> m_World;
        Camera m_Camera;
        GameObject m_CameraObject;
        bool m_Forced;
        int m_Shadow,m_Dot;
        void OnEnable()
        {
            m_Asset=BatCharacterAsset.Bake();m_Batch=new BatCharacterBatch(m_Asset,BatLimits.MaxCapacity,ForceCpu);m_Forced=ForceCpu;
            m_Rig=BatCharacterAsset.CreateRig();m_Pose=new NativeArray<BoneLocal>(3,Allocator.Persistent);m_World=new NativeArray<BoneWorld>(3,Allocator.Persistent);
            var atlas=new SpriteAtlasBuilder();m_Shadow=atlas.Add(BlobShadow.CreateCanvas());
            var dot=new PixelCanvas(16,16);for(int y=0;y<16;y++)for(int x=0;x<16;x++){float2 d=new float2(x-7.5f,y-7.5f);dot.Pixels[y*16+x]=new Color32(255,255,255,(byte)(math.lengthsq(d)<49?255:0));}
            m_Dot=atlas.Add(dot);m_Sheet=atlas.Build(256,FilterMode.Bilinear,2,true);
            // Contact shadows use the existing independently gated lower tier, never the BAT capability as proof.
            m_Shadows=new SpriteBatch(RenderTier.DataTexture,m_Sheet.Texture,BlendKind.Translucent,256);
            m_Markers=new SpriteBatch(RenderTier.DataTexture,m_Sheet.Texture,BlendKind.Opaque,2);
            m_Shadows.Warmup(256);m_Markers.Warmup(2);
            m_Camera=OutputCamera!=null?OutputCamera:Camera.main;
            if(m_Camera==null){m_CameraObject=new GameObject("BAT Validation Camera");m_Camera=m_CameraObject.AddComponent<Camera>();m_CameraObject.tag="MainCamera";}
            m_Camera.orthographic=true;m_Camera.clearFlags=CameraClearFlags.SolidColor;m_Camera.backgroundColor=new Color(.055f,.08f,.12f);
            Debug.Log("BAT character validation: "+m_Batch.Backend+" / "+m_Batch.Precision+". Half max error "+m_Asset.HalfMaxPixelError+" px at 256 px/unit x4 scale. C toggles CPU; I toggles IK; 1/2/3 select 1/64/256 actors. Pointer controls first actor target; gold=target, green=CPU reference tip.");
        }
        void Update()
        {
            if(Input.GetKeyDown(KeyCode.C))ForceCpu=!ForceCpu;if(Input.GetKeyDown(KeyCode.I))EnableIk=!EnableIk;
            if(Input.GetKeyDown(KeyCode.Alpha1))ActorCount=1;if(Input.GetKeyDown(KeyCode.Alpha2))ActorCount=64;if(Input.GetKeyDown(KeyCode.Alpha3))ActorCount=256;
            if(m_Forced!=ForceCpu){m_Batch.Dispose();m_Batch=new BatCharacterBatch(m_Asset,BatLimits.MaxCapacity,ForceCpu);m_Forced=ForceCpu;}
            int count=math.clamp(ActorCount,1,256),columns=count==1?1:count<=64?8:16,rows=(count+columns-1)/columns;
            m_Camera.orthographicSize=math.max(2,math.max((rows*2.4f+1)*.5f,(columns*2.5f+1)*.5f/math.max(.1f,m_Camera.aspect)));m_Camera.transform.position=new Vector3((columns-1)*1.25f,(rows-1)*1.2f+.9f,-10);
            if(Input.GetMouseButton(0)){var p=m_Camera.ScreenToWorldPoint(Input.mousePosition);ModelTarget=new Vector2(p.x,p.y);}
            float2 target=math.clamp(new float2(ModelTarget.x,ModelTarget.y),-1000,1000);
            float bend=BendSign<0?-1:1;
            m_Batch.Clear();m_Shadows.Clear();m_Markers.Clear();
            for(int i=0;i<count;i++)
            {
                float2 position=new float2(i%columns*2.5f,i/columns*2.4f);float facing=(i&1)==0?1:-1;
                var tint=(i%3) switch{0=>new float4(1,1,1,1),1=>new float4(.65f,1,.85f,1),_=>new float4(.8f,.75f,1,1)};
                var instance=m_Asset.Instance(i%2,Time.time+i*.113f,position,1,facing,tint,0,target,EnableIk&&i==0,bend);
                m_Batch.Add(instance);
                if(ContactShadows){var profile=BlobShadowProfile.Default;profile.Size=new float2(.95f,.28f);BlobShadow.Add(m_Shadows,m_Sheet[m_Shadow].Uv,position,.2f,profile);}
            }
            // Independent source-pose CPU reference probe; this presentation demo does not govern gameplay. No runtime GPU readback.
            Skeletal.Sample(m_Rig.View,0,Time.time,m_Pose);
            if(EnableIk)Skeletal.TwoBoneIK(m_Rig.View,m_Pose,1,2,target,bend);
            Skeletal.ToWorld(m_Rig.View,m_Pose,0,1,1,m_World);
            float2 tip=m_World[2].Transform(new float2(.5f,0),1);
            m_Markers.Add(target,new float2(.11f),m_Sheet[m_Dot].Uv,-.2f,new float4(1,.7f,.1f,1));
            m_Markers.Add(tip,new float2(.07f),m_Sheet[m_Dot].Uv,-.3f,new float4(.2f,1,.55f,1));
            var bounds=new Bounds(new Vector3(columns,rows,0),new Vector3(200,200,2));
            m_Shadows.Draw(bounds);m_Batch.Draw(m_Camera);m_Markers.Draw(bounds);
        }
        void OnDisable()
        {
            m_Batch?.Dispose();m_Asset?.Dispose();m_Shadows?.Dispose();m_Markers?.Dispose();m_Sheet?.Dispose();m_Rig?.Dispose();
            if(m_Pose.IsCreated)m_Pose.Dispose();if(m_World.IsCreated)m_World.Dispose();
            if(m_CameraObject!=null)BatGraphics.Destroy(m_CameraObject);
        }
    }
}
#endif
