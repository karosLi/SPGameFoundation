using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    public enum GameplayCharacterState : byte { Idle, Run, Attack, Hit, Death, Recovery, Walk, Dodge, Jump, Knockdown }
    public enum GameplayCharacterAction : byte { Punch, Kick, Cast }

    /// <summary>Read-only view contract. Ground is the contact plane; Root includes any authoritative jump
    /// height. Handle must be stable across swap-back/reordering. Timing and aim are inputs, never damage authority.</summary>
    public struct GameplayCharacterInput
    {
        public EntityHandle Handle;
        public float2 Root, Ground, Velocity, AimTarget;
        public float Facing, Scale, Phase, Flash, Depth;
        /// <summary>Optional additive recoil, independent of an ongoing action. Clamped to [0,1].</summary>
        public float HitWeight;
        public float4 Tint;
        public GameplayCharacterState State;
        public GameplayCharacterAction Action;
        public WeaponViewState Weapon;
        public int MotionProfileId,SkillPoseId;
        public uint SkillPulse;
        public float SkillPhase,SkillWeight;
        public GameplayLocomotionProfile MotionProfile;
        public GameplaySkillPoseProfile SkillProfile;
        public int Kind;
        public bool Aim, Teleported;
    }

    /// <summary>One fixed slot of purely visual state; no simulation reference or RNG.</summary>
    public struct GameplayCharacterMotion
    {
        public FootPlantState FarFoot, NearFoot;
        public SmoothedAimState Aim;
        public float2 PreviousRoot, PreviousVelocity, BodyVelocity, WeaponAim;
        public float Gait, Support, Breath, AimWeight, WeaponWeight, Turn, Acceleration, Move, Walk, SwingHeight;
        public GameplayLocomotionState Locomotion;
        public GameplayLocomotionClassifier LocomotionClassifier;
        public GameplaySkillMotion Skill;
        public int WeaponVisualId;
        public float2 HeldGrip, ChangeGrip, HeldGripVelocity, ChangeGripVelocity;
        public float HeldAngle, ChangeAngle, HeldAngleVelocity, ChangeAngleVelocity, EquipAge;
        public float HeldWeaponBody, ChangeWeaponBody, HeldWeaponBodyVelocity, ChangeWeaponBodyVelocity;
        public float Phase, Run, Attack, Hit, Death, Facing, Scale, FarSwingSeconds, NearSwingSeconds;
        public bool Initialized, Airborne, Moving, WeaponWasActing, ChangingWeapon;
        public float TransferDelay, SupportCeiling;
        public bool NextNearStep;

        public void Step(in GameplayCharacterInput input, float dt)
        {
            dt = math.clamp(dt, 0, .1f);
            float scale = math.clamp(input.Scale, .1f, 4f);
            var profile=GameplayMotionProfiles.Resolve(input);float width=math.clamp(profile.FootWidth,.08f,.2f);
            float facing = input.Facing < 0 ? -1f : 1f;
            width*=facing;
            float2 ground = input.Ground / scale;
            bool discontinuity = Initialized && (input.Teleported || math.distancesq(input.Root, PreviousRoot) > scale * scale * 2.25f || math.abs(Scale-scale) > .001f);
            bool reset = !Initialized || discontinuity;
            if (reset)
            {
                this = default; Initialized = true; Facing = facing; Scale = scale;SupportCeiling=1.15f;
                Phase = ((uint)input.Handle.Index * 37u % 97) / 97f;
                Breath=Phase;Turn=facing;WeaponAim=input.Weapon.Equipped?input.Weapon.AimDirection:new float2(facing,0);
                WeaponWeight=input.Weapon.Equipped?1:0;WeaponVisualId=input.Weapon.VisualId;EquipAge=1;
                NaturalMotion.InitializeFoot(ref FarFoot, ground, ground + new float2(-width, .075f), 0);
                NaturalMotion.InitializeFoot(ref NearFoot, ground, ground + new float2(width, .075f), .5f);
            }
            bool airborne=input.Root.y-input.Ground.y>.03f;
            if(Airborne&&!airborne)
            {
                NaturalMotion.InitializeFoot(ref FarFoot,ground,ground+new float2(-width,.075f),0);
                NaturalMotion.InitializeFoot(ref NearFoot,ground,ground+new float2(width,.075f),.5f);
                Moving=false;
            }
            Airborne=airborne;
            float response = 1f - math.exp(-18f * dt);
            Skill.Step(input,dt);
            float attack = input.State == GameplayCharacterState.Attack || input.State == GameplayCharacterState.Recovery ? Strike(input.Phase) : 0;
            // Action phase is already a continuous authoritative clock: filtering it delays contact.
            Attack = attack;
            float hitWeight=math.isfinite(input.HitWeight)?math.saturate(input.HitWeight):0;
            Hit = math.lerp(Hit, input.State == GameplayCharacterState.Hit ? 1 : hitWeight, response);
            Death = math.lerp(Death, input.State == GameplayCharacterState.Death ? 1 : 0, 1f-math.exp(-7f*dt));
            // A bounded walk/run cycle, rather than speeding tiny legs up to 10+ cycles/second.
            // Faster travel changes cadence while the two legs keep a coordinated transfer.
            float2 velocity=discontinuity?float2.zero:input.Velocity/scale;
            float speed=math.length(velocity);
            bool allowed=input.State!=GameplayCharacterState.Death;
            Locomotion=LocomotionClassifier.Step(speed,profile.RunEnter,profile.RunExit,!Airborne,allowed);
            bool run=LocomotionClassifier.RunLatched;
            bool travel=allowed&&Locomotion!=GameplayLocomotionState.Idle;
            Move=math.lerp(Move,travel?1:0,response);Run=math.lerp(Run,run?1:0,response);Walk=Move*(1-Run);
            SwingHeight=math.clamp(profile.StepHeight,.10f,.28f)*math.lerp(.58f,1,Run)*math.saturate(speed/.8f);
            float period=speed>.03f?math.clamp(math.lerp(profile.WalkStride,profile.RunStride,Run)/speed,
                math.lerp(.44f,math.max(.26f,profile.MinimumPeriod),Run),math.lerp(profile.WalkPeriod,profile.RunPeriod,Run)):profile.WalkPeriod;
            // Contact duty is a gait decision, never an independent reach-time cap. A walk
            // needs overlapping support; a run has one short, intentional flight per transfer.
            float duty=run?.46f:.62f;
            // The full-cycle stride must fit the existing legs. Increase cadence only when the
            // authored speed would otherwise exceed that span; do not silently delete stance.
            period=math.min(period,.80f/math.max(.03f,speed*duty));
            float stanceSeconds=period*duty;
            float swingSeconds=period-stanceSeconds;
            bool moving=speed>.03f&&input.State!=GameplayCharacterState.Death;
            Facing=facing;
            if(moving&&!Moving)
            {
                // Begin with one support and a short placement step. Never release both legs
                // just because the authoritative root began moving in a single fixed tick.
                float2 atStart=FarFoot.PreviousRoot;
                NearSwingSeconds=math.min(swingSeconds,math.max(.065f,SupportTime(FarFoot,atStart,velocity,-.1f,facing)*.8f));
                PlanSwing(ref NearFoot,atStart,velocity,width,NearSwingSeconds,stanceSeconds);
                NextNearStep=false;TransferDelay=0;
            }
            if(moving)
            {
                StepGroundPair(ground,velocity,width,facing,dt,period,stanceSeconds,swingSeconds,run);
                Phase=math.frac(Phase+dt/period);
            }
            else
            {
                if(Moving)
                {
                    ReplanAirFoot(ref FarFoot,ref FarSwingSeconds,ground,0,-width,stanceSeconds);
                    ReplanAirFoot(ref NearFoot,ref NearSwingSeconds,ground,0,width,stanceSeconds);
                }
                Settle(ref FarFoot,ref FarSwingSeconds,ground,-width,dt,facing,-.1f,SwingHeight,NearFoot.InStance);
                Settle(ref NearFoot,ref NearSwingSeconds,ground,width,dt,facing,.1f,SwingHeight,FarFoot.InStance);
                Phase=math.frac(Phase+dt*.16f);
            }
            Moving=moving;
            float2 oldBody=BodyVelocity;BodyVelocity=math.lerp(BodyVelocity,velocity,1-math.exp(-11f*dt));
            Acceleration=math.lerp(Acceleration,dt>0?math.clamp((BodyVelocity.x-oldBody.x)/dt,-12,12):0,response);
            // Weight transfer and shoulder opposition share the actual two-foot cycle, not a separate oscillator.
            float rawGait=(math.sin(FarFoot.Phase*2*math.PI)-math.sin(NearFoot.Phase*2*math.PI))*.5f;
            Gait=math.lerp(Gait,rawGait*Move*math.saturate(speed/1.2f),1-math.exp(-24f*dt));
            float support=(FarFoot.InStance?-profile.WeightShift:0)+(NearFoot.InStance?profile.WeightShift:0);
            Support=math.lerp(Support,support,1-math.exp(-16f*dt));
            Breath=math.frac(Breath+dt*.29f);
            AimWeight=math.lerp(AimWeight,input.Aim&&!input.Weapon.Equipped?1:0,1-math.exp(-14f*dt));
            WeaponWeight=math.lerp(WeaponWeight,input.Weapon.Equipped?1:0,1-math.exp(-18f*dt));
            Turn=math.lerp(Turn,facing,1-math.exp(-15f*dt));
            float ceiling=1.15f,supportX=Support*Turn*Facing;
            if(!Airborne)
            {
                if(FarFoot.InStance||FarFoot.AirSeconds<.04f)ceiling=math.min(ceiling,ContactCeiling(FarFoot,ground,Facing,supportX,-.1f));
                if(NearFoot.InStance||NearFoot.AirSeconds<.04f)ceiling=math.min(ceiling,ContactCeiling(NearFoot,ground,Facing,supportX,.1f));
            }
            // Contact can require immediate compression, but releasing that support must not
            // snap the body upward or pull the just-lifted FK foot away from its world plant.
            SupportCeiling=math.min(ceiling,SupportCeiling+dt*1.2f);
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
            {ChangeGrip=HeldGrip;ChangeAngle=HeldAngle;ChangeGripVelocity=HeldGripVelocity;ChangeAngleVelocity=HeldAngleVelocity;ChangeWeaponBody=HeldWeaponBody;ChangeWeaponBodyVelocity=HeldWeaponBodyVelocity;EquipAge=0;ChangingWeapon=changed;}
            else EquipAge=math.min(1,EquipAge+dt/.2f);
            WeaponWasActing=WeaponMotion.Acting(input.Weapon);WeaponVisualId=input.Weapon.VisualId;PreviousVelocity=velocity;
            var held=WeaponMotion.Sample(input,this);
            if(!reset&&dt>0)
            {HeldGripVelocity=math.clamp((held.Grip-HeldGrip)/dt,new float2(-8),new float2(8));HeldAngleVelocity=math.clamp(WeaponMotion.AngleDelta(HeldAngle,held.Angle)/dt,-15,15);HeldWeaponBodyVelocity=math.clamp((held.Body-HeldWeaponBody)/dt,-3,3);}
            HeldGrip=held.Grip;HeldAngle=held.Angle;HeldWeaponBody=held.Body;
            float2 target = input.Aim && input.Action!=GameplayCharacterAction.Kick ? input.AimTarget : input.Root + new float2(facing * math.lerp(.34f,.83f,math.max(0,Attack)), math.lerp(1.5f,1.65f,math.max(0,Attack))) * scale;
            NaturalMotion.SmoothAim(ref Aim,target,dt,24);
            PreviousRoot=input.Root;
        }

        // Projected ground has two contact coordinates; swing clearance alone adds screen height.
        static void PlanSwing(ref FootPlantState foot,float2 root,float2 velocity,float offset,float duration,float stance)
        {
            foot.Phase=NaturalMotion.Stance;foot.AirSeconds=0;foot.SwingStart=foot.Position;
            foot.SwingEnd=Landing(root,velocity,offset,duration,stance);
        }
        static float2 Landing(float2 root,float2 velocity,float offset,float swing,float stance)
        {
            // Screen Y also represents ground depth. Place the depth support patch above the
            // root's contact baseline, rather than asking a trailing depth foot to pull the
            // entire body down by half a model unit. This is a fixed world-space landing.
            return root+velocity*(swing+stance*.5f)+new float2(offset,.075f+math.abs(velocity.y)*stance*.5f);
        }
        static void ReplanAirFoot(ref FootPlantState foot,ref float swing,float2 root,float2 velocity,float offset,float stance)
        {
            if(foot.InStance)return;
            swing=math.clamp(swing*(1-foot.Phase)/(1-NaturalMotion.Stance),.04f,.25f);
            PlanSwing(ref foot,root,velocity,offset,swing,stance);
        }
        // One coupled transfer controller owns both contacts. Stance phases describe support
        // age; they cannot independently lift a foot. A run releases its support only during the
        // final 4% of the other foot's cycle, keeping flight separate from authoritative Jump.
        void StepGroundPair(float2 root,float2 velocity,float width,float facing,float dt,float period,float stance,float swing,bool run)
        {
            int steps=math.max(1,(int)math.ceil(dt*120));float h=dt/steps;
            float2 from=FarFoot.PreviousRoot;
            for(int step=0;step<12&&step<steps;step++)
            {
                float2 ground=math.lerp(from,root,(step+1)/(float)steps);
                // Acceleration/reversal can shorten the remaining reach of the sole support.
                // Bring its partner down sooner instead of dropping both contacts or the pelvis.
                if(FarFoot.InStance&&!NearFoot.InStance)HurryLanding(ref NearFoot,ref NearSwingSeconds,FarFoot,ground,velocity,-.1f,facing,h);
                if(NearFoot.InStance&&!FarFoot.InStance)HurryLanding(ref FarFoot,ref FarSwingSeconds,NearFoot,ground,velocity,.1f,facing,h);
                GuardAirPlan(ref FarFoot,ref FarSwingSeconds,ground,velocity,-width,stance);
                GuardAirPlan(ref NearFoot,ref NearSwingSeconds,ground,velocity,width,stance);
                bool landed=AdvanceSwing(ref FarFoot,ref FarSwingSeconds,h,SwingHeight)|AdvanceSwing(ref NearFoot,ref NearSwingSeconds,h,SwingHeight);
                if(landed)TransferDelay=math.max(0,stance-period*.5f);
                if(FarFoot.InStance)FarFoot.Phase=math.min(NaturalMotion.Stance-1e-6f,FarFoot.Phase+h*NaturalMotion.Stance/math.max(.005f,stance));
                if(NearFoot.InStance)NearFoot.Phase=math.min(NaturalMotion.Stance-1e-6f,NearFoot.Phase+h*NaturalMotion.Stance/math.max(.005f,stance));
                if(FarFoot.InStance&&NearFoot.InStance)
                {
                    float supportTime=NextNearStep?SupportTime(NearFoot,ground,velocity,.1f,facing):SupportTime(FarFoot,ground,velocity,-.1f,facing);
                    TransferDelay=math.min(TransferDelay-h,supportTime*.8f);
                    if(TransferDelay<=0)
                    {
                        if(NextNearStep){NearSwingSeconds=swing;PlanSwing(ref NearFoot,ground,velocity,width,swing,stance);}
                        else{FarSwingSeconds=swing;PlanSwing(ref FarFoot,ground,velocity,-width,swing,stance);}
                        NextNearStep=!NextNearStep;
                    }
                }
                else if(run)
                {
                    float flight=math.max(0,period*.5f-stance);
                    if(FarFoot.InStance&&Remaining(NearFoot,NearSwingSeconds)<=flight)
                    {FarSwingSeconds=swing;PlanSwing(ref FarFoot,ground,velocity,-width,swing,stance);NextNearStep=true;}
                    else if(NearFoot.InStance&&Remaining(FarFoot,FarSwingSeconds)<=flight)
                    {NearSwingSeconds=swing;PlanSwing(ref NearFoot,ground,velocity,width,swing,stance);NextNearStep=false;}
                }
                FarFoot.PreviousRoot=NearFoot.PreviousRoot=ground;
            }
        }
        static float ContactCeiling(in FootPlantState foot,float2 root,float facing,float support,float hip)
        {
            float x=(foot.Position.x-root.x)*facing-support-hip;
            return foot.Position.y-root.y+math.sqrt(math.max(0,1.1f*1.1f-x*x))+.03f;
        }
        static float Remaining(in FootPlantState foot,float swing)=>foot.InStance?0:swing*(1-foot.Phase)/(1-NaturalMotion.Stance);
        static float SupportTime(in FootPlantState foot,float2 root,float2 velocity,float hip,float facing)
        {
            float2 d=foot.Position-root-new float2(hip*facing,1.01f);
            float speedSq=math.lengthsq(velocity);if(speedSq<.0001f)return 10;
            float along=math.dot(d,velocity);
            float time=math.max(0,(along+math.sqrt(math.max(0,along*along+speedSq*(1.07f*1.07f-math.lengthsq(d)))))/speedSq);
            if(velocity.y<-.001f)time=math.min(time,math.max(0,(.75f-(foot.Position.y-root.y))/-velocity.y));
            return time;
        }
        static void HurryLanding(ref FootPlantState air,ref float swing,in FootPlantState support,float2 root,float2 velocity,float hip,float facing,float dt)
        {
            float remaining=Remaining(air,swing),available=math.max(math.max(dt,.065f-air.AirSeconds),SupportTime(support,root,velocity,hip,facing)*.8f);
            if(remaining>available)swing*=available/math.max(.001f,remaining);
        }
        static void GuardAirPlan(ref FootPlantState foot,ref float swing,float2 root,float2 velocity,float offset,float stance)
        {
            if(foot.InStance)return;
            float remaining=Remaining(foot,swing);
            // A shortened swing or changed root velocity can invalidate the old destination.
            // Retarget in flight without resetting phase or its position at this instant.
            float2 desired=Landing(root,velocity,offset,remaining,stance);
            if(math.distancesq(foot.SwingEnd,desired)<.01f*.01f)return;
            float u=(foot.Phase-NaturalMotion.Stance)/(1-NaturalMotion.Stance),blend=NaturalMotion.Ease(u);
            if(blend>.995f)return;
            foot.SwingStart+=(foot.SwingEnd-desired)*(blend/math.max(.005f,1-blend));
            foot.SwingEnd=desired;
        }
        static bool AdvanceSwing(ref FootPlantState foot,ref float swing,float dt,float lift)
        {
            if(foot.InStance){foot.Position=foot.Plant;return false;}
            foot.AirSeconds+=dt;
            foot.Phase+=dt*(1-NaturalMotion.Stance)/math.max(.005f,swing);
            if(foot.Phase>=1)
            {foot.Phase=0;foot.Plant=foot.Position=foot.SwingEnd;return true;}
            float u=(foot.Phase-NaturalMotion.Stance)/(1-NaturalMotion.Stance);
            foot.Position=math.lerp(foot.SwingStart,foot.SwingEnd,NaturalMotion.Ease(u));
            float arc=math.sin(math.PI*u);foot.Position.y+=lift*arc*arc;return false;
        }
        static void Settle(ref FootPlantState foot,ref float swing,float2 ground,float offset,float dt,float facing,float hip,float lift,bool allowLift)
        {
            if(allowLift&&foot.InStance&&math.distancesq(foot.Position,ground+new float2(offset,.075f))>.35f*.35f)
            {swing=.18f;PlanSwing(ref foot,ground,0,offset,swing,.5f);}
            AdvanceSwing(ref foot,ref swing,dt,lift);foot.PreviousRoot=ground;
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
            var profile=GameplayMotionProfiles.Resolve(input);var skill=motion.Skill.Pose;
            float skillBodyWeight=1-WeaponMotion.ActionWeight(input.Weapon);
            float scale=motion.Scale,height=math.max(0,input.Root.y-input.Ground.y);
            float2 far=motion.FarFoot.Position*scale+new float2(0,height),near=motion.NearFoot.Position*scale+new float2(0,height);
            if(motion.Airborne){far=input.Root+new float2(-motion.Facing*(.15f+skill.FootTuck*.3f),.27f+skill.FootTuck)*scale;near=input.Root+new float2(motion.Facing*(.24f-skill.FootTuck*.3f),.17f+skill.FootTuck*.65f)*scale;}
            if(input.Action==GameplayCharacterAction.Kick)
                near=input.Aim?input.AimTarget:math.lerp(near,input.Root+new float2(motion.Facing*.86f,.98f)*scale,math.max(0,motion.Attack));
            // Solve actual support before the legs. A swing cannot pull the whole body down.
            // The cached ceiling only preserves the brief toe-off contact and eases recovery from
            // an exceptional reversal compression; the moving footprint is planned to fit the rig.
            var pelvis=local[at+NaturalCharacterRig.Pelvis];
            float breathing=math.sin(motion.Breath*2*math.PI);
            float baseHeight=rig.Bones[NaturalCharacterRig.Pelvis].Position.y-.045f*motion.Move+.015f*motion.Gait*motion.Gait*math.lerp(.6f,1.25f,motion.Run)-.065f*math.max(0,motion.Attack)+profile.Breath*breathing*(1-motion.Move)-skill.PelvisDrop*skillBodyWeight;
            pelvis.Position.x=motion.Support*motion.Turn*motion.Facing;pelvis.Rotation=0;
            pelvis.Position.y=baseHeight;
            if(input.Weapon.Equipped)baseHeight-=.16f*math.saturate(-motion.WeaponAim.y);
            if(!motion.Airborne)
            {
                float support=math.min(baseHeight,motion.SupportCeiling);
                if(motion.FarFoot.InStance)support=math.min(support,SupportHeight(rig,NaturalCharacterRig.FarThigh,NaturalCharacterRig.FarShin,NaturalMotion.ModelPoint(far,input.Root,motion.Facing,scale)-new float2(pelvis.Position.x,0)));
                if(motion.NearFoot.InStance)support=math.min(support,SupportHeight(rig,NaturalCharacterRig.NearThigh,NaturalCharacterRig.NearShin,NaturalMotion.ModelPoint(near,input.Root,motion.Facing,scale)-new float2(pelvis.Position.x,0)));
                pelvis.Position.y=math.clamp(support,baseHeight-.52f,baseHeight);
            }
            local[at+NaturalCharacterRig.Pelvis]=pelvis;
            NaturalMotion.Aim(rig,local,NaturalCharacterRig.FarThigh,NaturalCharacterRig.FarShin,far,input.Root,motion.Facing,scale,1,at);
            NaturalMotion.Aim(rig,local,NaturalCharacterRig.NearThigh,NaturalCharacterRig.NearShin,near,input.Root,motion.Facing,scale,1,at);
            var foot=local[at+NaturalCharacterRig.FarFoot];foot.Rotation=-local[at+NaturalCharacterRig.FarThigh].Rotation-local[at+NaturalCharacterRig.FarShin].Rotation;local[at+NaturalCharacterRig.FarFoot]=foot;
            foot=local[at+NaturalCharacterRig.NearFoot];foot.Rotation=-local[at+NaturalCharacterRig.NearThigh].Rotation-local[at+NaturalCharacterRig.NearShin].Rotation;local[at+NaturalCharacterRig.NearFoot]=foot;
            float forward=motion.BodyVelocity.x*motion.Facing;
            float depth=math.clamp(motion.BodyVelocity.y*.02f,-.08f,.08f);
            float lean=math.clamp(forward*profile.Lean,-.12f,.12f);
            float armStride=math.lerp(profile.WalkArm,profile.RunArm,motion.Run)*math.lerp(1,.76f,math.saturate(-forward*.25f));
            var weapon=WeaponMotion.Sample(input,motion);
            var torso=local[at+NaturalCharacterRig.Torso];torso.Position=rig.Bones[NaturalCharacterRig.Torso].Position;
            torso.Position.x=-motion.Support*.38f;torso.Position.y+=depth*.18f;
            torso.Rotation=-.035f-lean-depth-profile.BodySway*motion.Gait+weapon.Body+skill.Body*skillBodyWeight-.12f*motion.Attack+motion.Hit*.24f*profile.HitRecoil;
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
            head.Rotation=-torso.Rotation*.76f+.028f*motion.Gait+profile.Breath*1.3f*breathing-motion.Hit*.15f*profile.HitRecoil+skill.Head;
            local[at+NaturalCharacterRig.Head]=head;
            var arm=local[at+NaturalCharacterRig.FarArm];arm.Position=rig.Bones[NaturalCharacterRig.FarArm].Position;arm.Position.x*=motion.Turn*motion.Facing;
            arm.Rotation=math.radians(-84)+armStride*motion.Gait+.12f*(1-motion.Move);local[at+NaturalCharacterRig.FarArm]=arm;
            var fore=local[at+NaturalCharacterRig.FarForearm];fore.Rotation=-.34f-.18f*motion.Move-.09f*motion.Run-.17f*motion.Gait;local[at+NaturalCharacterRig.FarForearm]=fore;
            arm=local[at+NaturalCharacterRig.NearArm];arm.Position=rig.Bones[NaturalCharacterRig.NearArm].Position;arm.Position.x*=motion.Turn*motion.Facing;
            arm.Rotation=math.radians(-80)-armStride*motion.Gait;local[at+NaturalCharacterRig.NearArm]=arm;
            fore=local[at+NaturalCharacterRig.NearForearm];fore.Rotation=-.36f-.20f*motion.Move-.09f*motion.Run+.17f*motion.Gait;local[at+NaturalCharacterRig.NearForearm]=fore;
            // A genuinely relaxed run leaves both arms free. Aim affects only the requested layer.
            float aimWeight=math.max(motion.AimWeight,math.abs(motion.Attack));
            if(!input.Weapon.Equipped&&aimWeight>.001f&&input.Action!=GameplayCharacterAction.Kick)
                NaturalMotion.BlendAim(rig,local,NaturalCharacterRig.NearArm,NaturalCharacterRig.NearForearm,motion.Aim.Target,input.Root,motion.Facing,scale,-1,aimWeight,at);
            WeaponMotion.ApplyArms(rig,local,input,motion,at);
            if(!input.Weapon.Equipped&&skill.NearWeight>.001f)
                NaturalMotion.BlendAim(rig,local,NaturalCharacterRig.NearArm,NaturalCharacterRig.NearForearm,input.Root+new float2(skill.NearHand.x*motion.Facing,skill.NearHand.y)*scale,input.Root,motion.Facing,scale,-1,skill.NearWeight,at);
            float offHand=skill.FarWeight*(input.Weapon.Equipped?WeaponMotion.SupportRelease(input,motion):1);
            if(offHand>.001f)
                NaturalMotion.BlendAim(rig,local,NaturalCharacterRig.FarArm,NaturalCharacterRig.FarForearm,input.Root+new float2(skill.FarHand.x*motion.Facing,skill.FarHand.y)*scale,input.Root,motion.Facing,scale,-1,offHand,at);
        }
        static float SupportHeight(in SkeletonView rig,int upper,int lower,float2 target)
        {
            var hip=rig.Bones[upper];float reach=hip.Length+rig.Bones[lower].Length-.01f;
            float x=target.x-hip.Position.x;
            return target.y+math.sqrt(math.max(0,reach*reach-x*x))-hip.Position.y;
        }

    }
}
