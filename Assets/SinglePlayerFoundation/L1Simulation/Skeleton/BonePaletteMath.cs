using Unity.Mathematics;

namespace SPF.L1.Skeleton
{
    /// <summary>
    /// CPU reference affine 2D transform, row-major [m00 m01 tx; m10 m11 ty]. This is not a GPU
    /// buffer/texture ABI: a future shader contract must explicitly pack two float4 rows (32 bytes).
    /// </summary>
    public struct Affine2D
    {
        public float3 Row0;
        public float3 Row1;

        public static Affine2D Identity => new Affine2D { Row0 = new float3(1f, 0f, 0f), Row1 = new float3(0f, 1f, 0f) };

        public float2 TransformPoint(float2 point) => new float2(
            Row0.x * point.x + Row0.y * point.y + Row0.z,
            Row1.x * point.x + Row1.y * point.y + Row1.z);

        /// <summary>Matches BoneWorld.Transform exactly, including the mirrored frame's negative Y axis.</summary>
        public static Affine2D FromBone(in BoneWorld bone, float facing)
        {
            math.sincos(bone.Rotation, out float s, out float c);
            float mirror = facing < 0f ? -1f : 1f;
            return new Affine2D
            {
                Row0 = new float3(c, -s * mirror, bone.Position.x),
                Row1 = new float3(s, c * mirror, bone.Position.y),
            };
        }

        /// <summary>Composition a * b: transform by b first, then a. Handles rotation, reflection and scale.</summary>
        public static Affine2D Compose(in Affine2D a, in Affine2D b) => new Affine2D
        {
            Row0 = new float3(a.Row0.x * b.Row0.x + a.Row0.y * b.Row1.x,
                a.Row0.x * b.Row0.y + a.Row0.y * b.Row1.y,
                a.Row0.x * b.Row0.z + a.Row0.y * b.Row1.z + a.Row0.z),
            Row1 = new float3(a.Row1.x * b.Row0.x + a.Row1.y * b.Row1.x,
                a.Row1.x * b.Row0.y + a.Row1.y * b.Row1.y,
                a.Row1.x * b.Row0.z + a.Row1.y * b.Row1.z + a.Row1.z),
        };

        /// <summary>False for singular/near-singular or non-finite transforms; output is then identity.</summary>
        public bool TryInverse(out Affine2D inverse)
        {
            inverse = Identity;
            float det = Row0.x * Row1.y - Row0.y * Row1.x;
            if (!math.all(math.isfinite(Row0)) || !math.all(math.isfinite(Row1)) || !math.isfinite(det) || math.abs(det) < 1e-8f)
                return false;
            float reciprocal = 1f / det;
            inverse = new Affine2D
            {
                Row0 = new float3(Row1.y * reciprocal, -Row0.y * reciprocal, (Row0.y * Row1.z - Row1.y * Row0.z) * reciprocal),
                Row1 = new float3(-Row1.x * reciprocal, Row0.x * reciprocal, (Row1.x * Row0.z - Row0.x * Row1.z) * reciprocal),
            };
            if (!math.all(math.isfinite(inverse.Row0)) || !math.all(math.isfinite(inverse.Row1)))
            {
                inverse = Identity;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Small allocation-free CPU reference for a future baked bone-animation palette. No baker, GPU
    /// skinning, weighted mesh renderer or texture upload path is implemented by this helper.
    /// Existing gameplay continues to evaluate SkeletonView/Skeletal and CPU hit probes.
    /// </summary>
    public static class BonePaletteMath
    {
        /// <summary>
        /// Maps a bind-pose model-space point into posed model space: posed bone * inverse(bind bone).
        /// Both inputs must use the SAME model coordinate system; add character placement separately.
        /// </summary>
        public static bool TrySkinningTransform(in Affine2D bindBone, in Affine2D posedBone, out Affine2D palette)
        {
            if (!bindBone.TryInverse(out var inverse)) { palette = Affine2D.Identity; return false; }
            palette = Affine2D.Compose(posedBone, inverse);
            return true;
        }

        /// <summary>
        /// Selects sampled frames for a single clip. Looping clips exclude a duplicated endpoint and use
        /// frameCount samples across [0,duration); non-looping clips include both endpoints. Negative loop
        /// time wraps, non-loop time clamps. Invalid/empty clip metadata returns frame 0 with zero blend;
        /// callers must still avoid reading empty palette storage.
        /// </summary>
        public static void SampleFrames(float time, float duration, int frameCount, bool loop, out int first, out int second, out float blend)
        {
            first = second = 0;
            blend = 0f;
            if (frameCount <= 1 || duration <= 0f || !math.isfinite(duration) || !math.isfinite(time)) return;
            float normalized = time / duration;
            if (!math.isfinite(normalized)) return;
            if (loop)
            {
                float frame = (normalized - math.floor(normalized)) * frameCount;
                first = math.min((int)math.floor(frame), frameCount - 1);
                second = (first + 1) % frameCount;
                blend = math.saturate(frame - first);
            }
            else
            {
                float frame = math.saturate(normalized) * (frameCount - 1);
                first = math.min((int)math.floor(frame), frameCount - 1);
                second = math.min(first + 1, frameCount - 1);
                blend = math.saturate(frame - first);
            }
        }

        /// <summary>
        /// Elementwise matrix blend, useful only as a visual reference. It can shrink/shear rotations
        /// (180-degree blends can collapse); it must never replace authoritative CPU pose/IK hit probes.
        /// </summary>
        public static Affine2D Lerp(in Affine2D first, in Affine2D second, float blend) => new Affine2D
        {
            Row0 = math.lerp(first.Row0, second.Row0, math.saturate(blend)),
            Row1 = math.lerp(first.Row1, second.Row1, math.saturate(blend)),
        };
    }
}
