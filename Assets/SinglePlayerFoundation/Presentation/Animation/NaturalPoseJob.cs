using SPF.L1.Skeleton;
using SPF.Presentation.Sprites;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    public struct NaturalPoseInput
    {
        public float2 Root, FarFoot, NearFoot, AimTarget;
        public float Facing, Scale, Phase, Attack;
        public int Aim, Kind;
    }
    [BurstCompile]
    public struct NaturalPoseJob : IJobParallelFor
    {
        [ReadOnly] public SkeletonView Rig;
        [ReadOnly] public NativeArray<NaturalPoseInput> Actors;
        [ReadOnly] public NativeArray<BoneAttachment> Attachments;
        [NativeDisableParallelForRestriction] public NativeArray<BoneLocal> Local;
        [NativeDisableParallelForRestriction] public NativeArray<BoneWorld> World;
        [NativeDisableParallelForRestriction] public NativeArray<PackedSprite> Sprites;
        public NativeArray<int> PoseTicks;
        public int LodTick, FullRateActors;
        public void Execute(int index)
        {
            if (PoseTicks.IsCreated && index >= FullRateActors && PoseTicks[index] == LodTick) return;
            if (PoseTicks.IsCreated) PoseTicks[index] = LodTick;
            var input=Actors[index];int at=index*NaturalCharacterRig.Bones;
            NaturalCharacterRig.Pose(Rig,Local,World,input.Root,input.Facing,input.Scale,input.Phase,input.FarFoot,input.NearFoot,
                input.Aim!=0,input.AimTarget,input.Attack,at);
            for(int k=0;k<NaturalCharacterArt.Parts;k++)
            {
                var a=Attachments[input.Kind*NaturalCharacterArt.Parts+k];var bone=World[at+a.Bone];
                float facing=input.Facing<0?-1:1;
                Sprites[index*NaturalCharacterArt.Parts+k]=PackedSprite.Pack(bone.Transform(a.Offset*input.Scale,facing),
                    a.Size*new float2(input.Scale,input.Scale*facing),a.Uv,-.01f-a.Layer*.001f,a.Tint,bone.Rotation+a.Rotation*facing);
            }
        }
    }
}
