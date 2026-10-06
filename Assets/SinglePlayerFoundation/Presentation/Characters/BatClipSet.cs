using System;
using SPF.L1.Skeleton;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Presentation.Characters
{
    /// <summary>Owned immutable bind mesh and baked palettes. Resources are shared by batches; dispose after all batches.</summary>
    public sealed partial class BatClipSet : IDisposable
    {
        internal NativeArray<BatRows> FloatRows, HalfRows;
        internal NativeArray<BatVertex> Vertices;
        readonly int[] m_Indices;
        readonly BatClip[] m_Clips;
        bool m_Disposed;
        public int VertexCount => Vertices.Length;
        public int IndexCount => m_Indices.Length;
        public int FrameCount => FloatRows.Length / BatLimits.Bones;
        public int ClipCount => m_Clips.Length;
        public float4 IkShape { get; }
        public float HalfMaxModelError { get; }
        public float HalfMaxPixelError => HalfMaxModelError * BatLimits.MaxPixelsPerUnit * BatLimits.MaxScale;
        public bool HalfAccepted => HalfMaxPixelError <= BatLimits.HalfPixelBudget;
        public float ModelRadius { get; }
        public long CpuPaletteBytes => (long)FloatRows.Length * BatRows.Stride * 2;
        public bool IsDisposed => m_Disposed;
        internal BatClipSet(BatVertex[] vertices, int[] indices, BatClip[] clips, BatRows[] floats, BatRows[] halves, float4 ikShape, float halfError, float radius)
        {
            Vertices = new NativeArray<BatVertex>(vertices, Allocator.Persistent);
            FloatRows = new NativeArray<BatRows>(floats, Allocator.Persistent);
            HalfRows = new NativeArray<BatRows>(halves, Allocator.Persistent);
            m_Indices = (int[])indices.Clone(); m_Clips = (BatClip[])clips.Clone(); IkShape = ikShape; HalfMaxModelError = halfError; ModelRadius = radius;
        }
        public BatVertex Vertex(int index) { CheckAlive(); return Vertices[index]; }
        public int Index(int index) { CheckAlive(); return m_Indices[index]; }
        public BatClip Clip(int index) { CheckAlive(); return m_Clips[index]; }
        public BatRows Row(int frame, int bone, BatPrecision precision)
        {
            CheckAlive(); if (frame < 0 || frame >= FrameCount || bone < 0 || bone >= BatLimits.Bones) throw new ArgumentOutOfRangeException(nameof(frame));
            return (precision == BatPrecision.Half ? HalfRows : FloatRows)[frame * BatLimits.Bones + bone];
        }
        public BatInstance Instance(int clip, float time, float2 position, float scale = 1, float facing = 1, float4? tint = null, float depth = 0, float2 target = default, bool ik = false, float bend = 1)
        {
            CheckAlive(); if (!math.isfinite(time)) throw new ArgumentException("Animation time must be finite.");
            var c = Clip(clip);
            BonePaletteMath.SampleFrames(time,c.Duration,c.FrameCount,c.Loop,out int a,out int b,out float blend);
            var result = new BatInstance { Placement = new float4(position,scale,facing), Frames = new float4(c.FirstFrame+a,c.FirstFrame+b,blend,depth), Tint = tint ?? new float4(1), Ik = new float4(target,ik?1:0,bend) };
            BatLimits.Instance(result,FrameCount); return result;
        }
        public float3 Skin(int vertex, in BatInstance instance, BatPrecision precision)
        {
            CheckAlive(); BatLimits.Instance(instance,FrameCount);
            return BatSkinning.Skin(Vertices[vertex], instance, precision == BatPrecision.Half ? HalfRows : FloatRows, IkShape);
        }
        /// <summary>Independent CPU reference for compute output. No GPU access or mutation.</summary>
        public BatPalette SamplePalette(in BatInstance instance, BatPrecision precision)
        {
            CheckAlive(); BatLimits.Instance(instance,FrameCount);
            return BatSkinning.SamplePalette(instance,precision == BatPrecision.Half ? HalfRows : FloatRows,IkShape);
        }
        internal void CheckAlive() { if (m_Disposed) throw new ObjectDisposedException(nameof(BatClipSet)); }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            ReleaseGraphics();
            Vertices.Dispose(); FloatRows.Dispose(); HalfRows.Dispose();
        }
        partial void ReleaseGraphics();
    }

    public static class BatSkinning
    {
        public static BatPalette SamplePalette(in BatInstance instance, NativeArray<BatRows> rows, float4 shape)
        {
            int a=(int)instance.Frames.x*BatLimits.Bones,b=(int)instance.Frames.y*BatLimits.Bones;
            var palette=new BatPalette { Root=BatRows.Lerp(rows[a],rows[b],instance.Frames.z) };
            if(instance.Ik.z>.5f) BatMath.SolveIk(shape,instance.Ik,out palette.Upper,out palette.Lower);
            else
            {
                palette.Upper=BatRows.Lerp(rows[a+1],rows[b+1],instance.Frames.z);
                palette.Lower=BatRows.Lerp(rows[a+2],rows[b+2],instance.Frames.z);
            }
            return palette;
        }
        public static float3 SkinFromPalette(in BatVertex v,in BatInstance instance,in BatPalette palette) =>
            BatMath.Place(v.Skin.z*palette.Bone((int)v.Skin.x).Transform(v.Position)+v.Skin.w*palette.Bone((int)v.Skin.y).Transform(v.Position),instance);

        public static float3 Skin(in BatVertex v, in BatInstance instance, NativeArray<BatRows> rows, float4 shape)
        {
            int b0 = (int)v.Skin.x, b1 = (int)v.Skin.y;
            int a = (int)instance.Frames.x * BatLimits.Bones, b = (int)instance.Frames.y * BatLimits.Bones;
            var p0 = BatRows.Lerp(rows[a+b0],rows[b+b0],instance.Frames.z);
            var p1 = BatRows.Lerp(rows[a+b1],rows[b+b1],instance.Frames.z);
            if (instance.Ik.z > .5f)
            {
                BatMath.SolveIk(shape, instance.Ik, out var upper,out var lower);
                if (b0 == 1) p0 = upper; else if (b0 == 2) p0 = lower;
                if (b1 == 1) p1 = upper; else if (b1 == 2) p1 = lower;
            }
            return BatMath.Place(v.Skin.z * p0.Transform(v.Position) + v.Skin.w * p1.Transform(v.Position),instance);
        }
    }
}
