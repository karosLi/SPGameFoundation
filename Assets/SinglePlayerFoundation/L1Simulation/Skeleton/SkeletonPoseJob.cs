using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.L1.Skeleton
{
    /// <summary>
    /// Evaluates many characters' poses in parallel: crossfaded clip sample → forward kinematics → world bones
    /// (<c>World[character * bones + bone]</c>). Used by the simulation for bone-attached hitboxes and by the
    /// renderer for drawing (at the interpolated time).
    /// </summary>
    [BurstCompile]
    public struct SkeletonPoseJob : IJobParallelFor
    {
        public SkeletonView View;
        [ReadOnly] public NativeArray<Animator2D> Animators;
        [ReadOnly] public NativeArray<float2> Roots;
        [ReadOnly] public NativeArray<float> Facing;
        public float Scale;
        /// <summary>Added to every animator's clock (render interpolation between ticks).</summary>
        public float TimeOffset;
        [NativeDisableParallelForRestriction] public NativeArray<BoneLocal> Scratch;   // 2 * bones per character
        [NativeDisableParallelForRestriction] public NativeArray<BoneWorld> World;

        public void Execute(int index)
        {
            int bones = View.BoneCount;
            var animator = Animators[index];
            if (TimeOffset != 0f) animator.Advance(TimeOffset);
            int poseAt = index * bones * 2;
            Skeletal.Evaluate(View, animator, Scratch, Scratch, poseAt, poseAt + bones);
            Skeletal.ToWorld(View, Scratch, Roots[index], Facing[index], Scale, World, poseAt, index * bones);
        }
    }
}
