using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    /// <summary>Presentation sockets after FK. Never feed these back into damage or projectile simulation.</summary>
    public struct WeaponAttachmentSample
    {
        public float2 PrimaryGrip, SupportGrip, Muzzle, Tip, Direction;
        public float Rotation, Draw, Visibility, SupportWeight;
        public int VisualId;
        public uint ActionPulse, CueSequence;
        public WeaponCueKind Cues;
    }

    public struct WeaponPose
    {
        public float2 Grip, Support;
        public float Angle, Length, Draw, Weight, Visibility, Body, StringWeight;
    }

    /// <summary>Small authored pose library. Hermite tangents pass through contact instead of stopping at
    /// every key. Whole-action phase and release markers are authoritative; only aim/hold transitions ease.</summary>
    public static class WeaponMotion
    {
        const float MarkerEpsilon=.00001f;
        public static bool Acting(in WeaponViewState w)=>w.Stage==WeaponStage.Windup||w.Stage==WeaponStage.Active||w.Stage==WeaponStage.Recovery;
        public static float ActionWeight(in WeaponViewState w)
        {
            if(!Acting(w))return 0;
            float marker=(w.Family==WeaponActionFamily.Cast||w.Family==WeaponActionFamily.Draw)?w.ReleasePhase:w.ContactPhase;
            marker=math.clamp(marker,MarkerEpsilon,1-2*MarkerEpsilon);
            float end=math.clamp(w.ActiveEndPhase,marker+MarkerEpsilon,1-MarkerEpsilon);
            return NaturalMotion.Ease(w.Phase/marker)*(1-NaturalMotion.Ease((w.Phase-end)/math.max(MarkerEpsilon,1-end)));
        }
        public static float SupportRelease(in GameplayCharacterInput input,in GameplayCharacterMotion motion)=>
            math.saturate(motion.Skill.Pose.SupportRelease*(1-ActionWeight(input.Weapon)));
        public static float Hermite(float a,float b,float ta,float tb,float u,float duration)
        {
            u=math.saturate(u);float u2=u*u,u3=u2*u;
            return (2*u3-3*u2+1)*a+(u3-2*u2+u)*duration*ta+(-2*u3+3*u2)*b+(u3-u2)*duration*tb;
        }
        public static float Curve(float phase,float contact,float activeEnd,float rest,float windup,float impact,float follow,float windupFraction=.55f)
        {
            contact=math.clamp(contact,MarkerEpsilon,1-2*MarkerEpsilon);activeEnd=math.clamp(activeEnd,contact+MarkerEpsilon,1-MarkerEpsilon);
            float t1=contact*math.clamp(windupFraction,.25f,.75f),t2=contact,t3=activeEnd;
            float v1=(impact-rest)/t2,v2=(follow-windup)/(t3-t1),v3=(rest-impact)/(1-t2);
            if(phase<t1)return Hermite(rest,windup,0,v1,phase/t1,t1);
            if(phase<t2)return Hermite(windup,impact,v1,v2,(phase-t1)/(t2-t1),t2-t1);
            if(phase<t3)return Hermite(impact,follow,v2,v3,(phase-t2)/(t3-t2),t3-t2);
            return Hermite(follow,rest,v3,0,(phase-t3)/(1-t3),1-t3);
        }
        // Blade-only braking: preserve the incoming curve/contact tangent, then reduce the
        // whole chain's velocity to zero on one shared clock. The settled key is derived
        // from that tangent; a bounded vertical carry lets the forearm finish the cut
        // instead of leaving a nearly level forearm with a long wrist-only flourish.
        // This uses the same safe trajectory for hit and miss; phase is not a hit fact.
        static float BladeCurve(float phase,float contact,float activeEnd,float rest,float windup,float impact,float follow,float windupFraction=.5f,float finishScale=1)
        {
            if(phase<=contact)return Curve(phase,contact,activeEnd,rest,windup,impact,follow,windupFraction);
            float duration=(activeEnd-contact)*.45f,settle=contact+duration;
            float tangent=(follow-windup)/(activeEnd-contact*windupFraction);
            float finish=impact+tangent*duration*.5f*finishScale;
            if(phase<settle)return Hermite(impact,finish,tangent,0,(phase-contact)/duration,duration);
            return Hermite(finish,rest,0,0,(phase-settle)/(1-settle),1-settle);
        }
        public static float2 Rotate(float2 p,float angle)
        {float c=math.cos(angle),s=math.sin(angle);return new float2(c*p.x-s*p.y,s*p.x+c*p.y);}
        public static float AngleDelta(float from,float to)=>math.atan2(math.sin(to-from),math.cos(to-from));
        public static float2 WorldOffset(in GameplayCharacterInput input,float2 aim,float2 offset)=>input.Root+(aim*offset.x+new float2(0,offset.y))*input.Scale;
        public static WeaponPose Sample(in GameplayCharacterInput input,in GameplayCharacterMotion motion)
        {
            var w=input.Weapon;if(!w.Equipped)return default;
            bool acting=Acting(w);float phase=acting?math.saturate(w.Phase):0;
            float impactMarker=(w.Family==WeaponActionFamily.Cast||w.Family==WeaponActionFamily.Draw)?w.ReleasePhase:w.ContactPhase;
            float contact=math.clamp(impactMarker,MarkerEpsilon,1-2*MarkerEpsilon),end=math.clamp(w.ActiveEndPhase,contact+MarkerEpsilon,1-MarkerEpsilon);
            float reachWeight=acting?NaturalMotion.Ease(phase/contact)*(1-NaturalMotion.Ease((phase-end)/math.max(MarkerEpsilon,1-end))):0;
            float2 aim=math.lerp(motion.WeaponAim,w.AimDirection,acting?NaturalMotion.Ease(phase/contact):0);
            if(math.lengthsq(aim)<.01f)aim=new float2(motion.Facing,0);
            // Preserve projected ground-aim length. It encodes belt depth foreshortening.
            float facing=motion.Facing;
            float2 canonical=WorldOffset(input,aim,w.GripOffset);
            float2 muzzle=WorldOffset(input,aim,w.MuzzleOffset);
            float angle=math.atan2(muzzle.y-canonical.y,muzzle.x-canonical.x),canonicalAngle=angle;
            float2 offset=0;float rotation=0,body=0,draw=0;
            switch(w.Family)
            {
                case WeaponActionFamily.Slash:
                    // Preserve the loaded key and exact canonical contact, then brake grip,
                    // blade and chest together into a compact low finish before returning to guard.
                    // Extra vertical carry changes only this terminal key, not the overhead arc;
                    // the contact position/velocity and the exact shared settling time stay fixed.
                    offset=new float2(BladeCurve(phase,contact,end,-.23f,-.13f,0,.04f),BladeCurve(phase,contact,end,.16f,.40f,0,-.06f,.5f,2));
                    rotation=BladeCurve(phase,contact,end,.70f,1.30f,0,-.58f);
                    body=BladeCurve(phase,contact,end,0,.12f,-.10f,-.09f,.35f);break;
                case WeaponActionFamily.Thrust:
                    // A rearward chamber stays near the thrust line. The shoulder loads first;
                    // the elbow then opens into contact, followed by a small settle and retraction.
                    // Raising this hand to shoulder height creates a vertical pump instead of a thrust.
                    offset=new float2(Curve(phase,contact,end,-.22f,-.44f,0,.025f),Curve(phase,contact,end,.08f,.05f,0,-.02f));
                    rotation=Curve(phase,contact,end,.16f,.05f,0,-.025f);
                    body=Curve(phase,contact,end,0,.07f,-.045f,-.035f,.35f);break;
                case WeaponActionFamily.Cast:
                    // Gather the staff with both arms, lift, and direct the cast from the chest.
                    offset=new float2(Curve(phase,contact,end,-.12f,-.22f,0,-.08f),Curve(phase,contact,end,-.02f,.58f,0,.10f));
                    rotation=Curve(phase,contact,end,.78f,.90f,0,-.13f);body=Curve(phase,contact,end,0,.12f,-.09f,-.06f);break;
                case WeaponActionFamily.Draw:
                    // Draw remains taut until the simulation's exact release marker. Recoil then settles.
                    float release=math.clamp(w.ReleasePhase,MarkerEpsilon,1-MarkerEpsilon);
                    draw=acting?(phase<release?NaturalMotion.Ease(phase/release):1-NaturalMotion.Ease((phase-release)/.09f)):0;
                    // Raise before drawing; settle the bow arm at release rather than rotating it
                    // like a melee swing. This lobe vanishes at the exact release socket.
                    float raise=acting?math.max(0,Curve(phase,release,math.max(end,release+MarkerEpsilon),0,.30f,0,0)):0;
                    offset=new float2(-.11f*(1-reachWeight),.035f*(1-reachWeight)+raise);
                    rotation=.10f*(1-reachWeight);body=.10f*draw;break;
            }
            // Locomotion only perturbs the relaxed hold; contact/release stays on the canonical socket.
            float relaxed=1-reachWeight;
            offset+=new float2(motion.Support*.25f,.022f*motion.Gait)*relaxed;
            // A guarded hand follows the weight transfer by a few degrees. Action weight
            // removes this contribution exactly at authoritative contact/release.
            rotation+=.055f*motion.Gait*relaxed;
            float equip=w.Stage==WeaponStage.Equipping?NaturalMotion.Ease(w.StagePhase):0;
            offset+=new float2(-.12f,-.48f)*equip;
            rotation-=.8f*equip;
            // A vertical aim otherwise drives the lifted wrist directly through its shoulder.
            // Give the chamber a small forward arc in character space; it is zero at release/contact.
            float chamber=acting?math.sin(math.PI*math.saturate(phase/contact)):0;chamber*=chamber;
            float verticalAim=1-math.saturate(math.abs(aim.x));
            float followArc=acting?math.sin(math.PI*math.saturate((phase-contact)/(1-contact))):0;followArc*=followArc;
            float sideArc=verticalAim*(.48f*chamber+(w.Family==WeaponActionFamily.Slash?.42f:.30f)*followArc);
            offset.y*=1-.30f*verticalAim;
            float2 grip=canonical+(aim*offset.x+new float2(facing*sideArc,offset.y))*input.Scale;
            angle+=rotation*facing;
            var skill=motion.Skill.Pose;
            float2 skillGrip=input.Root+new float2(skill.NearHand.x*facing,skill.NearHand.y)*input.Scale;
            grip=math.lerp(grip,skillGrip,skill.NearWeight*relaxed);
            angle+=skill.WeaponAngle*facing*relaxed;
            float transition=NaturalMotion.Ease(motion.EquipAge);
            if(motion.EquipAge<1&&(!acting||phase<contact))
            {
                // Preserve outgoing hand/angular velocity. This is a bounded hand-space inertial
                // transition, not independently damped IK joints or a claim of full-body inertialization.
                float blendTime=acting?math.max(motion.EquipAge,phase/contact):motion.EquipAge;
                grip=new float2(Hermite(motion.ChangeGrip.x,grip.x,motion.ChangeGripVelocity.x,0,blendTime,.2f),
                    Hermite(motion.ChangeGrip.y,grip.y,motion.ChangeGripVelocity.y,0,blendTime,.2f));
                angle=Hermite(motion.ChangeAngle,motion.ChangeAngle+AngleDelta(motion.ChangeAngle,angle),motion.ChangeAngleVelocity,0,blendTime,.2f);
                body=Hermite(motion.ChangeWeaponBody,body,motion.ChangeWeaponBodyVelocity,0,blendTime,.2f);
            }
            float length=math.distance(canonical,muzzle)/math.max(.001f,input.Scale);
            float2 support;float stringWeight=1;
            if(w.Family==WeaponActionFamily.Draw)
            {
                // The string recoils quickly but the drawing hand follows through near the face
                // before relaxing. Do not force that hand to traverse the shoulder with the string.
                float release=math.clamp(w.ReleasePhase,MarkerEpsilon,1-MarkerEpsilon);
                float handDraw=acting&&phase>=release?1-NaturalMotion.Ease((phase-release)/(1-release)):draw;
                support=grip+math.lerp(-aim*.10f*input.Scale,WorldOffset(input,aim,w.SecondaryGripOffset)-canonical,handDraw);
                stringWeight=handDraw>MarkerEpsilon?math.saturate(draw/handDraw):0;
            }
            else if(WeaponArt.Resolve(w.VisualId,w.Family)==1002)
            {
                // Sword art has its primary palm at (46,48), rear palm at (24,48), and
                // grip-to-tip length 198 pixels. The generic simulation secondary socket lies
                // outside this drawn handle. Correct only its visual hand anchor, never content/ABI.
                support=grip+Rotate(new float2(-22f*length/198f*input.Scale,0),angle);
            }
            else
                support=grip+Rotate(WorldOffset(input,aim,w.SecondaryGripOffset)-canonical,angle-canonicalAngle);
            return new WeaponPose {Grip=grip,Support=support,Angle=angle,Length=length,Draw=draw,
                StringWeight=stringWeight,Weight=motion.WeaponWeight,Visibility=math.max(.04f,(1-equip)*(motion.ChangingWeapon?transition:1)),Body=body};
        }

        public static void ApplyArms(in SkeletonView rig,NativeArray<BoneLocal> local,in GameplayCharacterInput input,
            in GameplayCharacterMotion motion,int at)
        {
            var pose=Sample(input,motion);if(!input.Weapon.Equipped)return;
            // Final IK consumes the phase-authored hand trajectory. It must never replace
            // that trajectory with an idle socket, which would erase anticipation/arm lift.
            float2 primaryShoulder=NaturalMotion.BonePoint(rig,local,NaturalCharacterRig.NearArm,0,at);
            float primaryDistance=math.distance(NaturalMotion.ModelPoint(pose.Grip,input.Root,motion.Facing,motion.Scale),primaryShoulder);
            float primaryFold=1-NaturalMotion.Ease((primaryDistance-.12f)/.22f);
            if(primaryFold>.001f)
            {
                var primaryArm=local[at+NaturalCharacterRig.NearArm];
                primaryArm.Position+=Rotate(new float2(-.10f*primaryFold,0),-local[at+NaturalCharacterRig.Pelvis].Rotation-local[at+NaturalCharacterRig.Torso].Rotation);
                local[at+NaturalCharacterRig.NearArm]=primaryArm;
            }
            NaturalMotion.BlendAim(rig,local,NaturalCharacterRig.NearArm,NaturalCharacterRig.NearForearm,pose.Grip,input.Root,motion.Facing,motion.Scale,-1,pose.Weight,at);
            // The wrist is a separate degree of freedom from hand position. Keep a bounded
            // grip angle instead of inheriting the forearm rotation at every attack phase.
            float forearm=local[at+NaturalCharacterRig.Pelvis].Rotation+local[at+NaturalCharacterRig.Torso].Rotation+
                local[at+NaturalCharacterRig.NearArm].Rotation+local[at+NaturalCharacterRig.NearForearm].Rotation;
            float2 worldDirection=new float2(math.cos(pose.Angle)*motion.Facing,math.sin(pose.Angle));
            float gripAngle=math.atan2(worldDirection.y,worldDirection.x);
            var wrist=local[at+NaturalCharacterRig.Hand];
            float wristDelta=AngleDelta(forearm,gripAngle);
            // A grip directly behind the forearm cannot be reached by a human wrist. Ease
            // toward neutral there instead of snapping between opposite joint limits at ±pi.
            float wristReach=1-NaturalMotion.Ease((math.abs(wristDelta)-2f)/(math.PI-2f));
            wrist.Rotation=math.clamp(wristDelta,-.85f,.85f)*pose.Weight*wristReach;
            local[at+NaturalCharacterRig.Hand]=wrist;
            if(input.Weapon.Family!=WeaponActionFamily.Slash)
            {
                // The secondary target follows the solved dominant hand, so both hands remain attached
                // even when the full-body reach clamp is active near an obstacle or during a turn.
                float2 actual=NaturalMotion.BonePoint(rig,local,NaturalCharacterRig.Hand,0,at);
                actual=input.Root+new float2(actual.x*motion.Facing,actual.y)*motion.Scale;
                float2 support=pose.Support+actual-pose.Grip;
                float2 shoulder=NaturalMotion.BonePoint(rig,local,NaturalCharacterRig.FarArm,0,at);
                float2 reach=NaturalMotion.ModelPoint(support,input.Root,motion.Facing,motion.Scale)-shoulder;
                float distance=math.length(reach);
                // A folding two-hand grip needs scapular room before the elbow approaches its
                // inner singularity. The bounded glide eases in/out and never shifts the hand/socket.
                float fold=1-NaturalMotion.Ease((distance-.12f)/.22f);
                if(fold>.001f)
                {
                    var supportArm=local[at+NaturalCharacterRig.FarArm];
                    supportArm.Position+=Rotate(new float2(-.14f*fold,0),-local[at+NaturalCharacterRig.Pelvis].Rotation-local[at+NaturalCharacterRig.Torso].Rotation);
                    local[at+NaturalCharacterRig.FarArm]=supportArm;
                    shoulder=NaturalMotion.BonePoint(rig,local,NaturalCharacterRig.FarArm,0,at);
                    reach=NaturalMotion.ModelPoint(support,input.Root,motion.Facing,motion.Scale)-shoulder;distance=math.length(reach);
                }
                if(distance>.82f||distance<.035f)
                {
                    // A small scapular glide keeps the target inside the reachable annulus: unequal
                    // arm lengths also have a nonzero inner radius when the hand folds to the shoulder.
                    float2 direction=distance>.00001f?reach/distance:new float2(0,-1);
                    float2 slide=distance>.82f?direction*math.min(.035f,distance-.82f):-direction*(.035f-distance);
                    var arm=local[at+NaturalCharacterRig.FarArm];
                    arm.Position+=Rotate(slide,-local[at+NaturalCharacterRig.Pelvis].Rotation-local[at+NaturalCharacterRig.Torso].Rotation);local[at+NaturalCharacterRig.FarArm]=arm;
                }
                NaturalMotion.BlendAim(rig,local,NaturalCharacterRig.FarArm,NaturalCharacterRig.FarForearm,support,input.Root,motion.Facing,motion.Scale,-1,pose.Weight*(1-SupportRelease(input,motion)),at);
            }
        }
        public static WeaponAttachmentSample Attach(in GameplayCharacterInput input,in GameplayCharacterMotion motion,
            NativeArray<BoneWorld> world,int at)
        {
            if(!input.Weapon.Equipped)return default;
            var p=Sample(input,motion);var w=input.Weapon;
            float2 grip=world[at+NaturalCharacterRig.Hand].Position;
            float2 support=world[at+NaturalCharacterRig.FarForearm].Transform(new float2(.42f*motion.Scale,0),motion.Facing);
            float2 direction=new float2(math.cos(p.Angle),math.sin(p.Angle));
            float2 tip=grip+direction*p.Length*motion.Scale;
            return new WeaponAttachmentSample {PrimaryGrip=grip,SupportGrip=support,Muzzle=tip,Tip=tip,Direction=direction,
                Rotation=p.Angle,Draw=p.Draw,Visibility=p.Visibility,SupportWeight=(1-SupportRelease(input,motion))*p.StringWeight,VisualId=WeaponArt.Resolve(w.VisualId,w.Family),ActionPulse=w.ActionPulse,CueSequence=w.CueSequence,Cues=w.Cues};
        }
    }
}
