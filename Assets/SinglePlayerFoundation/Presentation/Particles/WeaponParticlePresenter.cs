using System;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Particles
{
    /// <summary>Sequenced weapon cues + evaluated sockets → bounded presentation effects. No gameplay writes or random draws.</summary>
    public sealed class WeaponParticlePresenter : IDisposable
    {
        struct Emitter
        {
            public EntityHandle Owner;
            public int ContentId, VisualId, Frame;
            public uint Token, Action;
            public WeaponActionFamily Family;
            public WeaponStage Stage;
            public float2 Tip, Muzzle, Direction;
            public float Depth, ChargeClock, RuneClock;
            public bool Hero, HasTip, Charging;
        }
        struct CueCursor { public EntityHandle Owner; public uint Sequence; }
        readonly Emitter[] m_Emitters = new Emitter[ParticleLimits.Emitters];
        readonly CueCursor[] m_Cues = new CueCursor[ParticleLimits.CueOwners];
        int m_Frame;
        uint m_Token;
        bool m_Disposed;
        public ParticleRenderer Renderer { get; }
        public int DroppedEmitters { get; private set; }
        public int DroppedCueOwners { get; private set; }
        public WeaponParticlePresenter(RenderTier tier, bool lowQuality=false, bool forceCpu=false) => Renderer=new ParticleRenderer(tier,lowQuality,forceCpu);
        public void BeginFrame(float dt,float4 viewRect)
        {
            Check();m_Frame++;DroppedEmitters=0;DroppedCueOwners=0;Renderer.Pool.BeginFrame(dt,viewRect);
        }
        public void UpdateEmitter(EntityHandle owner,in WeaponViewState state,float2 tip,float2 muzzle,float2 direction,float depth=0,bool hero=false)
        {
            Check();if(owner.IsNull)return;
            if(!state.Equipped || !math.all(math.isfinite(tip)) || !math.all(math.isfinite(muzzle)) || !math.isfinite(depth)){ResetOwner(owner);return;}
            int slot=-1;
            for(int i=0;i<m_Emitters.Length;i++)
            {
                if(m_Emitters[i].Owner==owner){slot=i;break;}
                if(m_Emitters[i].Owner.Index==owner.Index && !m_Emitters[i].Owner.IsNull) ResetSlot(i);
            }
            if(slot<0)
            {
                for(int i=0;i<m_Emitters.Length;i++)if(m_Emitters[i].Owner.IsNull){slot=i;break;}
                if(slot<0 && hero)for(int i=0;i<m_Emitters.Length;i++)if(!m_Emitters[i].Hero){ResetSlot(i);slot=i;break;}
                if(slot<0){DroppedEmitters++;return;}
                m_Emitters[slot]=new Emitter{Owner=owner,Token=NextToken(),ContentId=state.ContentId};
            }
            ref var e=ref m_Emitters[slot];
            if(e.ContentId!=state.ContentId || (e.HasTip && (e.Action!=state.ActionPulse || e.VisualId!=state.VisualId || math.distance(e.Muzzle,muzzle)>2.5f)) || e.Frame<m_Frame-1)
            {
                ResetSlot(slot);e=new Emitter{Owner=owner,Token=NextToken(),ContentId=state.ContentId};
            }
            var aim=math.normalizesafe(direction,new float2(1,0));
            Renderer.Pool.SetSocket(slot,new ParticleSocket{Pose=new float4(muzzle,aim),Identity=new uint4(e.Token,1,0,0)});
            bool active=state.Stage==WeaponStage.Active && (state.Family==WeaponActionFamily.Slash || state.Family==WeaponActionFamily.Thrust);
            if(active && e.HasTip && e.Action==state.ActionPulse && math.distance(e.Tip,tip)<1.3f)
            {
                uint seed=ParticleMath.Hash((uint)owner.Index ^ (uint)owner.Generation*747796405u ^ state.ActionPulse*2891336453u ^ (uint)m_Frame);
                Segment(e.Tip,tip,depth, state.Family==WeaponActionFamily.Thrust ? new float4(.50f,.88f,1,.76f) : new float4(.74f,.91f,1,.72f),
                    state.Family==WeaponActionFamily.Thrust?.025f:.048f,.105f,seed,hero);
            }
            bool charging=(state.Stage==WeaponStage.Windup || state.Stage==WeaponStage.Active) && state.Phase<state.ReleasePhase &&
                (state.Family==WeaponActionFamily.Cast || state.Family==WeaponActionFamily.Draw);
            if(charging)
            {
                e.ChargeClock+=Renderer.Pool.DeltaTime;e.RuneClock+=Renderer.Pool.DeltaTime;
                if(e.ChargeClock>=.065f)
                {
                    e.ChargeClock-=.065f;
                    uint seed=ParticleMath.Hash((uint)owner.Index ^ state.ActionPulse*1009u ^ (uint)m_Frame*9176u);
                    int count=state.Family==WeaponActionFamily.Cast?3:1;
                    for(int i=0;i<count;i++)
                    {
                        float angle=ParticleMath.Random01(ref seed)*2*math.PI;
                        float radius=math.lerp(.18f,.38f,ParticleMath.Random01(ref seed));
                        float2 local=new float2(math.cos(angle),math.sin(angle))*radius;
                        var color=state.Family==WeaponActionFamily.Cast?new float4(.36f,.70f,1,.9f):new float4(1,.81f,.39f,.66f);
                        var p=Make(local,-local*3.6f,.27f,new float2(.025f,.055f),angle+math.PI*.5f,color,depth,ParticleShape.Spark,ParticlePriority.Decorative);
                        p.Attachment.x=(uint)slot+1;p.Attachment.y=e.Token;p.VelocityDrag.z=.3f;
                        Renderer.Pool.Spawn(p);
                    }
                }
                if(state.Family==WeaponActionFamily.Cast && e.RuneClock>=.20f)
                {
                    e.RuneClock-=.20f;
                    var rune=Make(float2.zero,float2.zero,.24f,new float2(.34f+.09f*state.StagePhase),.18f*m_Frame,new float4(.21f,.54f,.94f,.62f),depth,ParticleShape.Rune,ParticlePriority.Decorative);
                    rune.Attachment.x=(uint)slot+1;rune.Attachment.y=e.Token;rune.Shape.w=.8f;rune.Visual.z=.65f;
                    Renderer.Pool.Spawn(rune);
                }
            }
            else
            {
                if(e.Charging)
                {
                    // Cancel/release resets attached charge particles immediately; bursts remain in world space.
                    e.Token=NextToken();Renderer.Pool.SetSocket(slot,new ParticleSocket{Pose=new float4(muzzle,aim),Identity=new uint4(e.Token,1,0,0)});
                }
                e.ChargeClock=0;e.RuneClock=0;
            }
            e.Tip=tip;e.Muzzle=muzzle;e.Direction=aim;e.Depth=depth;e.Family=state.Family;e.Stage=state.Stage;e.Action=state.ActionPulse;e.VisualId=state.VisualId;
            e.Hero=hero;e.HasTip=true;e.Charging=charging;e.Frame=m_Frame;
        }
        public void SubmitCue(in WeaponCue cue,float depth=0,bool hero=false,WeaponActionFamily family=WeaponActionFamily.None)
        {
            Check();if(!AcceptCue(cue))return;
            int slot=Find(cue.Owner);var emitter=slot>=0?m_Emitters[slot]:default;
            bool sameContent=slot>=0&&(cue.ContentId==0||cue.ContentId==emitter.ContentId);
            bool sameAction=sameContent&&cue.ActionPulse==emitter.Action;
            if((cue.Kind&(WeaponCueKind.Equip|WeaponCueKind.Cancel))!=0)
            {
                // A retained ring can contain the prior action's stop after the next socket was
                // submitted. Consume its sequence without invalidating a different live action.
                if(sameAction)ResetSlot(slot);
                return;
            }
            if(family==WeaponActionFamily.None&&sameContent)family=emitter.Family;
            var direction=math.normalizesafe(cue.Direction,new float2(1,0));
            uint seed=ParticleMath.Hash(cue.Sequence ^ cue.ActionPulse*2891336453u ^ (uint)cue.Owner.Generation*747796405u ^ (uint)cue.Owner.Index);
            if((cue.Kind&WeaponCueKind.Impact)!=0)
                Burst(cue.Position,direction,depth,seed,hero?ParticlePriority.Hero:ParticlePriority.Impact,family,true);
            if((cue.Kind&WeaponCueKind.Release)!=0)
            {
                // Detached bursts belong to the authoritative release position, even when the
                // renderer missed release and now observes recovery, movement or another weapon.
                Burst(cue.Position,direction,depth,seed,hero?ParticlePriority.Hero:ParticlePriority.Release,family,false);
            }
        }
        bool AcceptCue(in WeaponCue cue)
        {
            if(cue.Owner.IsNull || cue.Sequence==0)return false;
            int free=-1;
            for(int i=0;i<m_Cues.Length;i++)
            {
                var cursor=m_Cues[i];
                if(cursor.Owner.IsNull){if(free<0)free=i;continue;}
                if(cursor.Owner.Index!=cue.Owner.Index)continue;
                if(cursor.Owner==cue.Owner)
                {
                    if(unchecked((int)(cue.Sequence-cursor.Sequence))<=0)return false;
                }
                else if(unchecked(cue.Owner.Generation-cursor.Owner.Generation)<=0)return false;
                m_Cues[i]=new CueCursor{Owner=cue.Owner,Sequence=cue.Sequence};return true;
            }
            // Keep admitted cursors across emitter resets. Eviction would allow old cue replay.
            // A new generation reuses its index above; a new index at capacity waits for Clear.
            if(free<0){DroppedCueOwners++;return false;}
            m_Cues[free]=new CueCursor{Owner=cue.Owner,Sequence=cue.Sequence};return true;
        }
        void Burst(float2 origin,float2 aim,float depth,uint seed,ParticlePriority priority,WeaponActionFamily family,bool impact)
        {
            int count=impact?9:family==WeaponActionFamily.Cast?11:family==WeaponActionFamily.Draw?5:3;
            var color=impact?new float4(1,.66f,.22f,.95f):family==WeaponActionFamily.Cast?new float4(.30f,.68f,1,.9f):new float4(.90f,.95f,1,.8f);
            for(int i=0;i<count;i++)
            {
                float spread=(ParticleMath.Random01(ref seed)-.5f)*(impact?3.3f:family==WeaponActionFamily.Cast?2.2f:.45f);
                var direction=ParticleMath.Rotate(aim,new float2(math.cos(spread),math.sin(spread)));
                float speed=math.lerp(impact?1.0f:.6f,impact?3.9f:2.7f,ParticleMath.Random01(ref seed));
                float life=math.lerp(.12f,.30f,ParticleMath.Random01(ref seed));
                var p=Make(origin,direction*speed,life,new float2(math.lerp(.08f,.17f,ParticleMath.Random01(ref seed)),.027f),math.atan2(direction.y,direction.x),color,depth,ParticleShape.Streak,priority);
                p.VelocityDrag.z=impact?3f:2;p.VelocityDrag.w=impact?-2.2f:0;Renderer.Pool.Spawn(p);
            }
            if(impact || family==WeaponActionFamily.Cast)
            {
                // Compact core pop then detached, slower embers: readable without broad overlapping glows.
                var core=Make(origin,float2.zero,.10f,new float2(impact?.15f:.22f),0,new float4(color.xyz,.9f),depth,ParticleShape.Spark,priority);core.Visual.z=.2f;Renderer.Pool.Spawn(core);
                for(int i=0;i<3;i++)
                {
                    var drift=new float2((ParticleMath.Random01(ref seed)-.5f)*.55f,.25f+ParticleMath.Random01(ref seed)*.4f);
                    var ember=Make(origin,drift,.42f,new float2(.043f),0,new float4(color.xyz,.66f),depth,ParticleShape.Ember,priority);ember.VelocityDrag.z=1;Renderer.Pool.Spawn(ember);
                }
            }
            if(!impact && family==WeaponActionFamily.Draw)
            {
                var across=new float2(-aim.y,aim.x)*.095f;
                Segment(origin-across,origin+across,depth,new float4(1,.79f,.34f,.8f),.018f,.065f,seed,true);
            }
        }
        public void ProjectileTrail(float2 previous,float2 current,float depth,uint visualSeed,bool hero=false)
        {
            Check();if(math.distance(previous,current)>.8f)return;
            Segment(previous,current,depth,new float4(1,.81f,.38f,.6f),.025f,.12f,visualSeed,hero);
        }
        void Segment(float2 start,float2 end,float depth,float4 color,float width,float life,uint seed,bool hero)
        {
            float2 delta=end-start;float distance=math.length(delta);if(distance<.008f || distance>1.3f)return;
            // Finite segment between actual successive blade/arrow samples, tapered by atlas and life.
            var p=Make((start+end)*.5f,float2.zero,life,new float2(distance+.018f,width),math.atan2(delta.y,delta.x),color,depth,ParticleShape.Streak,hero?ParticlePriority.Release:ParticlePriority.Trail);
            p.Visual.z=.12f;p.Visual.w=.001f;Renderer.Pool.Spawn(p);
        }
        public static ParticleState Make(float2 position,float2 velocity,float life,float2 size,float angle,float4 color,float depth,ParticleShape shape,ParticlePriority priority)
            => new ParticleState{PositionAge=new float4(position,0,life),VelocityDrag=new float4(velocity,0,0),Shape=new float4(size,angle,0),Color=color,
                Visual=new float4(depth,(float)shape,.1f,.012f),Attachment=new uint4(0,0,(uint)priority,0)};
        public void EndFrame(Bounds bounds,int layer=0)
        {
            Check();for(int i=0;i<m_Emitters.Length;i++)if(!m_Emitters[i].Owner.IsNull && m_Emitters[i].Frame!=m_Frame)ResetSlot(i);
            Renderer.Simulate();Renderer.Draw(bounds,layer);
        }
        public void ResetOwner(EntityHandle owner) { Check();int slot=Find(owner);if(slot>=0)ResetSlot(slot); }
        void ResetSlot(int slot) { Renderer.Pool.SetSocket(slot,default);m_Emitters[slot]=default; }
        int Find(EntityHandle owner) { for(int i=0;i<m_Emitters.Length;i++)if(m_Emitters[i].Owner==owner && !owner.IsNull)return i;return -1; }
        uint NextToken() { m_Token++;if(m_Token==0)m_Token++;return m_Token; }
        public void Clear() { Check();Array.Clear(m_Emitters,0,m_Emitters.Length);Array.Clear(m_Cues,0,m_Cues.Length);DroppedEmitters=0;DroppedCueOwners=0;Renderer.Clear(); }
        public void Dispose(){if(m_Disposed)return;m_Disposed=true;Renderer.Dispose();}
        void Check(){if(m_Disposed)throw new ObjectDisposedException(nameof(WeaponParticlePresenter));}
    }
}
