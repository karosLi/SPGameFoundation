using SPF.Presentation.ArtDirection;
using System;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Presentation.Combat;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using SPF.Shell.CameraRig;
using Unity.Mathematics;
using UnityEngine;

namespace ShooterFoundation.Presentation
{
    /// <summary>
    /// Smooth alpha uses separate explicit render queues: background, clouds, shadows, pickups,
    /// Y-sorted aircraft, beam, bullets, impact rings. Aircraft blend in stable far-to-near Y/ID order;
    /// no alpha-cutout shortcut and no per-entity GameObjects. Both tiers share the exact sprite stream.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class ShooterRenderer : MonoBehaviour
    {
        public SessionHost Host; public FollowCamera2D Camera;
        public RenderTier? ForcedTier;
        public event Action<ShooterFeedback> Feedback;
        public int SpritesDrawn { get; private set; }
        public long BytesUploaded { get; private set; }
        public int QualityLevel { get; private set; }
        public int ShadowBudget => QualityLevel >= 3 ? 0 : QualityLevel == 2 ? 24 : QualityLevel == 1 ? 64 : 128;
        public int EffectBudget => VfxBudget.ForQuality(QualityLevel).Active;
        public VfxDiagnostics VfxStats => m_Fx.Stats;
        public int ActiveEffects => m_Fx.Active;
        public VfxProfile ImpactProfile = VfxProfile.Impact, ShotProfile = VfxProfile.Muzzle, DeathProfile = VfxProfile.Destruction;
        public RenderTier Tier { get; private set; }
        SanctuaryBackdrop m_Sanctuary;
        ShooterArt m_Art;
        SpriteBatch m_Back, m_Clouds, m_Shadows, m_Pickups, m_Actors, m_Beam, m_Bullets, m_Effects;
        int[] m_Order;
        readonly CombatVfxPool m_Fx = new CombatVfxPool(96);
        int m_LevelVersion = -1, m_LastHitTick = -1;
        uint m_EventSequence;
        BlobShadowProfile m_Shadow;
        public void SetQuality(int level) => QualityLevel = math.clamp(level,0,3);
        void Start()
        {
            if (Host == null || Host.Session == null) return;
            var s = Host.Session.World.Resource(ShooterKeys.Rules).Settings;
            m_Art = ShooterArt.Build(); Tier = ForcedTier ?? RenderCapabilities.Detect();
            m_Sanctuary=new SanctuaryBackdrop(Tier,SanctuaryScene.SkyRiver);
            m_Back = Batch(BlendKind.Opaque, 96, 0); m_Clouds = Batch(BlendKind.Translucent,24,0);
            m_Shadows = Batch(BlendKind.Translucent,130,1); m_Pickups = Batch(BlendKind.Translucent,s.PickupCapacity,2);
            m_Actors = Batch(BlendKind.Translucent,s.EnemyCapacity+4,3); m_Beam = Batch(BlendKind.Translucent,8,4);
            m_Bullets = Batch(BlendKind.Translucent,s.BulletCapacity,5); m_Effects = Batch(BlendKind.Translucent,384,6);
            m_Order = new int[s.EnemyCapacity];
            m_Shadow = BlobShadowProfile.Default; m_Shadow.Size = new float2(1.2f,0.62f); m_Shadow.Offset = new float2(0.15f,-0.40f); m_Shadow.Color = new float4(0.015f,0.04f,0.08f,0.42f);
        }
        SpriteBatch Batch(BlendKind blend,int capacity,int order) { var b = new SpriteBatch(Tier,m_Art.Sheet.Texture,blend,capacity,order); b.Warmup(capacity); return b; }
        void LateUpdate()
        {
            if (m_Art == null || Host == null || Host.Session == null) return;
            var session = Host.Session; var w = session.World; ref var r = ref w.Resource(ShooterKeys.State).Run;
            var s = w.Resource(ShooterKeys.Rules).Settings; float alpha = r.Flow == ShooterFlow.Playing ? session.InterpolationAlpha : 1f;
            if (m_LevelVersion != w.LevelVersion) { m_LevelVersion = w.LevelVersion; m_Fx.Clear(); m_LastHitTick = -1; m_EventSequence = 0; }
            Clear(); m_Fx.BeginFrame(r.Flow == ShooterFlow.Playing ? Time.deltaTime : 0f, QualityLevel); Drain(w);
            int hitTick = (int)session.Clock.NextTickIndex / 3; bool emitHits = hitTick != m_LastHitTick; m_LastHitTick = hitTick;
            Background(s.ArenaHalf,r.Time);
            float2 hero = math.lerp(r.HeroPrevious,r.Hero,alpha);
            var p = w.Column(ShooterKeys.EnemyPosition); var prev = w.Column(ShooterKeys.EnemyPrevious); var info = w.Column(ShooterKeys.Enemies); var table = w.Table(ShooterKeys.Enemy);
            int count = 0;
            for (int i=0;i<table.Count;i++) if (table.DeadFlags[i] == 0) m_Order[count++] = i;
            // In-place heapsort: bounded O(N log N), no comparer/delegate/list allocation.
            for (int i=count/2-1;i>=0;i--) Sift(i,count,p,info);
            for (int end=count-1;end>0;end--) { Swap(0,end); Sift(0,end,p,info); }
            int shadows = 0;
            for (int j=0;j<count;j++)
            {
                int i=m_Order[j]; var e=info[i]; float2 pos=math.lerp(prev[i],p[i],alpha); float size=e.Radius*3.2f;
                if (shadows++ < ShadowBudget) BlobShadow.Add(m_Shadows,Uv(m_Art.Shadow),pos,3f,m_Shadow,0f,size);
                float hit = math.saturate(e.Flash * 8f);
                m_Actors.Add(pos,new float2(size * (1f + hit * 0.035f), size * (1f - hit * 0.035f)),Uv(e.Kind==2?m_Art.Heavy:m_Art.Drone),0f,new float4(1f),0f,hit * 0.24f);
                if (emitHits && e.Flash > 0f)
                    m_Fx.Emit(ImpactProfile, pos + new float2(0f,-size*0.15f), ((ulong)(uint)(hitTick+1)<<32) | (uint)e.Id, 0.7f);
            }
            if (r.Flow != ShooterFlow.Menu)
            {
                if (ShadowBudget > 0) BlobShadow.Add(m_Shadows,Uv(m_Art.Shadow),hero,3f,m_Shadow,0f,1.5f);
                float angle=math.clamp((r.Hero.x-r.HeroPrevious.x)*-0.65f,-0.15f,0.15f);
                float4 tint = r.Invulnerable > 0f ? new float4(1f,1f,1f,0.72f) : new float4(1f);
                m_Actors.Add(hero,new float2(1.7f),Uv(m_Art.Plane),-0.2f,tint,angle);
                if (r.Wingman>0) m_Actors.Add(hero+new float2(-0.95f,-0.25f),new float2(0.9f),Uv(m_Art.Wing),-0.3f,new float4(1f),angle);
                if (r.BeamTargetId>0 && r.Flow==ShooterFlow.Playing) DrawBeam(hero+new float2(0f,0.55f),r.BeamEnd,r.Time);
                if (QualityLevel < 2) { AddRect(m_Beam,hero+new float2(-0.075f,-0.68f),new float2(0.08f,0.3f),new float4(0.3f,0.86f,1f,0.8f),-0.5f); AddRect(m_Beam,hero+new float2(0.075f,-0.68f),new float2(0.08f,0.3f),new float4(0.3f,0.86f,1f,0.8f),-0.5f); }
            }
            var pickups=w.Table(ShooterKeys.Pickup); var pp=w.Column(ShooterKeys.PickupPosition); var pi=w.Column(ShooterKeys.Pickups);
            for (int i=0;i<pickups.Count;i++) if (pickups.DeadFlags[i]==0) m_Pickups.Add(pp[i],new float2(0.50f),Uv(pi[i].Heal?m_Art.Repair:m_Art.Coin),1f,new float4(1f));
            var bullets=w.Table(ShooterKeys.Bullet); var bp=w.Column(ShooterKeys.BulletPosition); var bprev=w.Column(ShooterKeys.BulletPrevious); var bi=w.Column(ShooterKeys.Bullets);
            for (int i=0;i<bullets.Count;i++)
            {
                if (bullets.DeadFlags[i]!=0) continue; var b=bi[i]; float2 pos=math.lerp(bprev[i],bp[i],alpha);
                m_Bullets.Add(pos,b.Hostile?new float2(0.40f):new float2(0.18f,0.62f),Uv(b.Hostile?m_Art.Orb:m_Art.Bolt),-1f,new float4(1f),math.atan2(b.Velocity.y,b.Velocity.x)-math.PI*0.5f);
            }
            m_Fx.Draw(m_Effects, m_Art.CombatFx.Resolve(m_Art.Sheet), Camera != null ? Camera.ViewRect : new float4(-s.ArenaHalf, s.ArenaHalf));
            var bounds=new Bounds(Vector3.zero,new Vector3(s.ArenaHalf.x*2f+10f,s.ArenaHalf.y*2f+10f,100f));
            SpritesDrawn=0; BytesUploaded=0;
            if(m_Sanctuary.Ready){m_Sanctuary.Draw(Camera!=null?Camera.ViewRect:new float4(-s.ArenaHalf,s.ArenaHalf),bounds,r.Time);SpritesDrawn+=m_Sanctuary.SpritesDrawn;BytesUploaded+=m_Sanctuary.BytesUploaded;}
            Draw(m_Back,bounds); Draw(m_Clouds,bounds); Draw(m_Shadows,bounds); Draw(m_Pickups,bounds); Draw(m_Actors,bounds); Draw(m_Beam,bounds); Draw(m_Bullets,bounds); Draw(m_Effects,bounds);
        }
        bool After(int a,int b,Unity.Collections.NativeArray<float2> p,Unity.Collections.NativeArray<ShooterEnemy> info) => p[a].y < p[b].y || p[a].y==p[b].y && info[a].Id>info[b].Id;
        void Sift(int root,int n,Unity.Collections.NativeArray<float2> p,Unity.Collections.NativeArray<ShooterEnemy> info)
        {
            for (int child=root*2+1;child<n;child=root*2+1) { if (child+1<n && After(m_Order[child+1],m_Order[child],p,info)) child++; if (!After(m_Order[child],m_Order[root],p,info)) break; Swap(root,child); root=child; }
        }
        void Swap(int a,int b) { int x=m_Order[a]; m_Order[a]=m_Order[b]; m_Order[b]=x; }
        void Background(float2 half,float time)
        {
            if(m_Sanctuary.Ready)return;
            AddRect(m_Back,float2.zero,half*2f+new float2(0.6f,3f),new float4(0.06f,0.14f,0.24f,1f),10f);
            for (int i=0;i<12;i++)
            {
                float y=ShooterMath.Repeat(i*2.1f-time*0.7f+40f,half.y*2f+4f)-half.y-2f;
                AddRect(m_Back,new float2(-half.x+0.20f,y),new float2(0.08f,1.0f),new float4(0.13f,0.31f,0.40f,1f),9f);
                AddRect(m_Back,new float2(half.x-0.20f,y),new float2(0.08f,1.0f),new float4(0.13f,0.31f,0.40f,1f),9f);
                if (QualityLevel<3) { float x=((i*3.7f)%8f)-4f; m_Clouds.Add(new float2(x,y),new float2(3.5f,2.4f),Uv(m_Art.Cloud),8f,new float4(0.6f,0.8f,1f,0.24f)); }
            }
            for (int i=0;i<18;i++)
            {
                float y=ShooterMath.Repeat(i*1.37f-time*1.8f+80f,half.y*2f+2f)-half.y-1f; float x=((i*2.41f)%9f)-4.5f;
                AddRect(m_Back,new float2(x,y),new float2(0.025f,0.17f),new float4(0.2f,0.38f,0.48f,1f),9f);
            }
        }
        void DrawBeam(float2 start,float2 end,float time)
        {
            float2 delta=end-start; float length=math.length(delta); float angle=math.atan2(delta.y,delta.x)-math.PI*0.5f; float2 center=(start+end)*0.5f;
            if (QualityLevel<2) m_Beam.Add(center,new float2(0.30f,length+0.10f),Uv(m_Art.CombatFx.Glow),-0.6f,new float4(1f,0.48f,0.17f,0.26f),angle);
            m_Beam.Add(center,new float2(0.085f,length),Uv(m_Art.White),-0.7f,new float4(1f,0.68f,0.28f,0.9f),angle);
            m_Beam.Add(center,new float2(0.035f,length),Uv(m_Art.White),-0.8f,new float4(1f,1f,0.85f,1f),angle);
            m_Beam.Add(end,new float2(0.34f+0.045f*math.sin(time*35f)),Uv(m_Art.CombatFx.Core),-0.9f,new float4(1f,0.89f,0.62f,0.9f),time*1.4f);
        }
        float4 Uv(int sprite) => m_Art.Sheet[sprite].Uv;
        void AddRect(SpriteBatch batch,float2 pos,float2 size,float4 tint,float z) => batch.Add(pos,size,Uv(m_Art.White),z,tint);
        void Clear() { m_Back.Clear();m_Clouds.Clear();m_Shadows.Clear();m_Pickups.Clear();m_Actors.Clear();m_Beam.Clear();m_Bullets.Clear();m_Effects.Clear(); }
        void Draw(SpriteBatch batch,Bounds bounds) { batch.Draw(bounds); SpritesDrawn+=batch.Count; BytesUploaded+=batch.BytesUploaded; }
        void Drain(SimWorld w)
        {
            var events=w.Resource(ShooterKeys.Feedback);
            for (int i=0;i<events.Count;i++)
            {
                var e=events[i]; Feedback?.Invoke(e);
                ulong key = 0xf000000000000000ul | ++m_EventSequence;
                if (e.Kind==ShooterFeedbackKind.Destroyed) m_Fx.Emit(DeathProfile,e.Position,key,e.Scale);
                else if (e.Kind==ShooterFeedbackKind.Shot) m_Fx.Emit(ShotProfile,e.Position+new float2(0f,0.8f),key);
                else if (e.Kind==ShooterFeedbackKind.Hit) m_Fx.Emit(ImpactProfile,e.Position,key);
                else if (e.Kind==ShooterFeedbackKind.Hurt) m_Fx.Emit(VfxProfile.HeroHurt,e.Position,key);
            }
            events.Clear();
        }
        void OnDestroy() { m_Sanctuary?.Dispose(); m_Back?.Dispose();m_Clouds?.Dispose();m_Shadows?.Dispose();m_Pickups?.Dispose();m_Actors?.Dispose();m_Beam?.Dispose();m_Bullets?.Dispose();m_Effects?.Dispose();m_Art?.Dispose(); }
    }
}
