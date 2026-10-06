using System;
using System.Runtime.InteropServices;
using SPF.L1.Skeleton;
using Unity.Mathematics;

namespace SPF.Presentation.Characters
{
    public enum BatBackend { CpuWeighted, GpuVertex }
    public enum BatPrecision { Float, Half }

    /// <summary>Explicit shader ABI, four float4s, offsets 0/16/32/48. No simulation state lives here.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BatInstance
    {
        public float4 Placement; // world xy, uniform positive scale, facing +/-1
        public float4 Frames; // absolute frame A/B, blend, world z
        public float4 Tint;
        public float4 Ik; // unmirrored model target xy, enable 0/1, bend +/-1
        public const int Stride = 64;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BatRows
    {
        public float4 Row0, Row1; // m00,m01,tx,0 / m10,m11,ty,0
        public const int Stride = 32;
        public float2 Transform(float2 p) => new float2(math.dot(Row0.xyz, new float3(p, 1)), math.dot(Row1.xyz, new float3(p, 1)));
        public static BatRows From(in Affine2D a) => new BatRows { Row0 = new float4(a.Row0, 0), Row1 = new float4(a.Row1, 0) };
        public static BatRows Lerp(in BatRows a, in BatRows b, float t) => new BatRows { Row0 = math.lerp(a.Row0, b.Row0, t), Row1 = math.lerp(a.Row1, b.Row1, t) };
    }

    public struct BatVertex
    {
        public float2 Position, Uv;
        public float4 Skin; // exact integer bone indices xy, normalized weights zw
        public float4 Color;
    }

    public struct BatClip
    {
        public int FirstFrame, FrameCount;
        public float Duration;
        public bool Loop;
    }

    /// <summary>Bounded workload, validated before allocating resources. This is not an arbitrary rig importer.</summary>
    public static class BatLimits
    {
        public const int Bones = 3, MaxClips = 2, MaxFramesPerClip = 60, MaxVertices = 128, MaxIndices = 768, MaxCapacity = 256, CpuPageSize = 64;
        public const float MaxScale = 4f, MaxPixelsPerUnit = 256f, HalfPixelBudget = 0.25f;
        public static void Capacity(int count) { if (count < 1 || count > MaxCapacity) throw new ArgumentOutOfRangeException(nameof(count)); }
        public static void Instance(in BatInstance i, int frameCount)
        {
            if (!math.all(math.isfinite(i.Placement)) || !math.all(math.isfinite(i.Frames)) || !math.all(math.isfinite(i.Tint)) || !math.all(math.isfinite(i.Ik)) ||
                i.Placement.z <= 0 || i.Placement.z > MaxScale || math.abs(i.Placement.w) != 1 ||
                i.Frames.x != math.floor(i.Frames.x) || i.Frames.y != math.floor(i.Frames.y) || i.Frames.x < 0 || i.Frames.y < 0 || i.Frames.x >= frameCount || i.Frames.y >= frameCount ||
                i.Frames.z < 0 || i.Frames.z > 1 || (i.Ik.z != 0 && i.Ik.z != 1) || math.abs(i.Ik.w) != 1 ||
                math.any(math.abs(i.Placement.xy) > 100000f) || math.abs(i.Frames.w) > 100000f || math.any(math.abs(i.Ik.xy) > 1000f) || math.any(i.Tint < 0) || math.any(i.Tint > 1))
                throw new ArgumentException("Invalid bounded BAT instance.");
        }
    }

    /// <summary>Pure selector: vertex SSBO, API, shader and exact sampled format support are separate gates.</summary>
    public struct BatCapabilities
    {
        public bool Graphics, SupportedApi, Shader, Instancing, HalfSample, FloatSample;
        public int ShaderLevel, VertexBuffers, MaxTextureSize;
        public long MaxBufferBytes;
        public BatBackend Select(bool forceCpu, bool halfAccepted, int frames, int capacity, out BatPrecision precision)
        {
            BatLimits.Capacity(capacity);
            if (frames < 1 || frames > BatLimits.MaxClips * BatLimits.MaxFramesPerClip) throw new ArgumentOutOfRangeException(nameof(frames));
            precision = halfAccepted && HalfSample ? BatPrecision.Half : BatPrecision.Float;
            return !forceCpu && Graphics && SupportedApi && Shader && Instancing && ShaderLevel >= 45 && VertexBuffers >= 1 &&
                MaxTextureSize >= 2 * BatLimits.Bones && MaxTextureSize >= frames && MaxBufferBytes >= (long)capacity * BatInstance.Stride &&
                (precision == BatPrecision.Half ? HalfSample : FloatSample) ? BatBackend.GpuVertex : BatBackend.CpuWeighted;
        }
    }

    public static class BatMath
    {
        public static BatRows Rigid(float2 posedOrigin, float angle, float2 bindOrigin)
        {
            math.sincos(angle, out float s, out float c);
            return new BatRows { Row0 = new float4(c, -s, posedOrigin.x - c * bindOrigin.x + s * bindOrigin.y, 0), Row1 = new float4(s, c, posedOrigin.y - s * bindOrigin.x - c * bindOrigin.y, 0) };
        }
        /// <summary>One static-parent chain only; full IK override. Mirrors the source Skeletal.TwoBoneIK signs.</summary>
        public static void SolveIk(float4 shape, float4 ik, out BatRows upper, out BatRows lower)
        {
            float2 d = ik.xy - shape.xy;
            float l1 = shape.z, l2 = shape.w;
            float distance = math.clamp(math.length(d), math.abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            float cosInner = math.clamp((l1*l1 + l2*l2 - distance*distance) / (2*l1*l2), -1f, 1f);
            float cosA = math.clamp((l1*l1 + distance*distance - l2*l2) / (2*l1*distance), -1f, 1f);
            float aim = math.lengthsq(d) == 0 ? 0 : math.atan2(d.y,d.x);
            float a = aim + ik.w * math.acos(cosA);
            float b = a - ik.w * (math.PI - math.acos(cosInner));
            math.sincos(a, out float s, out float c);
            upper = Rigid(shape.xy, a, shape.xy);
            lower = Rigid(shape.xy + l1 * new float2(c,s), b, shape.xy + new float2(l1,0));
        }
        public static float3 Place(float2 posed, in BatInstance i) => new float3(i.Placement.xy + new float2(posed.x * i.Placement.w, posed.y) * i.Placement.z, i.Frames.w);
    }
}
