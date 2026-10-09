using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using SPF.L2.Weapons;
using SPF.Presentation.Animation;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class BladeContinuousTransitionTests
    {
        static GameplayCharacterInput Input(int face=1)
        {
            var p=WeaponProfiles.CreateDefaults(60)[0];
            return new GameplayCharacterInput
            {
                Handle=new EntityHandle(7,1),Facing=face,Scale=.8f,
                Weapon=new WeaponViewState
                {
                    ContentId=p.ContentId,VisualId=p.VisualId,Family=p.Family,Stage=WeaponStage.Idle,
                    AimDirection=new float2(face,0),GripOffset=p.GripOffset,SecondaryGripOffset=p.SecondaryGripOffset,
                    MuzzleOffset=p.MuzzleOffset,ContactPhase=p.Active.From/(float)p.DurationTicks,
                    ActiveEndPhase=p.Active.Until/(float)p.DurationTicks,ReleasePhase=p.ReleaseTick/(float)p.DurationTicks
                }
            };
        }

        static void Action(ref GameplayCharacterInput input,float phase,uint pulse=1)
        {
            input.State=GameplayCharacterState.Attack;input.Phase=input.Weapon.Phase=phase;
            input.Weapon.ActionPulse=pulse;
            input.Weapon.Stage=phase<input.Weapon.ContactPhase?WeaponStage.Windup:
                phase<input.Weapon.ActiveEndPhase?WeaponStage.Active:WeaponStage.Recovery;
        }

        static GameplayCharacterMotion Warm(in GameplayCharacterInput input)
        {
            var motion=default(GameplayCharacterMotion);
            for(int i=0;i<120;i++)motion.Step(input,1f/120);
            return motion;
        }

        static void SamePose(in WeaponPose before,in WeaponPose after,string context)
        {
            Assert.Less(math.distance(before.Grip,after.Grip),.00001f,"grip "+context);
            Assert.Less(math.abs(WeaponMotion.AngleDelta(before.Angle,after.Angle)),.00001f,"angle "+context);
            Assert.That(after.Body,Is.EqualTo(before.Body).Within(.00001f),"body "+context);
            Assert.That(after.ArmBend,Is.EqualTo(before.ArmBend).Within(.00001f),"arm ownership "+context);
        }

        [TestCase(-1)] [TestCase(1)]
        public void CancelledBladeFollowsLiveRootInsteadOfFrozenWorldAnchor(int face)
        {
            foreach(float phase in new[]{.08f,.22f,.5f,.8f})
            {
                var input=Input(face);var motion=Warm(input);
                Action(ref input,phase);motion.Step(input,1f/60);
                var before=WeaponMotion.Sample(input,motion);
                input.Weapon.Stage=WeaponStage.Idle;input.State=GameplayCharacterState.Run;
                motion.Step(input,0);SamePose(before,WeaponMotion.Sample(input,motion),"cancel "+phase);
                foreach(float elapsed in new[]{0f,.04f,.10f,.19f})
                {
                    var at=motion;at.BladeTransition.Age=elapsed;
                    var original=WeaponMotion.Sample(input,at);
                    var translated=input;float2 travel=new float2(.031f*face,.023f);translated.Root+=travel;
                    var moved=WeaponMotion.Sample(translated,at);
                    Assert.Less(math.distance(moved.Grip-original.Grip,travel),.000001f,"entire live target follows root at age "+elapsed);
                    Assert.Less(math.abs(WeaponMotion.AngleDelta(moved.Angle,original.Angle)),.000001f);
                }
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void BufferedPulseAndPhaseRewindRetainOutgoingPose(bool incrementPulse)
        {
            for(int face=-1;face<=1;face+=2)
            {
                var input=Input(face);var motion=Warm(input);
                for(int i=0;i<60;i++){Action(ref input,.8f*i/59);motion.Step(input,.64f*.8f/59);}
                var before=WeaponMotion.Sample(input,motion);
                Action(ref input,0,incrementPulse?2u:1u);motion.Step(input,0);
                Assert.IsTrue(motion.BladeTransition.Active,"restart captures without an intervening idle frame");
                SamePose(before,WeaponMotion.Sample(input,motion),"recovery to next pulse");
                float contact=input.Weapon.ContactPhase;
                for(int i=1;i<=40;i++){Action(ref input,contact*i/40,incrementPulse?2u:1u);motion.Step(input,.64f*contact/40);}
                var reached=WeaponMotion.Sample(input,motion);
                Assert.Less(math.distance(reached.Grip,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.GripOffset)),.00001f);
                Assert.Less(math.abs(WeaponMotion.AngleDelta(face==1?0:math.PI,reached.Angle)),.00001f);
                Assert.That(reached.Body,Is.EqualTo(-.10f).Within(.00001f),"authoritative contact remains exact");
            }
        }

        [Test]
        public void BufferedPulseCarriesRootRelativeOutgoingVelocity()
        {
            var input=Input();var motion=Warm(input);
            const float dt=.001f;
            for(int i=0;i<=500;i++)
            {
                input.Root=input.Ground+=new float2(.7f,.2f)*dt;
                Action(ref input,.5f+i*dt/.64f*.25f);motion.Step(input,dt);
            }
            float2 velocity=motion.BladeHeldGripVelocity;
            float angleVelocity=motion.HeldAngleVelocity;
            Action(ref input,0,2);motion.Step(input,0);var start=WeaponMotion.Sample(input,motion);
            var startInput=input;var startMotion=motion;
            const float h=.0001f;float2 rootVelocity=new float2(.7f,.2f);
            input.Root=input.Ground+=rootVelocity*h;Action(ref input,h/.64f,2);motion.Step(input,h);
            var next=WeaponMotion.Sample(input,motion);
            var halfInput=startInput;var halfMotion=startMotion;
            halfInput.Root=halfInput.Ground+=rootVelocity*(h*.5f);Action(ref halfInput,h*.5f/.64f,2);halfMotion.Step(halfInput,h*.5f);
            var half=WeaponMotion.Sample(halfInput,halfMotion);
            // Cancel the forward difference's O(h) acceleration term. This assertion
            // measures the instantaneous inherited tangent, not the acceleration of
            // the new authored arc. Render-step speed bounds are tested separately.
            float2 tangent=2*(half.Grip-start.Grip)/(h*.5f)-(next.Grip-start.Grip)/h;
            float angularTangent=2*WeaponMotion.AngleDelta(start.Angle,half.Angle)/(h*.5f)-WeaponMotion.AngleDelta(start.Angle,next.Angle)/h;
            Assert.Less(math.distance((tangent-rootVelocity)/input.Scale,velocity),.06f,"root travel is not inherited a second time");
            Assert.Less(math.abs(angularTangent-angleVelocity),.06f,"outgoing angular velocity is retained");
        }

        [Test]
        public void ActingTurnOwnershipKeepsVelocityAtBothRampJoinsAndContact()
        {
            var input=Input();var motion=Warm(input);Action(ref input,0,2);
            motion.BladeTransition=new BladePoseTransition {Active=true,ArmBend=-1};
            float contact=input.Weapon.ContactPhase,h=.00001f;
            foreach(float join in new[]{.05f,.95f})
            {
                input.Weapon.Phase=contact*(join-h);var before=WeaponMotion.Sample(input,motion).ArmBend;
                input.Weapon.Phase=contact*join;var at=WeaponMotion.Sample(input,motion).ArmBend;
                input.Weapon.Phase=contact*(join+h);var after=WeaponMotion.Sample(input,motion).ArmBend;
                Assert.Less(math.abs((at-before)-(after-at))/h,.04f,"C1 ownership ramp at "+join);
            }
            input.Weapon.Phase=contact*(1-h);float near=WeaponMotion.Sample(input,motion).ArmBend;
            input.Weapon.Phase=contact;float end=WeaponMotion.Sample(input,motion).ArmBend;
            Assert.That(end,Is.EqualTo(1));Assert.Less(math.abs(end-near)/h,.01f,"ownership stops at authoritative contact");
            // A nonzero outgoing velocity is carried in physical seconds, even when
            // the new action's first presentation phase provides no duration estimate.
            motion.BladeTransition.ArmBend=-.4f;motion.BladeTransition.ArmBendVelocity=3;
            input.Weapon.Phase=0;float start=WeaponMotion.Sample(input,motion).ArmBend;
            const float dt=.00001f;motion.BladeTransition.Age=dt;input.Weapon.Phase=contact*dt/.2f;
            Assert.That((WeaponMotion.Sample(input,motion).ArmBend-start)/dt,Is.EqualTo(3).Within(.02f));
        }

        [Test]
        public void LongCustomAnticipationKeepsOwnershipBridgeUntilItsOwnContact()
        {
            var input=Input();input.Weapon.ContactPhase=.8f;input.Weapon.ActiveEndPhase=.9f;
            var motion=Warm(input);input.Facing=-1;input.Weapon.AimDirection=new float2(-1,0);
            Action(ref input,0,2);motion.Step(input,0);
            for(int i=1;i<=30;i++){Action(ref input,i*.01f,2);motion.Step(input,.01f);}
            Assert.IsTrue(motion.BladeTransition.Active,"a custom long windup is not cut off by the .2-second positional residual");
            float bend=WeaponMotion.Sample(input,motion).ArmBend;
            Assert.That(bend,Is.InRange(-.5f,0f),"side ownership still follows the custom precontact clock");
            Action(ref input,input.Weapon.ContactPhase,2);motion.Step(input,0);
            var contact=WeaponMotion.Sample(input,motion);
            Assert.That(contact.ArmBend,Is.EqualTo(1).Within(.000001f));
            Assert.Less(math.distance(contact.Grip,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.GripOffset)),.00001f);
        }

        [TestCase(false)] [TestCase(true)]
        public void CancelAndBufferedPulseKeepPelvisTorsoAndFinalChainContinuous(bool buffered)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            for(int face=-1;face<=1;face+=2)foreach(float phase in new[]{.16f,.32f,.7f})
            {
                var input=Input(face);input.SkillPoseId=101;input.SkillWeight=1;input.SkillPhase=.28f;
                var motion=Warm(input);
                for(int i=0;i<60;i++){Action(ref input,phase*i/59);motion.Step(input,.64f*phase/59);}
                GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                var before=new BoneWorld[NaturalCharacterRig.Bones];for(int j=0;j<before.Length;j++)before[j]=world[j];
                if(buffered)Action(ref input,0,2);
                else {input.State=GameplayCharacterState.Run;input.Weapon.Stage=WeaponStage.Idle;input.Weapon.Phase=0;}
                motion.Step(input,0);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                foreach(int bone in new[]{NaturalCharacterRig.Pelvis,NaturalCharacterRig.Torso,NaturalCharacterRig.FarArm,NaturalCharacterRig.FarForearm,NaturalCharacterRig.NearArm,NaturalCharacterRig.NearForearm,NaturalCharacterRig.Hand})
                {
                    string context="face="+face+" phase="+phase+" buffered="+buffered+" bone="+bone;
                    Assert.Less(math.distance(before[bone].Position,world[bone].Position),.00001f,"final position "+context);
                    Assert.Less(math.abs(WeaponMotion.AngleDelta(before[bone].Rotation,world[bone].Rotation)),.00001f,"final angle "+context);
                }
                var attachment=WeaponMotion.Attach(input,motion,world,0);
                Assert.Less(math.distance(attachment.PrimaryGrip,world[NaturalCharacterRig.Hand].Position),.000001f);
                Assert.Less(math.abs(WeaponMotion.AngleDelta(attachment.Rotation,world[NaturalCharacterRig.Hand].Rotation)),.000001f);
            }
        }
    }
}
