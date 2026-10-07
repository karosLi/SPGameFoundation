using SPF.Contracts;
using SPF.Contracts.Weapons;
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
        public WeaponViewState Weapon;
        public int Kind;
        public bool Aim, Teleported;
    }

    /// <summary>One fixed slot of purely visual state; no simulation reference or RNG.</summary>
    public struct GameplayCharacterMotion
    {
        public FootPlantState FarFoot, NearFoot;
        public SmoothedAimState Aim;
        public float2 PreviousRoot, PreviousVelocity, BodyVelocity, WeaponAim;
        public float Gait, Support, Breath, AimWeight, WeaponWeight, Turn, Acceleration;
        public int WeaponVisualId;
        public float2 HeldGrip, ChangeGrip, HeldGripVelocity, ChangeGripVelocity;
        public float HeldAngle, ChangeAngle, HeldAngleVelocity, ChangeAngleVelocity, EquipAge;
        public float Phase, Run, Attack, Hit, Death, Facing, Scale, FarSwingSeconds, NearSwingSeconds;
        public bool Initialized, Airborne, Moving, WeaponWasActing, ChangingWeapon;

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
                Breath=Phase;Turn=facing;WeaponAim=input.Weapon.Equipped?input.Weapon.AimDirection:new float2(facing,0);
                WeaponWeight=input.Weapon.Equipped?1:0;WeaponVisualId=input.Weapon.VisualId;EquipAge=1;
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
            // Action phase is already a continuous authoritative clock: filtering it delays contact.
            Attack = attack;
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
            Moving=moving;
            float2 oldBody=BodyVelocity;BodyVelocity=math.lerp(BodyVelocity,velocity,1-math.exp(-11f*dt));
            Acceleration=math.lerp(Acceleration,dt>0?math.clamp((BodyVelocity.x-oldBody.x)/dt,-12,12):0,response);
            // Weight transfer and shoulder opposition share the actual two-foot cycle, not a separate oscillator.
            float rawGait=(math.sin(FarFoot.Phase*2*math.PI)-math.sin(NearFoot.Phase*2*math.PI))*.5f;
            Gait=math.lerp(Gait,rawGait*Run,1-math.exp(-24f*dt));
            float support=(FarFoot.InStance?-.045f:0)+(NearFoot.InStance?.045f:0);
            Support=math.lerp(Support,support,1-math.exp(-16f*dt));
            Breath=math.frac(Breath+dt*.29f);
            AimWeight=math.lerp(AimWeight,input.Aim&&!input.Weapon.Equipped?1:0,1-math.exp(-14f*dt));
            WeaponWeight=math.lerp(WeaponWeight,input.Weapon.Equipped?1:0,1-math.exp(-18f*dt));
            Turn=math.lerp(Turn,facing,1-math.exp(-15f*dt));
            float2 desiredAim=input.Weapon.Equipped?input.Weapon.AimDirection:new float2(facing,0);
            if(math.lengthsq(desiredAim)>.0001f)
            {
                float angle=math.atan2(WeaponAim.y,WeaponAim.x),targetAngle=math.atan2(desiredAim.y,desiredAim.x);
                angle+=WeaponMotion.AngleDelta(angle,targetAngle)*(1-math.exp(-18f*dt));
                float length=math.lerp(math.length(WeaponAim),math.length(desiredAim),1-math.exp(-18f*dt));
                WeaponAim=new float2(math.cos(angle),math.sin(angle))*length;
            }
            bool changed=input.Weapon.VisualId!=WeaponVisualId;
            if(changed||WeaponWasActing&&!WeaponMotion.Acting(input.Weapon))
            {ChangeGrip=HeldGrip;ChangeAngle=HeldAngle;ChangeGripVelocity=HeldGripVelocity;ChangeAngleVelocity=HeldAngleVelocity;EquipAge=0;ChangingWeapon=changed;}
            else EquipAge=math.min(1,EquipAge+dt/.2f);
            WeaponWasActing=WeaponMotion.Acting(input.Weapon);WeaponVisualId=input.Weapon.VisualId;PreviousVelocity=velocity;
            var held=WeaponMotion.Sample(input,this);
            if(!reset&&dt>0)
            {HeldGripVelocity=math.clamp((held.Grip-HeldGrip)/dt,new float2(-8),new float2(8));HeldAngleVelocity=math.clamp(WeaponMotion.AngleDelta(HeldAngle,held.Angle)/dt,-15,15);}
            HeldGrip=held.Grip;HeldAngle=held.Angle;
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
            // Cached bind/key layer. Continuous support, body, aim and weapon layers are composed below
            // on every rendered frame, including frames between quality-limited key evaluations.
            for(int i=0;i<NaturalCharacterRig.Bones;i++)
                local[at+i]=new BoneLocal {Position=rig.Bones[i].Position,Rotation=rig.Bones[i].Rotation};
            CorrectContacts(rig,local,input,motion,at);
            Skeletal.ToWorld(rig,local,input.Root,motion.Facing,motion.Scale,world,at,at);
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
            float breathing=math.sin(motion.Breath*2*math.PI);
            float baseHeight=rig.Bones[NaturalCharacterRig.Pelvis].Position.y+.025f*motion.Gait*motion.Gait-.065f*math.max(0,motion.Attack)+.006f*breathing*(1-motion.Run);
            pelvis.Position.x=motion.Support*motion.Turn*motion.Facing;pelvis.Rotation=0;
            pelvis.Position.y=baseHeight;
            if(input.Weapon.Equipped)baseHeight-=.16f*math.saturate(-motion.WeaponAim.y);
            if(!motion.Airborne)
            {
                float support=math.min(SupportHeight(rig,NaturalCharacterRig.FarThigh,NaturalCharacterRig.FarShin,NaturalMotion.ModelPoint(far,input.Root,motion.Facing,scale)-new float2(pelvis.Position.x,0)),
                    SupportHeight(rig,NaturalCharacterRig.NearThigh,NaturalCharacterRig.NearShin,NaturalMotion.ModelPoint(near,input.Root,motion.Facing,scale)-new float2(pelvis.Position.x,0)));
                pelvis.Position.y=math.clamp(support,baseHeight-.52f,baseHeight);
            }
            local[at+NaturalCharacterRig.Pelvis]=pelvis;
            NaturalMotion.Aim(rig,local,NaturalCharacterRig.FarThigh,NaturalCharacterRig.FarShin,far,input.Root,motion.Facing,scale,1,at);
            NaturalMotion.Aim(rig,local,NaturalCharacterRig.NearThigh,NaturalCharacterRig.NearShin,near,input.Root,motion.Facing,scale,1,at);
            var foot=local[at+NaturalCharacterRig.FarFoot];foot.Rotation=-local[at+NaturalCharacterRig.FarThigh].Rotation-local[at+NaturalCharacterRig.FarShin].Rotation;local[at+NaturalCharacterRig.FarFoot]=foot;
            foot=local[at+NaturalCharacterRig.NearFoot];foot.Rotation=-local[at+NaturalCharacterRig.NearThigh].Rotation-local[at+NaturalCharacterRig.NearShin].Rotation;local[at+NaturalCharacterRig.NearFoot]=foot;
            float forward=motion.BodyVelocity.x*motion.Facing;
            float depth=math.clamp(motion.BodyVelocity.y*.02f,-.08f,.08f);
            float lean=math.clamp(forward*.021f,-.12f,.12f);
            float armStride=math.lerp(.58f,.38f,math.saturate(-forward*.25f));
            var weapon=WeaponMotion.Sample(input,motion);
            var torso=local[at+NaturalCharacterRig.Torso];torso.Position=rig.Bones[NaturalCharacterRig.Torso].Position;
            torso.Position.x=-motion.Support*.38f;torso.Position.y+=depth*.18f;
            torso.Rotation=-.035f-lean-depth-.095f*motion.Gait+weapon.Body-.12f*motion.Attack+motion.Hit*.24f;
            torso.Rotation+=.065f*(motion.Turn*motion.Facing-1);
            torso.Rotation-=math.clamp(motion.Acceleration*motion.Facing*.004f,-.045f,.045f);
            local[at+NaturalCharacterRig.Torso]=torso;
            if(input.Weapon.Equipped)
            {
                // A projected-depth step can lower the support pelvis. Let the chest/shoulders reach
                // modestly toward the held target before asking the elbow to lock or the hand to detach.
                float2 shoulder=NaturalMotion.BonePoint(rig,local,NaturalCharacterRig.Torso,0,at)+
                    WeaponMotion.Rotate(new float2(.05f*motion.Turn*motion.Facing,.45f),torso.Rotation);
                float2 delta=NaturalMotion.ModelPoint(weapon.Grip,input.Root,motion.Facing,scale)-shoulder;
                float distance=math.length(delta);
                if(distance>.80f)
                {
                    float2 correction=math.clamp(delta*(1-.80f/math.max(.001f,distance)),new float2(-.18f,-.15f),new float2(.18f,.24f));
                    torso.Position+=correction;local[at+NaturalCharacterRig.Torso]=torso;
                }
            }
            var head=local[at+NaturalCharacterRig.Head];head.Position=rig.Bones[NaturalCharacterRig.Head].Position;head.Position.x*=motion.Turn*motion.Facing;
            head.Rotation=-torso.Rotation*.76f+.028f*motion.Gait+.009f*breathing-motion.Hit*.15f;
            local[at+NaturalCharacterRig.Head]=head;
            var arm=local[at+NaturalCharacterRig.FarArm];arm.Position=rig.Bones[NaturalCharacterRig.FarArm].Position;arm.Position.x*=motion.Turn*motion.Facing;
            arm.Rotation=math.radians(-84)+armStride*motion.Gait+.12f*(1-motion.Run);local[at+NaturalCharacterRig.FarArm]=arm;
            var fore=local[at+NaturalCharacterRig.FarForearm];fore.Rotation=-.34f-.27f*motion.Run-.17f*motion.Gait;local[at+NaturalCharacterRig.FarForearm]=fore;
            arm=local[at+NaturalCharacterRig.NearArm];arm.Position=rig.Bones[NaturalCharacterRig.NearArm].Position;arm.Position.x*=motion.Turn*motion.Facing;
            arm.Rotation=math.radians(-80)-armStride*motion.Gait;local[at+NaturalCharacterRig.NearArm]=arm;
            fore=local[at+NaturalCharacterRig.NearForearm];fore.Rotation=-.36f-.29f*motion.Run+.17f*motion.Gait;local[at+NaturalCharacterRig.NearForearm]=fore;
            // A genuinely relaxed run leaves both arms free. Aim affects only the requested layer.
            float aimWeight=math.max(motion.AimWeight,math.abs(motion.Attack));
            if(!input.Weapon.Equipped&&aimWeight>.001f&&input.Action!=GameplayCharacterAction.Kick)
                NaturalMotion.BlendAim(rig,local,NaturalCharacterRig.NearArm,NaturalCharacterRig.NearForearm,motion.Aim.Target,input.Root,motion.Facing,scale,-1,aimWeight,at);
            WeaponMotion.ApplyArms(rig,local,input,motion,at);
        }
        static float SupportHeight(in SkeletonView rig,int upper,int lower,float2 target)
        {
            var hip=rig.Bones[upper];float reach=hip.Length+rig.Bones[lower].Length-.01f;
            float x=target.x-hip.Position.x;
            return target.y+math.sqrt(math.max(0,reach*reach-x*x))-hip.Position.y;
        }

    }
}
