#if !SPF_DOTNET_HARNESS
using SPF.L1.Skeleton;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SPF.MotionValidation
{
    /// <summary>Bounded presentation laboratory: 8 cutout actors, 112 sprites, one pose job. No simulation writes.</summary>
    public sealed class NaturalMotionDemo : MonoBehaviour
    {
        public const int ActorCount=8;
        // Authored boot sole: ankle .075 + part offset -.005 + (15/96 - .5) * .25.
        // Keep visual surface contact separate from the ankle anchor used by the IK solver.
        const float SoleModelY=-.0159375f;
        const float ForegroundRootY=.415f-SoleModelY*1.42f;
        const float LibraryRootY=5.2625f-SoleModelY*.54f;
        public bool ForceDataTexture, Shading=true, Playback=true, ShowMarkers=true;
        public bool InteractiveAimWhilePaused=true;
        public PoseShadowQuality ShadowQuality=PoseShadowQuality.BakedPose;
        public Camera OutputCamera;
        public Vector2 AimOffset=new Vector2(0,0);
        public bool UnreachableTarget;
        public RenderTier ActiveTier {get;private set;}
        public int BakedShadows {get;private set;}
        public int BlobShadows {get;private set;}
        public float PresentationTime=>m_Time;
        public int ShadowAtlasBytes=>m_ShadowAtlas?.TextureBytes??0;
        public FootPlantState ReadFoot(int actor,bool near)=>near?m_Feet[actor*2+1]:m_Feet[actor*2];
        public NaturalPoseInput ReadActor(int actor)=>m_Input[actor];
        public BoneWorld ReadBone(int actor,int bone)=>m_World[actor*NaturalCharacterRig.Bones+bone];
        NaturalCharacterArt m_Art;
        SkeletonAsset m_Rig;
        PoseSilhouetteShadow m_ShadowAtlas;
        NativeArray<BoneLocal> m_Local;
        NativeArray<BoneWorld> m_World;
        NativeArray<NaturalPoseInput> m_Input;
        NativeArray<int> m_PoseTicks;
        NativeArray<BoneAttachment> m_Attachments;
        readonly FootPlantState[] m_Feet=new FootPlantState[ActorCount*2];
        readonly SmoothedAimState[] m_Aim=new SmoothedAimState[ActorCount];
        SpriteBatch m_Actors,m_Set,m_Shadow,m_Blob,m_Effects;
        Texture2D m_Normals;
        Camera m_Camera;
        GameObject m_CameraObject,m_Ui,m_EventSystem;
        Text m_Status;
        RectTransform m_UiLayout;
        CanvasScaler m_UiScaler;
        float m_Time;
        bool m_LastForce,m_LastShading;
        PoseShadowQuality m_LastQuality;
        readonly Bounds m_Bounds=new Bounds(new Vector3(0,2,0),new Vector3(40,40,8));
        void OnEnable()
        {
            m_Art=new NaturalCharacterArt();m_Rig=NaturalCharacterRig.Create();
            m_Local=new NativeArray<BoneLocal>(ActorCount*NaturalCharacterRig.Bones,Allocator.Persistent);
            m_World=new NativeArray<BoneWorld>(ActorCount*NaturalCharacterRig.Bones,Allocator.Persistent);
            m_Input=new NativeArray<NaturalPoseInput>(ActorCount,Allocator.Persistent);
            m_PoseTicks=new NativeArray<int>(ActorCount,Allocator.Persistent);
            m_Attachments=new NativeArray<BoneAttachment>(NaturalCharacterArt.Parts*2,Allocator.Persistent);
            for(int k=0;k<2;k++)for(int a=0;a<NaturalCharacterArt.Parts;a++)m_Attachments[k*NaturalCharacterArt.Parts+a]=m_Art.Attachments[k][a];
            BakeShadows();m_Normals=NormalMapBaker.Bake(m_Art.Sheet,new NormalMapBaker.Settings{Bevel=4,Strength=.75f,Detail=.12f});m_Normals.filterMode=FilterMode.Bilinear;
            m_Camera=OutputCamera!=null?OutputCamera:Camera.main;
            if(m_Camera==null){m_CameraObject=new GameObject("Natural Motion Camera");m_Camera=m_CameraObject.AddComponent<Camera>();m_CameraObject.tag="MainCamera";}
            m_Camera.orthographic=true;m_Camera.clearFlags=CameraClearFlags.SolidColor;m_Camera.backgroundColor=new Color(.035f,.066f,.09f);
            CreateBatches();CreateUi();SetPreviewTime(0);
        }
        void BakeShadows()
        {
            var samples=new PoseShadowSample[PoseSilhouetteShadow.MaxSamples];int n=0;
            for(int kind=0;kind<2;kind++)for(int mirror=0;mirror<2;mirror++)for(int phase=0;phase<8;phase++)
            {
                float p=phase/8f,facing=mirror==0?1:-1;
                float2 far=NaturalMotion.CanonicalFoot(p,.5f),near=NaturalMotion.CanonicalFoot(p,0);far.x*=facing;near.x*=facing;
                NaturalCharacterRig.Pose(m_Rig.View,m_Local,m_World,0,facing,1,p,far,near,false,0,0);
                var bones=new BoneWorld[NaturalCharacterRig.Bones];for(int b=0;b<bones.Length;b++)bones[b]=m_World[b];
                var parts=new PixelCanvas[NaturalCharacterArt.Parts];for(int a=0;a<parts.Length;a++)parts[a]=m_Art.Canvases[kind][NaturalCharacterArt.CanvasForAttachment(a)];
                samples[n++]=new PoseShadowSample{Kind=kind,Facing=facing,Bones=bones,Attachments=m_Art.Attachments[kind],Parts=parts};
            }
            m_ShadowAtlas=new PoseSilhouetteShadow(samples);
        }
        void CreateBatches()
        {
            m_Actors?.Dispose();m_Set?.Dispose();m_Shadow?.Dispose();m_Blob?.Dispose();m_Effects?.Dispose();
            ActiveTier=ForceDataTexture?RenderTier.DataTexture:RenderCapabilities.Detect();m_LastForce=ForceDataTexture;
            m_Actors=new SpriteBatch(ActiveTier,m_Art.Sheet.Texture,BlendKind.Translucent,ActorCount*NaturalCharacterArt.Parts,0);
            m_Set=new SpriteBatch(ActiveTier,m_Art.Sheet.Texture,BlendKind.Opaque,128);
            m_Shadow=new SpriteBatch(ActiveTier,m_ShadowAtlas.Sheet.Texture,BlendKind.Translucent,ActorCount,-20);
            m_Blob=new SpriteBatch(ActiveTier,m_Art.Sheet.Texture,BlendKind.Translucent,ActorCount,-20);
            m_Effects=new SpriteBatch(ActiveTier,m_Art.Sheet.Texture,BlendKind.Additive,64,20);
            m_Actors.Warmup(m_Actors.Capacity);m_Set.Warmup(128);m_Shadow.Warmup(ActorCount);m_Blob.Warmup(ActorCount);m_Effects.Warmup(64);
            for(int i=0;i<ActorCount;i++)m_PoseTicks[i]=-1;
            m_LastShading=!Shading;BuildSet();RefreshStatus();
        }
        void BuildSet()
        {
            m_Set.Clear();
            Rect(0,2.5f,18,10,.18f,new float4(.052f,.101f,.13f,1));
            // Architectural silhouettes are intentionally quiet behind the articulation.
            for(int i=0;i<11;i++)
            {
                float x=-8+i*1.6f;Rect(x,3.5f,.72f,7,.14f,new float4(.066f,.133f,.157f,1));
                Rect(x+.2f,3.5f,.12f,6.8f,.13f,new float4(.08f,.157f,.175f,1));
            }
            Rect(0,-.66f,18,2,.09f,new float4(.062f,.092f,.12f,1));
            Rect(0,.35f,18,.13f,.08f,new float4(.33f,.42f,.43f,1));
            for(int i=0;i<4;i++)
            {
                float x=-6+i*4;Rect(x,-.06f,3.55f,.73f,.075f,new float4(.11f,.17f,.20f,1));
                Rect(x,.29f,3.55f,.055f,.07f,new float4(.61f,.56f,.41f,1));
                Rect(x,-.20f,2.95f,.022f,.065f,new float4(.25f,.32f,.33f,1));
            }
            Rect(0,4.46f,15.8f,.022f,.10f,new float4(.2f,.39f,.4f,1));
            for(int i=0;i<4;i++) {Rect(-5.6f+i*3.7f,5.24f,2.8f,.045f,.07f,new float4(.27f,.42f,.4f,1));}
            // Fixed grid marks make foot sliding visible.
            for(int i=0;i<65;i++)Rect(-8+i*.25f,.4f,.012f,i%4==0?.16f:.065f,.065f,new float4(.45f,.51f,.48f,1));
        }
        void Rect(float x,float y,float w,float h,float z,float4 color)=>m_Set.Add(new float2(x,y),new float2(w,h),m_Art.Sheet[m_Art.White].Uv,z,color);
        public void SetPreviewTime(float time)
        {
            m_Time=0;
            for(int i=0;i<ActorCount;i++)m_PoseTicks[i]=-1;
            for(int i=0;i<m_Feet.Length;i++)m_Feet[i]=default;
            for(int i=0;i<m_Aim.Length;i++)m_Aim[i]=default;
            FillInputs(0);
            float target=math.clamp(time,0,30);
            while(m_Time<target-.00001f){float dt=math.min(1f/120f,target-m_Time);m_Time+=dt;FillInputs(dt);}
            Evaluate();
        }
        void FillInputs(float dt, float aimDt = -1)
        {
            for(int i=0;i<ActorCount;i++)
            {
                bool thumbnail=i>=4;int kind=i&1;float scale=thumbnail?.54f:1.42f;
                float facing=i==1||i==3||i==6?-1:1;
                float phase=thumbnail?math.floor(m_Time*15)/15*.18f+i*.125f:m_Time/NaturalMotion.Period+i*.5f;
                float2 root=new float2(thumbnail?-5.6f+(i-4)*3.7f:-6+i*4,thumbnail?LibraryRootY:ForegroundRootY);
                if(i<2)root.x+=.52f*math.sin(m_Time*.95f+(i==0?0:math.PI));
                float velocity=i<2?.52f*.95f*math.cos(m_Time*.95f+(i==0?0:math.PI)):0;
                if(i<2)facing=velocity<0?-1:1;
                float2 far,near;
                if(thumbnail)
                {
                    // Fixed library pose with 15 Hz torso/head breathing. Feet remain exact world anchors.
                    float posePhase=(i-4)*.25f;
                    far=NaturalMotion.CanonicalFoot(posePhase,.5f);near=NaturalMotion.CanonicalFoot(posePhase,0);
                    far=root+far*new float2(scale*facing,scale);near=root+near*new float2(scale*facing,scale);
                    phase=posePhase+.045f*math.sin(math.floor(m_Time*15)/15*1.6f);
                }
                else if(i<2)
                {
                    int f=i*2;
                    if(!m_Feet[f].Initialized)
                    {
                        NaturalMotion.InitializeFoot(ref m_Feet[f],root,root+new float2(-.14f,.075f)*scale,0);
                        NaturalMotion.InitializeFoot(ref m_Feet[f+1],root,root+new float2(.14f,.075f)*scale,.5f);
                    }
                    NaturalMotion.StepFoot(ref m_Feet[f],root,new float2(velocity,0),-.1f*scale,dt,root.y+.075f*scale);
                    NaturalMotion.StepFoot(ref m_Feet[f+1],root,new float2(velocity,0),.1f*scale,dt,root.y+.075f*scale);
                    far=m_Feet[f].Position;near=m_Feet[f+1].Position;
                    phase=m_Feet[f+1].Phase;
                }
                else {far=root+new float2(-.24f,.075f)*scale;near=root+new float2(.26f,.075f)*scale;}
                float strike=i>=2&&!thumbnail?NaturalCharacterRig.Strike(m_Time+(i==3?.14f:0)):0;
                float2 modelTarget=new float2(.50f+strike*.32f,1.7f+.15f*math.sin(m_Time*1.5f));
                if(UnreachableTarget)modelTarget.x+=2;
                modelTarget+=new float2(AimOffset.x,AimOffset.y);
                float2 target=root+modelTarget*new float2(scale*facing,scale);
                target=NaturalMotion.SmoothAim(ref m_Aim[i],target,aimDt < 0 ? dt : aimDt);
                m_Input[i]=new NaturalPoseInput{Root=root,Facing=facing,Scale=scale,Kind=kind,Phase=phase,FarFoot=far,NearFoot=near,Aim=i>=2&&!thumbnail?1:0,AimTarget=target,Attack=math.max(0,strike)*.4f};
            }
        }
        void Evaluate()
        {
            m_Actors.Count=ActorCount*NaturalCharacterArt.Parts;
            new NaturalPoseJob{Rig=m_Rig.View,Actors=m_Input,Attachments=m_Attachments,Local=m_Local,World=m_World,Sprites=m_Actors.Instances,PoseTicks=m_PoseTicks,LodTick=(int)(m_Time*15),FullRateActors=4}.Schedule(ActorCount,4).Complete();
        }
        void Update()
        {
            if(Input.GetKeyDown(KeyCode.Space))Playback=!Playback;
            if(Input.GetKeyDown(KeyCode.T))ForceDataTexture=!ForceDataTexture;
            if(Input.GetKeyDown(KeyCode.L))Shading=!Shading;
            if(Input.GetKeyDown(KeyCode.Q))CycleQuality();
            if(Input.GetKeyDown(KeyCode.U))UnreachableTarget=!UnreachableTarget;
            if(m_LastForce!=ForceDataTexture)CreateBatches();
            // Tap-drag in the lower half adjusts only the two presentation arm targets.
            bool overUi=EventSystem.current!=null&&(EventSystem.current.IsPointerOverGameObject()||(Input.touchCount>0&&EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)));
            if(Input.GetMouseButton(0)&&!overUi)
            {var p=m_Camera.ScreenToWorldPoint(Input.mousePosition);if(p.y>ForegroundRootY&&p.y<4.3f)AimOffset=new Vector2(math.clamp((p.x-2)/1.42f-.8f,-1,2),math.clamp((p.y-ForegroundRootY)/1.42f-1.7f,-.8f,.8f));}
            float dt=math.min(Time.deltaTime,.1f);
            if(Playback)m_Time+=dt;
            // Pause stops the gait clock; live target controls can still settle smoothly.
            FillInputs(Playback?dt:0,Playback||InteractiveAimWhilePaused?dt:0);
            Evaluate();
            m_Camera.orthographicSize=NaturalMotion.FitOrthographic(18,10.2f,m_Camera.aspect);m_Camera.transform.position=new Vector3(0,3.35f,-10);
            m_UiScaler.matchWidthOrHeight=m_Camera.aspect>=16f/9f?1:0;
            if(m_LastShading!=Shading){m_Actors.SetLighting(Shading?m_Normals:null);m_LastShading=Shading;RefreshStatus();}
            if(m_LastQuality!=ShadowQuality){m_LastQuality=ShadowQuality;RefreshStatus();}
            SpriteLighting.Begin(new Color(.77f,.84f,.86f));SpriteLighting.Add(new float2(-4,4),11,new Color(.65f,.86f,1),.75f,3);
            SpriteLighting.Add(new float2(6,3),7,new Color(1,.56f,.29f),.5f,2);SpriteLighting.Apply();
            DrawShadows();DrawEffects();m_Set.Draw(m_Bounds,dirty:false);m_Shadow.Draw(m_Bounds);m_Blob.Draw(m_Bounds);m_Actors.Draw(m_Bounds);m_Effects.Draw(m_Bounds);
        }
        void DrawShadows()
        {
            m_Shadow.Clear();m_Blob.Clear();BakedShadows=BlobShadows=0;
            if(ShadowQuality==PoseShadowQuality.None)return;
            for(int i=0;i<ActorCount;i++)
            {
                var input=m_Input[i];
                // Statefully planted feet are outside the straight-line library contract. Show honest blobs.
                bool dynamic=i<4;
                if(m_ShadowAtlas.TrySelect(ShadowQuality,input.Kind,m_World,i*NaturalCharacterRig.Bones,input.Root,input.Facing,input.Scale,dynamic,out var frame))
                {m_Shadow.Add(input.Root+frame.Center*input.Scale,frame.Size*input.Scale,m_ShadowAtlas.Sheet[frame.Index].Uv,.045f,new float4(.005f,.018f,.025f,.53f));BakedShadows++;}
                else
                {var profile=BlobShadowProfile.Default;profile.Size=new float2(1.14f,.27f);profile.Offset=new float2(.05f,.005f);profile.Color=new float4(.005f,.016f,.024f,.52f);BlobShadow.Add(m_Blob,m_Art.Sheet[m_Art.Blob].Uv,input.Root,.045f,profile,0,input.Scale);BlobShadows++;}
            }
        }
        void DrawEffects()
        {
            m_Effects.Clear();
            if(ShowMarkers)for(int i=0;i<2;i++)for(int f=0;f<2;f++)
            {
                var foot=m_Feet[i*2+f];float4 color=foot.InStance?new float4(.24f,.87f,.68f,.6f):new float4(1,.61f,.18f,.6f);
                m_Effects.Add(foot.Position+new float2(0,(SoleModelY-.075f)*m_Input[i].Scale),new float2(.18f,.022f),m_Art.Sheet[m_Art.White].Uv,-.12f,color);
            }
            for(int i=2;i<4;i++)
            {
                var a=m_Input[i];float2 tip=m_World[i*NaturalCharacterRig.Bones+NaturalCharacterRig.NearForearm].Transform(new float2(.42f*a.Scale,0),a.Facing);
                if(ShowMarkers)
                {
                    float2 t=a.AimTarget;var uv=m_Art.Sheet[m_Art.White].Uv;var color=new float4(.95f,.71f,.3f,.7f);
                    m_Effects.Add(t,new float2(.17f,.022f),uv,-.12f,color);m_Effects.Add(t,new float2(.022f,.17f),uv,-.12f,color);
                    m_Effects.Add(tip,new float2(.065f),m_Art.Sheet[m_Art.Disc].Uv,-.13f,new float4(.25f,1,.78f,1));
                }
                float contact=math.saturate((NaturalCharacterRig.Strike(m_Time+(i==3?.14f:0))-.9f)*10);
                if(contact>0)
                {
                    m_Effects.Add(tip,new float2(.43f,.43f)*contact,m_Art.Sheet[m_Art.Halo].Uv,-.14f,new float4(1,.5f,.12f,.55f));
                    for(int spark=0;spark<4;spark++)
                    {float angle=spark*1.5f+m_Time;float2 d=new float2(math.cos(angle),math.sin(angle));m_Effects.Add(tip+d*.14f,new float2(.16f,.025f)*contact,m_Art.Sheet[m_Art.White].Uv,-.15f,new float4(1,.8f,.34f,contact),angle);}
                }
            }
        }
        void CreateUi()
        {
            m_Ui=new GameObject("Motion Showcase UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=m_Ui.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=m_Camera;canvas.planeDistance=1;canvas.sortingOrder=30;
            m_UiScaler=m_Ui.GetComponent<CanvasScaler>();m_UiScaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;m_UiScaler.referenceResolution=new Vector2(1280,720);m_UiScaler.matchWidthOrHeight=m_Camera.aspect>=16f/9f?1:0;
            var layout=new GameObject("Aspect-fit landscape labels and controls",typeof(RectTransform));m_UiLayout=layout.GetComponent<RectTransform>();m_UiLayout.SetParent(m_Ui.transform,false);m_UiLayout.anchorMin=m_UiLayout.anchorMax=new Vector2(.5f,.5f);m_UiLayout.sizeDelta=new Vector2(1280,720);
            Label("KINETIC / FIELD LAB",new Vector2(.045f,.93f),new Vector2(.8f,.07f),32,new Color(.90f,.86f,.74f));
            Label("NATURAL MOTION   ·   ORIGINAL ARTICULATED CHARACTERS",new Vector2(.046f,.875f),new Vector2(.85f,.04f),13,new Color(.46f,.70f,.71f));
            Label("SAMPLED SILHOUETTES  /  15 HZ SECONDARY MOTION",new Vector2(.046f,.84f),new Vector2(.87f,.045f),12,new Color(.68f,.72f,.64f));
            Label("WORLD-SPACE FOOT CONTACT",new Vector2(.046f,.175f),new Vector2(.44f,.04f),15,new Color(.79f,.87f,.81f));
            Label("ANTICIPATION  /  CONTACT  /  RECOVERY",new Vector2(.52f,.175f),new Vector2(.46f,.04f),15,new Color(.93f,.75f,.52f));
            m_Status=Label("",new Vector2(.046f,.125f),new Vector2(.90f,.04f),12,new Color(.55f,.68f,.71f));
            Button("PAUSE",.046f,()=>Playback=!Playback);Button("SHADOW",.245f,CycleQuality);Button("LIGHT",.444f,()=>Shading=!Shading);Button("REACH",.643f,()=>UnreachableTarget=!UnreachableTarget);Button("TIER",.842f,()=>ForceDataTexture=!ForceDataTexture);
            if(EventSystem.current==null){m_EventSystem=new GameObject("Motion UI Events",typeof(EventSystem),typeof(StandaloneInputModule));}
            RefreshStatus();
        }
        Text Label(string value,Vector2 anchor,Vector2 size,int fontSize,Color color)
        {
            var go=new GameObject(value,typeof(RectTransform),typeof(Text));go.transform.SetParent(m_UiLayout,false);
            var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=anchor;rect.pivot=new Vector2(0,.5f);rect.sizeDelta=new Vector2(1280*size.x,720*size.y);
            var text=go.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.text=value;text.fontSize=fontSize;text.color=color;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Overflow;return text;
        }
        void Button(string value,float x,UnityEngine.Events.UnityAction action)
        {
            var go=new GameObject(value,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(m_UiLayout,false);var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(x,.063f);rect.pivot=new Vector2(0,.5f);rect.sizeDelta=new Vector2(140,37);
            go.GetComponent<Image>().color=new Color(.12f,.23f,.27f,.95f);go.GetComponent<Button>().onClick.AddListener(action);
            var text=Label(value,new Vector2(x+.014f,.063f),new Vector2(.10f,.04f),13,new Color(.82f,.88f,.84f));text.alignment=TextAnchor.MiddleLeft;
        }
        void CycleQuality(){ShadowQuality=(PoseShadowQuality)(((int)ShadowQuality+1)%3);}
        void RefreshStatus()
        {
            if(m_Status!=null)m_Status.text="8 ACTORS · 14 BONES · CPU/BURST CUTOUT · "+(ActiveTier==RenderTier.GpuDriven?"INDIRECT SPRITES":"DATA-TEXTURE SPRITES")+" · "+ShadowQuality.ToString().ToUpperInvariant()+" · IK USES BLOB";
        }
        void OnDisable()
        {
            m_Actors?.Dispose();m_Set?.Dispose();m_Shadow?.Dispose();m_Blob?.Dispose();m_Effects?.Dispose();m_ShadowAtlas?.Dispose();m_Art?.Dispose();m_Rig?.Dispose();
            if(m_Local.IsCreated)m_Local.Dispose();if(m_World.IsCreated)m_World.Dispose();if(m_Input.IsCreated)m_Input.Dispose();if(m_Attachments.IsCreated)m_Attachments.Dispose();if(m_PoseTicks.IsCreated)m_PoseTicks.Dispose();
            RenderObjects.Destroy(m_Normals);RenderObjects.Destroy(m_Ui);RenderObjects.Destroy(m_EventSystem);RenderObjects.Destroy(m_CameraObject);
            SpriteLighting.Begin(Color.white);SpriteLighting.Apply();
        }
    }
}
#endif
