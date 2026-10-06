using SPF.Contracts;
using SPF.L1.Skeleton;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    public enum GameplayCharacterState : byte { Idle, Run, Attack, Hit, Death, Recovery }
    public enum GameplayCharacterAction : byte { Punch, Kick, Cast }

    /// <summary>Read-only view contract. Ground is the contact plane; Root includes any authoritative jump
    /// height. Handle must be stable across swap-back/reordering. Timing and aim are inputs, never damage authority.</summary>
    public struct GameplayCharacterInput
    {
        public EntityHandle Handle;
        public float2 Root, Ground, Velocity, AimTarget;
        public float Facing, Scale, Phase, Flash, Depth;
        public float4 Tint;
        public GameplayCharacterState State;
        public GameplayCharacterAction Action;
        public int Kind;
        public bool Aim, Teleported;
    }

    /// <summary>One fixed slot of purely visual state; no simulation reference or RNG.</summary>
    public struct GameplayCharacterMotion
    {
        public FootPlantState FarFoot, NearFoot;
        public SmoothedAimState Aim;
        public float2 PreviousRoot, PreviousVelocity;
        public float Phase, Run, Attack, Hit, Death, Facing, Scale, FarSwingSeconds, NearSwingSeconds;
        public bool Initialized, Airborne, Moving;

        public void Step(in GameplayCharacterInput input, float dt)
        {
            dt = math.clamp(dt, 0, .1f);
            float scale = math.clamp(input.Scale, .1f, 4f);
            float facing = input.Facing < 0 ? -1f : 1f;
            float2 ground = input.Ground / scale;
            bool discontinuity = Initialized && (input.Teleported || math.distancesq(input.Root, PreviousRoot) > scale * scale * 2.25f || math.abs(Scale-scale) > .001f);
            bool reset = !Initialized || discontinuity;
            if (reset)
            {
                this = default; Initialized = true; Facing = facing; Scale = scale;
                Phase = ((uint)input.Handle.Index * 37u % 97) / 97f;
                NaturalMotion.InitializeFoot(ref FarFoot, ground, ground + new float2(-.13f, .075f), 0);
                NaturalMotion.InitializeFoot(ref NearFoot, ground, ground + new float2(.13f, .075f), .5f);
            }
            bool airborne=input.Root.y-input.Ground.y>.03f;
            if(Airborne&&!airborne)
            {
                NaturalMotion.InitializeFoot(ref FarFoot,ground,ground+new float2(-.13f,.075f),0);
                NaturalMotion.InitializeFoot(ref NearFoot,ground,ground+new float2(.13f,.075f),.5f);
                Moving=false;
            }
            Airborne=airborne;
            float response = 1f - math.exp(-18f * dt);
            Run = math.lerp(Run, input.State == GameplayCharacterState.Run ? 1 : 0, response);
            float attack = input.State == GameplayCharacterState.Attack || input.State == GameplayCharacterState.Recovery ? Strike(input.Phase) : 0;
            Attack = math.lerp(Attack, attack, 1f - math.exp(-35f * dt));
            Hit = math.lerp(Hit, input.State == GameplayCharacterState.Hit ? 1 : 0, response);
            Death = math.lerp(Death, input.State == GameplayCharacterState.Death ? 1 : 0, 1f-math.exp(-7f*dt));
            // A bounded walk/run cycle, rather than speeding tiny legs up to 10+ cycles/second.
            // Faster travel shortens contact time; the fixed world-space plant releases before reach is lost.
            float2 velocity=discontinuity?float2.zero:input.Velocity/scale;
            float speed=math.length(velocity),period=speed>.03f?math.clamp(2.7f/speed,.26f,.85f):.85f;
            float stanceSeconds=math.min(period*NaturalMotion.Stance,.52f/math.max(.03f,speed));
            float swingSeconds=period-stanceSeconds;
            bool moving=speed>.03f&&input.State!=GameplayCharacterState.Death;
            Facing=facing;
            if(moving&&!Moving)
            {
                NaturalMotion.InitializeFoot(ref FarFoot,ground,FarFoot.Position,NaturalMotion.Stance*.5f);
                NaturalMotion.InitializeFoot(ref NearFoot,ground,NearFoot.Position,NaturalMotion.Stance);
                FarSwingSeconds=swingSeconds;NearSwingSeconds=math.max(.04f,period*.5f-stanceSeconds*.5f);
                PlanSwing(ref NearFoot,ground,velocity,.13f,NearSwingSeconds,stanceSeconds);
            }
            bool reverse=moving&&Moving&&math.dot(velocity,PreviousVelocity)<.7f*speed*math.length(PreviousVelocity);
            if(reverse)
            {
                ReplanAirFoot(ref FarFoot,ref FarSwingSeconds,ground,velocity,-.13f,stanceSeconds);
                ReplanAirFoot(ref NearFoot,ref NearSwingSeconds,ground,velocity,.13f,stanceSeconds);
            }
            if(moving)
            {
                GuardAirPlan(ref FarFoot,ref FarSwingSeconds,ground,velocity,-.13f,-.1f,facing,stanceSeconds);
                GuardAirPlan(ref NearFoot,ref NearSwingSeconds,ground,velocity,.13f,.1f,facing,stanceSeconds);
                ReleaseBeforeReach(ref FarFoot,ref FarSwingSeconds,ground,velocity,-.13f,-.1f,facing,stanceSeconds,swingSeconds,dt);
                ReleaseBeforeReach(ref NearFoot,ref NearSwingSeconds,ground,velocity,.13f,.1f,facing,stanceSeconds,swingSeconds,dt);
                StepGroundFoot(ref FarFoot,ground,velocity,-.13f,dt,stanceSeconds,ref FarSwingSeconds,swingSeconds,facing,-.1f);
                StepGroundFoot(ref NearFoot,ground,velocity,.13f,dt,stanceSeconds,ref NearSwingSeconds,swingSeconds,facing,.1f);
                Phase=math.frac(Phase+dt/period);
            }
            else
            {
                if(Moving)
                {
                    ReplanAirFoot(ref FarFoot,ref FarSwingSeconds,ground,0,-.13f,stanceSeconds);
                    ReplanAirFoot(ref NearFoot,ref NearSwingSeconds,ground,0,.13f,stanceSeconds);
                }
                Settle(ref FarFoot,ref FarSwingSeconds,ground,-.13f,dt,facing,-.1f);
                Settle(ref NearFoot,ref NearSwingSeconds,ground,.13f,dt,facing,.1f);
                Phase=math.frac(Phase+dt*.16f);
            }
            Moving=moving;PreviousVelocity=velocity;
            float2 target = input.Aim && input.Action!=GameplayCharacterAction.Kick ? input.AimTarget : input.Root + new float2(facing * math.lerp(.34f,.83f,math.max(0,Attack)), math.lerp(1.5f,1.65f,math.max(0,Attack))) * scale;
            NaturalMotion.SmoothAim(ref Aim,target,dt,24);
            PreviousRoot=input.Root;
        }

        // Projected ground has two contact coordinates; swing clearance alone adds screen height.
        static void PlanSwing(ref FootPlantState foot,float2 root,float2 velocity,float offset,float duration,float stance)
        {
            foot.Phase=NaturalMotion.Stance;foot.SwingStart=foot.Position;
            foot.SwingEnd=root+velocity*(duration+stance*.5f)+new float2(offset,.075f);
            foot.PreviousRoot=root;
        }
        static void ReplanAirFoot(ref FootPlantState foot,ref float swing,float2 root,float2 velocity,float offset,float stance)
        {
            if(foot.InStance)return;
            swing=math.clamp(swing*(1-foot.Phase)/(1-NaturalMotion.Stance),.04f,.25f);
            PlanSwing(ref foot,root,velocity,offset,swing,stance);
        }
        static bool ContactEnvelope(FootPlantState foot,float2 root,float facing,float hip)
        {
            float2 d=foot.Position-root-new float2(hip*facing,.54f);
            return math.lengthsq(d)<.82f*.82f;
        }
        static void GuardAirPlan(ref FootPlantState foot,ref float swing,float2 root,float2 velocity,float offset,float hip,float facing,float stance)
        {
            if(foot.InStance)return;
            float remaining=swing*(1-foot.Phase)/(1-NaturalMotion.Stance);
            var landing=foot;landing.Position=foot.SwingEnd;
            if(ContactEnvelope(landing,root+velocity*remaining,facing,hip))return;
            // Turning invalidates a fixed plan, so replan explicitly while airborne. Keep the remaining
            // time (bounded at40ms), rather than restarting full swings and hovering indefinitely.
            ReplanAirFoot(ref foot,ref swing,root,velocity,offset,stance);
        }
        static void ReleaseBeforeReach(ref FootPlantState foot,ref float swing,float2 root,float2 velocity,float offset,float hip,float facing,float stance,float normalSwing,float dt)
        {
            if(!foot.InStance)return;
            // Reach reserve accounts for the next rendered step. Leaving stance does not move the plant
            // or toe: the new swing begins at that exact world-space contact with a zero-slope lift.
            if(ContactEnvelope(foot,root+velocity*math.min(math.max(dt,.0167f),stance),facing,hip))return;
            swing=normalSwing;PlanSwing(ref foot,root,velocity,offset,swing,stance);
        }
        static void StepGroundFoot(ref FootPlantState foot,float2 root,float2 velocity,float offset,float dt,float stance,ref float swing,float normalSwing,float facing,float hip)
        {
            dt=math.clamp(dt,0,.1f);float consumed=0,remaining=dt;
            for(int segment=0;segment<4&&remaining>0;segment++)
            {
                bool planted=foot.InStance;float boundary=planted?NaturalMotion.Stance:1f;
                float rate=planted?NaturalMotion.Stance/math.max(.005f,stance):(1-NaturalMotion.Stance)/math.max(.005f,swing);
                float until=math.max(0,(boundary-foot.Phase)/rate),step=math.min(remaining,until);
                foot.Phase+=step*rate;consumed+=step;remaining-=step;
                if(step>=until-1e-6f)
                {
                    if(planted)
                    {
                        float2 atEvent=math.lerp(foot.PreviousRoot,root,dt>0?consumed/dt:1);
                        foot.Phase=NaturalMotion.Stance;foot.SwingStart=foot.Plant;swing=normalSwing;
                        foot.SwingEnd=atEvent+velocity*(swing+stance*.5f)+new float2(offset,.075f);
                    }
                    else{foot.Phase=0;foot.Plant=foot.SwingEnd;}
                }
            }
            if(foot.InStance)foot.Position=foot.Plant;
            else
            {
                float u=(foot.Phase-NaturalMotion.Stance)/(1-NaturalMotion.Stance);
                foot.Position=math.lerp(foot.SwingStart,foot.SwingEnd,NaturalMotion.Ease(u));
                float arc=math.sin(math.PI*u);foot.Position.y+=NaturalMotion.SwingHeight*arc*arc;
            }
            foot.PreviousRoot=root;
            // An abrupt turn can make a previously planned landing infeasible. It remains a recovery
            // swing instead of claiming a grounded foot the rig cannot reach; the next plan starts here.
            if(foot.InStance&&!ContactEnvelope(foot,root,facing,hip))
            {swing=math.min(.18f,normalSwing);PlanSwing(ref foot,root,velocity,offset,swing,stance);}
        }
        static void Settle(ref FootPlantState foot,ref float swing,float2 ground,float offset,float dt,float facing,float hip)
        {
            if(foot.InStance&&math.distancesq(foot.Position,ground+new float2(offset,.075f))>.35f*.35f)
            {swing=.18f;PlanSwing(ref foot,ground,0,offset,swing,.5f);}
            if(!foot.InStance)StepGroundFoot(ref foot,ground,0,offset,dt,.5f,ref swing,.18f,facing,hip);
            foot.PreviousRoot=ground;
        }

        /// <summary>Normalized authoritative action timeline: wind-up, contact, then recovery.</summary>
        public static float Strike(float phase)
        {
            phase=math.saturate(phase);
            if(phase<.2f)return -.2f*NaturalMotion.Ease(phase/.2f);
            if(phase<.42f)return math.lerp(-.2f,1,NaturalMotion.Ease((phase-.2f)/.22f));
            return 1-NaturalMotion.Ease((phase-.42f)/.58f);
        }

        public static void Pose(in SkeletonView rig,NativeArray<BoneLocal> local,NativeArray<BoneWorld> world,
            in GameplayCharacterInput input,in GameplayCharacterMotion motion,int at)
        {
            float scale=motion.Scale;
            float2 far=motion.FarFoot.Position*scale,near=motion.NearFoot.Position*scale;
            // Feet leave the ground with the authoritative root height. Ground sorting/shadows stay below.
            float height=math.max(0,input.Root.y-input.Ground.y);
            far.y+=height;near.y+=height;
            if(motion.Airborne){far=input.Root+new float2(-motion.Facing*.15f,.27f)*scale;near=input.Root+new float2(motion.Facing*.24f,.17f)*scale;}
            float strike=math.max(0,motion.Attack);
            if(input.Action==GameplayCharacterAction.Kick)
                near=input.Aim?input.AimTarget:math.lerp(near,input.Root+new float2(motion.Facing*.86f,.98f)*scale,strike);
            NaturalCharacterRig.Pose(rig,local,world,input.Root,motion.Facing,scale,motion.Phase,far,near,
                true,motion.Aim.Target,motion.Attack,at);
            var torso=local[at+NaturalCharacterRig.Torso];torso.Rotation+=motion.Hit*.24f;local[at+NaturalCharacterRig.Torso]=torso;
            var head=local[at+NaturalCharacterRig.Head];head.Rotation-=motion.Hit*.15f;local[at+NaturalCharacterRig.Head]=head;
            // A relaxed rear arm versus readable guarded front hand; stance/run changes ease through Run.
            var arm=local[at+NaturalCharacterRig.FarArm];arm.Rotation+=.18f*(1-motion.Run);local[at+NaturalCharacterRig.FarArm]=arm;
            CorrectContacts(rig,local,input,motion,at);
            Skeletal.ToWorld(rig,local,input.Root,motion.Facing,scale,world,at,at);
        }

        /// <summary>Run every render step, including reused secondary-pose ticks. Cached local legs must
        /// never be translated with a moving root during a world-space stance plant.</summary>
        public static void CorrectContacts(in SkeletonView rig,NativeArray<BoneLocal> local,
            in GameplayCharacterInput input,in GameplayCharacterMotion motion,int at)
        {
            float scale=motion.Scale,height=math.max(0,input.Root.y-input.Ground.y);
            float2 far=motion.FarFoot.Position*scale+new float2(0,height),near=motion.NearFoot.Position*scale+new float2(0,height);
            if(motion.Airborne){far=input.Root+new float2(-motion.Facing*.15f,.27f)*scale;near=input.Root+new float2(motion.Facing*.24f,.17f)*scale;}
            if(input.Action==GameplayCharacterAction.Kick)
                near=input.Aim?input.AimTarget:math.lerp(near,input.Root+new float2(motion.Facing*.86f,.98f)*scale,math.max(0,motion.Attack));
            // Solve support height before the legs. Screen Y contains ground depth as well as visual
            // height; a nearly straight bind leg has no spare reach when the actor walks "up" the plane.
            // Lower the pelvis within a bounded .52-model-unit crouch, preserving both exact plants.
            var pelvis=local[at+NaturalCharacterRig.Pelvis];
            float baseHeight=rig.Bones[NaturalCharacterRig.Pelvis].Position.y+.025f*math.cos(motion.Phase*4*math.PI)-.065f*motion.Attack;
            pelvis.Position.y=baseHeight;
            if(!motion.Airborne)
            {
                float support=math.min(SupportHeight(rig,NaturalCharacterRig.FarThigh,NaturalCharacterRig.FarShin,NaturalMotion.ModelPoint(far,input.Root,motion.Facing,scale)),
                    SupportHeight(rig,NaturalCharacterRig.NearThigh,NaturalCharacterRig.NearShin,NaturalMotion.ModelPoint(near,input.Root,motion.Facing,scale)));
                pelvis.Position.y=math.clamp(support,baseHeight-.52f,baseHeight);
            }
            local[at+NaturalCharacterRig.Pelvis]=pelvis;
            NaturalMotion.Aim(rig,local,NaturalCharacterRig.FarThigh,NaturalCharacterRig.FarShin,far,input.Root,motion.Facing,scale,1,at);
            NaturalMotion.Aim(rig,local,NaturalCharacterRig.NearThigh,NaturalCharacterRig.NearShin,near,input.Root,motion.Facing,scale,1,at);
            var foot=local[at+NaturalCharacterRig.FarFoot];foot.Rotation=-local[at+NaturalCharacterRig.FarThigh].Rotation-local[at+NaturalCharacterRig.FarShin].Rotation;local[at+NaturalCharacterRig.FarFoot]=foot;
            foot=local[at+NaturalCharacterRig.NearFoot];foot.Rotation=-local[at+NaturalCharacterRig.NearThigh].Rotation-local[at+NaturalCharacterRig.NearShin].Rotation;local[at+NaturalCharacterRig.NearFoot]=foot;
            NaturalMotion.Aim(rig,local,NaturalCharacterRig.NearArm,NaturalCharacterRig.NearForearm,motion.Aim.Target,input.Root,motion.Facing,scale,-1,at);
        }
        static float SupportHeight(in SkeletonView rig,int upper,int lower,float2 target)
        {
            var hip=rig.Bones[upper];float reach=hip.Length+rig.Bones[lower].Length-.01f;
            float x=target.x-hip.Position.x;
            return target.y+math.sqrt(math.max(0,reach*reach-x*x))-hip.Position.y;
        }

    }
}
