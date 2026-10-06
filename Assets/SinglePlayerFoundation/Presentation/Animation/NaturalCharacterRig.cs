using SPF.L1.Skeleton;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    /// <summary>Small explicit cutout rig. This does not use or extend the independent three-bone BAT ABI.</summary>
    public static class NaturalCharacterRig
    {
        public const int Bones = 14;
        public const int Pelvis=0, Torso=1, Head=2, FarArm=3, FarForearm=4, FarThigh=5, FarShin=6, FarFoot=7,
            NearThigh=8, NearShin=9, NearFoot=10, NearArm=11, NearForearm=12, Hand=13;
        public static SkeletonAsset Create()
        {
            var b = new SkeletonAsset.Builder();
            b.Bone("pelvis",null,new float2(0,1.09f),0,.3f)
             .Bone("torso","pelvis",new float2(0,.13f),0,.55f)
             .Bone("head","torso",new float2(.03f,.71f),0,.4f)
             .Bone("farArm","torso",new float2(-.07f,.42f),-65,.43f)
             .Bone("farForearm","farArm",new float2(.43f,0),-35,.42f)
             .Bone("farThigh","pelvis",new float2(-.10f,-.03f),-90,.56f)
             .Bone("farShin","farThigh",new float2(.56f,0),0,.55f)
             .Bone("farFoot","farShin",new float2(.55f,0),0,.23f)
             .Bone("nearThigh","pelvis",new float2(.10f,-.03f),-90,.56f)
             .Bone("nearShin","nearThigh",new float2(.56f,0),0,.55f)
             .Bone("nearFoot","nearShin",new float2(.55f,0),0,.23f)
             .Bone("nearArm","torso",new float2(.05f,.45f),-70,.43f)
             .Bone("nearForearm","nearArm",new float2(.43f,0),-30,.42f)
             .Bone("hand","nearForearm",new float2(.42f,0),0,.18f);
            b.Clip("bind",1,true);return b.Build();
        }

        /// <summary>All secondary motion is deliberately small; leg IK solves after the pelvis motion.</summary>
        public static void Pose(in SkeletonView rig, NativeArray<BoneLocal> local, NativeArray<BoneWorld> world,
            float2 root, float facing, float scale, float phase, float2 leftFoot, float2 rightFoot,
            bool aim, float2 aimTarget, float attack, int at = 0)
        {
            for (int i=0;i<Bones;i++) local[at+i]=new BoneLocal { Position=rig.Bones[i].Position, Rotation=rig.Bones[i].Rotation };
            float p=phase*2*math.PI;
            var pelvis=local[at+Pelvis];pelvis.Position.y += .025f*math.cos(2*p)-.065f*attack;local[at+Pelvis]=pelvis;
            var torso=local[at+Torso];torso.Rotation=-.045f + .028f*math.sin(p) -.15f*attack;local[at+Torso]=torso;
            var head=local[at+Head];head.Rotation=-torso.Rotation*.65f+.018f*math.sin(p-.65f);local[at+Head]=head;
            NaturalMotion.Aim(rig,local,FarThigh,FarShin,leftFoot,root,facing,scale,1,at);
            NaturalMotion.Aim(rig,local,NearThigh,NearShin,rightFoot,root,facing,scale,1,at);
            // Feet stay level at contact. The upper/lower rotations include pelvis (rotation zero).
            var far=local[at+FarFoot];far.Rotation=-local[at+FarThigh].Rotation-local[at+FarShin].Rotation;local[at+FarFoot]=far;
            var near=local[at+NearFoot];near.Rotation=-local[at+NearThigh].Rotation-local[at+NearShin].Rotation;local[at+NearFoot]=near;
            var arm=local[at+FarArm];arm.Rotation=math.radians(-82)+.3f*math.sin(p);local[at+FarArm]=arm;
            arm=local[at+NearArm];arm.Rotation=math.radians(-88)-.3f*math.sin(p);local[at+NearArm]=arm;
            if(aim)NaturalMotion.Aim(rig,local,NearArm,NearForearm,aimTarget,root,facing,scale,-1,at);
            Skeletal.ToWorld(rig,local,root,facing,scale,world,at,at);
        }

        /// <summary>0..1 anticipation/contact/recovery. This visual cycle has no hit or damage authority.</summary>
        public static float Strike(float time)
        {
            float t=math.frac(time/2.8f);
            if(t<.3f)return -.3f*NaturalMotion.Ease(t/.3f);
            if(t<.42f)return math.lerp(-.3f,1,NaturalMotion.Ease((t-.3f)/.12f));
            return 1-NaturalMotion.Ease((t-.42f)/.58f);
        }
    }
}
