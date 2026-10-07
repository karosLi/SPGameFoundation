using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    public enum GameplayLocomotionState : byte { Idle, Walk, Run, Air }

    /// <summary>Blittable authored style data. A game can supply an override with a nonzero Id without
    /// adding a global actor or animation enum. Thresholds and distances are in character model units.</summary>
    public struct GameplayLocomotionProfile
    {
        public int Id;
        public float RunEnter,RunExit,WalkStride,RunStride,WalkPeriod,RunPeriod,MinimumPeriod;
        public float WalkArm,RunArm,BodySway,Lean,Breath,FootWidth,StepHeight,WeightShift,HitRecoil;
    }
    public static class GameplayMotionProfiles
    {
        public static GameplayLocomotionProfile Resolve(in GameplayCharacterInput input)=>input.MotionProfile.Id!=0&&Valid(input.MotionProfile)?input.MotionProfile:Get(input.MotionProfileId);
        public static bool Valid(in GameplayLocomotionProfile p)=>p.Id>0&&
            math.all(math.isfinite(new float4(p.RunEnter,p.RunExit,p.WalkStride,p.RunStride)))&&
            math.all(math.isfinite(new float4(p.WalkPeriod,p.RunPeriod,p.MinimumPeriod,p.WalkArm)))&&
            math.all(math.isfinite(new float4(p.RunArm,p.BodySway,p.Lean,p.Breath)))&&
            math.all(math.isfinite(new float4(p.FootWidth,p.StepHeight,p.WeightShift,p.HitRecoil)))&&
            p.RunEnter>p.RunExit&&p.RunExit>=.1f&&p.RunEnter<=20&&p.WalkStride>=.2f&&p.WalkStride<=4&&p.RunStride>=.2f&&p.RunStride<=4&&
            p.MinimumPeriod>=.26f&&p.MinimumPeriod<=1&&p.WalkPeriod>=.44f&&p.WalkPeriod<=2&&p.RunPeriod>=p.MinimumPeriod&&p.RunPeriod<=2&&
            p.WalkArm>=0&&p.WalkArm<=1.2f&&p.RunArm>=0&&p.RunArm<=1.2f&&math.abs(p.BodySway)<=.25f&&math.abs(p.Lean)<=.05f&&p.Breath>=0&&p.Breath<=.03f&&
            p.FootWidth>=.08f&&p.FootWidth<=.2f&&p.StepHeight>=.1f&&p.StepHeight<=.28f&&p.WeightShift>=0&&p.WeightShift<=.08f&&p.HitRecoil>=.1f&&p.HitRecoil<=2;
        public static GameplayLocomotionProfile Get(int id)
        {
            var p=new GameplayLocomotionProfile {Id=1,RunEnter=3.1f,RunExit=2.6f,WalkStride=1.4f,RunStride=2.7f,
                WalkPeriod=1.08f,RunPeriod=.85f,MinimumPeriod=.26f,WalkArm=.40f,RunArm=.64f,BodySway=.13f,Lean=.021f,
                Breath=.007f,FootWidth=.13f,StepHeight=.19f,WeightShift=.045f,HitRecoil=1};
            if(id==1) // Agile: lighter support, quicker response and a larger balancing arm arc.
            {p.Id=2;p.RunEnter=2.65f;p.RunExit=2.15f;p.WalkStride=1.25f;p.RunStride=2.5f;p.WalkPeriod=.95f;p.RunPeriod=.75f;p.WalkArm=.48f;p.RunArm=.72f;p.BodySway=.15f;p.Breath=.005f;p.FootWidth=.105f;p.StepHeight=.24f;p.WeightShift=.038f;p.HitRecoil=1.2f;}
            else if(id==2) // Heavy: wider support, deliberate cycle and restrained upper-arm swing.
            {p.Id=3;p.RunEnter=3.8f;p.RunExit=3.1f;p.WalkStride=1.65f;p.RunStride=3;p.WalkPeriod=1.18f;p.RunPeriod=1.02f;p.MinimumPeriod=.34f;p.WalkArm=.23f;p.RunArm=.46f;p.BodySway=.075f;p.Lean=.026f;p.Breath=.012f;p.FootWidth=.16f;p.StepHeight=.14f;p.WeightShift=.06f;p.HitRecoil=.7f;}
            return p;
        }
    }

    /// <summary>Skill content, not a skill enum. Targets are local hand anchors; the envelope and body
    /// keys are evaluated from the authoritative skill phase. Zero/unknown Id has no skill layer.</summary>
    public struct GameplaySkillPoseProfile
    {
        public int Id;
        public float Contact,Follow,WindBody,ContactBody,FollowBody,PelvisDrop,Head,NearWeight,FarWeight;
        public float2 NearHand,FarHand;
        public float SupportRelease,WeaponAngle,FootTuck,Fall;
    }
    public struct GameplaySkillPose
    {
        public float2 NearHand,FarHand;
        public float NearWeight,FarWeight,Body,PelvisDrop,Head,SupportRelease,WeaponAngle,FootTuck,Fall;
        public static GameplaySkillPose Blend(in GameplaySkillPose a,in GameplaySkillPose b,float t)
        {
            t=math.saturate(t);float nw=math.lerp(a.NearWeight,b.NearWeight,t),fw=math.lerp(a.FarWeight,b.FarWeight,t);
            return new GameplaySkillPose {NearHand=nw>.00001f?(a.NearHand*a.NearWeight*(1-t)+b.NearHand*b.NearWeight*t)/nw:b.NearHand,
                FarHand=fw>.00001f?(a.FarHand*a.FarWeight*(1-t)+b.FarHand*b.FarWeight*t)/fw:b.FarHand,NearWeight=nw,FarWeight=fw,
                Body=math.lerp(a.Body,b.Body,t),PelvisDrop=math.lerp(a.PelvisDrop,b.PelvisDrop,t),Head=math.lerp(a.Head,b.Head,t),
                SupportRelease=math.lerp(a.SupportRelease,b.SupportRelease,t),WeaponAngle=math.lerp(a.WeaponAngle,b.WeaponAngle,t),
                FootTuck=math.lerp(a.FootTuck,b.FootTuck,t),Fall=math.lerp(a.Fall,b.Fall,t)};
        }
    }
    public struct GameplaySkillMotion
    {
        public GameplaySkillPose Pose,From;
        public int ProfileId;
        public uint Pulse;
        public float Transition;
        public bool Initialized;
        public void Step(in GameplayCharacterInput input,float dt)
        {
            var profile=input.SkillProfile.Id!=0&&GameplaySkillProfiles.Valid(input.SkillProfile)?input.SkillProfile:GameplaySkillProfiles.Get(input.SkillPoseId);
            var next=GameplaySkillProfiles.Sample(profile,input.SkillPhase,input.SkillWeight);
            int id=input.SkillWeight>0?profile.Id:0;
            if(!Initialized){Initialized=true;Transition=1;Pose=next;ProfileId=id;Pulse=input.SkillPulse;return;}
            if(id!=ProfileId||id!=0&&input.SkillPulse!=Pulse){From=Pose;Transition=0;ProfileId=id;Pulse=input.SkillPulse;}
            else Transition=math.min(1,Transition+dt/.12f);
            float blend=NaturalMotion.Ease(Transition);
            if(id!=0)blend=math.max(blend,NaturalMotion.Ease(input.SkillPhase/math.max(.00001f,profile.Contact)));
            Pose=Transition<1?GameplaySkillPose.Blend(From,next,blend):next;
        }
    }
    public static class GameplaySkillProfiles
    {
        public static bool Valid(in GameplaySkillPoseProfile p)=>p.Id>0&&
            math.all(math.isfinite(new float4(p.Contact,p.Follow,p.WindBody,p.ContactBody)))&&
            math.all(math.isfinite(new float4(p.FollowBody,p.PelvisDrop,p.Head,p.NearWeight)))&&
            math.all(math.isfinite(new float4(p.FarWeight,p.SupportRelease,p.WeaponAngle,p.FootTuck)))&&math.isfinite(p.Fall)&&
            math.all(math.isfinite(p.NearHand))&&math.all(math.isfinite(p.FarHand))&&
            p.Contact>.00001f&&p.Contact<.99998f&&p.Follow>p.Contact&&p.Follow<=1&&math.abs(p.WindBody)<=1.2f&&math.abs(p.ContactBody)<=1.2f&&math.abs(p.FollowBody)<=1.2f&&
            p.PelvisDrop>=0&&p.PelvisDrop<=.3f&&math.abs(p.Head)<=.7f&&p.NearWeight>=0&&p.NearWeight<=1&&p.FarWeight>=0&&p.FarWeight<=1&&
            p.SupportRelease>=0&&p.SupportRelease<=1&&math.abs(p.WeaponAngle)<=math.PI&&p.FootTuck>=0&&p.FootTuck<=.5f&&p.Fall>=0&&p.Fall<=1;
        public static GameplaySkillPoseProfile Get(int id)
        {
            var p=new GameplaySkillPoseProfile {Id=id,Contact=.4f,Follow=.62f,NearHand=new float2(.28f,1.45f),FarHand=new float2(-.15f,1.5f),NearWeight=1,FarWeight=1,SupportRelease=1};
            switch(id)
            {
                case 100: // Kick: high guard and a backward chest counterweight.
                    p.Contact=.42f;p.WindBody=-.08f;p.ContactBody=.25f;p.FollowBody=.12f;p.NearHand=new float2(.22f,1.59f);p.WeaponAngle=.38f;break;
                case 101: // Radial pulse: gather at the chest, then open the free hand high/outward.
                    p.Contact=.28f;p.Follow=.55f;p.WindBody=.16f;p.ContactBody=-.14f;p.FollowBody=-.07f;p.PelvisDrop=.075f;
                    p.NearHand=new float2(.38f,1.40f);p.FarHand=new float2(.58f,1.95f);p.WeaponAngle=-.65f;break;
                case 102: // Blink/dodge: low compact silhouette and tucked weapon.
                    p.Contact=.34f;p.Follow=.7f;p.WindBody=.12f;p.ContactBody=-.5f;p.FollowBody=-.32f;p.PelvisDrop=.22f;
                    p.NearHand=new float2(.20f,1.14f);p.FarHand=new float2(-.19f,1.32f);p.WeaponAngle=-.75f;p.Head=.14f;break;
                case 103: // Authoritative airborne phase: tuck, open to land; ground contacts handled separately.
                    p.Contact=.34f;p.Follow=.63f;p.WindBody=-.07f;p.ContactBody=-.18f;p.FollowBody=.12f;
                    p.NearHand=new float2(.22f,1.45f);p.FarHand=new float2(-.24f,1.55f);p.FootTuck=.17f;p.WeaponAngle=.4f;break;
                case 104: // Knockdown only for games that actually report it.
                    p.Contact=.35f;p.Follow=.75f;p.WindBody=.1f;p.ContactBody=.25f;p.FollowBody=.2f;p.Fall=1;p.WeaponAngle=-1.1f;break;
                case 105: // Heal: dominant hand toward chest, free hand lifted, bowed head.
                    p.Contact=.45f;p.Follow=.70f;p.WindBody=.07f;p.ContactBody=.10f;p.FollowBody=.06f;p.Head=-.18f;
                    p.NearHand=new float2(.10f,1.46f);p.FarHand=new float2(-.10f,1.90f);p.WeaponAngle=.75f;p.PelvisDrop=.045f;break;
                default:return default;
            }
            return p;
        }
        public static GameplaySkillPose Sample(in GameplaySkillPoseProfile p,float phase,float weight)
        {
            if(p.Id==0||weight<=0||!Valid(p))return default;
            phase=math.saturate(phase);weight=math.saturate(weight);
            float envelope=math.saturate(WeaponMotion.Curve(phase,p.Contact,p.Follow,0,.3f,1,.55f))*weight;
            return new GameplaySkillPose {NearHand=p.NearHand,FarHand=p.FarHand,NearWeight=p.NearWeight*envelope,FarWeight=p.FarWeight*envelope,
                Body=WeaponMotion.Curve(phase,p.Contact,p.Follow,0,p.WindBody,p.ContactBody,p.FollowBody)*weight,
                PelvisDrop=p.PelvisDrop*envelope,Head=p.Head*envelope,SupportRelease=p.SupportRelease*envelope,
                WeaponAngle=p.WeaponAngle*envelope,FootTuck=p.FootTuck*envelope,Fall=p.Fall*envelope};
        }
    }
}
