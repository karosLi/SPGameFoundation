using SPF.L1.Skeleton;
using SPF.Presentation.Sprites;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    /// <summary>A sprite pinned to a bone (cut-out skeletal animation): placed in the bone's frame.</summary>
    public struct BoneAttachment
    {
        public int Bone;
        public float4 Uv;
        public float2 Offset;      // centre in the bone frame (e.g. half the limb length along +x)
        public float Rotation;     // extra rotation (radians)
        public float2 Size;
        public float Layer;        // draw order within a character (higher = in front)
        public float4 Tint;        // multiplied with the character tint
    }

    /// <summary>
    /// Writes every character's bone-attached sprites as packed instances in one Burst job
    /// (<c>Out[character * attachments + k]</c>): cut-out skeletal characters cost one 32-byte instance per part,
    /// drawn through the ordinary sprite batches on both render tiers.
    /// </summary>
    [BurstCompile]
    public struct SkeletonSpriteJob : IJobParallelFor
    {
        public int Bones;
        [ReadOnly] public NativeArray<BoneWorld> World;
        [ReadOnly] public NativeArray<BoneAttachment> Attachments;
        [ReadOnly] public NativeArray<float> Facing;
        [ReadOnly] public NativeArray<float4> Tint;
        [ReadOnly] public NativeArray<float> Flash;
        [ReadOnly] public NativeArray<float> Depth;     // per character (nearer = smaller)
        /// <summary>Optional skins: per-character palette row (<see cref="Palette"/>[skin * attachments + k] tints part k).</summary>
        [ReadOnly] public NativeArray<int> Skin;
        [ReadOnly] public NativeArray<float4> Palette;
        /// <summary>Depth step between parts of one character.</summary>
        public float LayerStep;
        [NativeDisableParallelForRestriction] public NativeArray<PackedSprite> Out;

        public void Execute(int index)
        {
            float facing = Facing[index];
            float mirror = facing < 0f ? -1f : 1f;
            int count = Attachments.Length;
            for (int k = 0; k < count; k++)
            {
                var a = Attachments[k];
                var bone = World[index * Bones + a.Bone];
                float2 centre = bone.Transform(a.Offset, facing);
                float rotation = bone.Rotation + a.Rotation * mirror;
                // Mirrored frames flip the sprite's y (see BoneWorld.Transform), so it reads correctly facing left.
                float2 size = new float2(a.Size.x, a.Size.y * mirror);
                float4 tint = Tint[index] * a.Tint;
                if (Palette.Length > 0) tint *= Palette[Skin[index] * count + k];
                Out[index * count + k] = PackedSprite.Pack(centre, size, a.Uv, Depth[index] - a.Layer * LayerStep, tint, rotation, Flash[index]);
            }
        }
    }
}
