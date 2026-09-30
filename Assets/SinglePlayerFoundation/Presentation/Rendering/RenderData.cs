using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace SPF.Presentation
{
    /// <summary>One disc instance. Layout matches <c>InstanceData</c> in SPFCommon.hlsl (32 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct InstanceData
    {
        public float2 Position;
        public float Radius;
        public float Depth;
        public float4 Color;   // straight alpha

        public const int Stride = 32;

        public InstanceData(float2 position, float radius, float depth, float4 color)
        {
            Position = position;
            Radius = radius;
            Depth = depth;
            Color = color;
        }
    }

    public enum BlendKind : byte
    {
        Opaque = 0,
        /// <summary>Premultiplied alpha; chains use one depth per chain so self-overlap never darkens.</summary>
        Translucent = 1,
        Additive = 2,
    }

    public enum ChainShape : byte
    {
        Nodes = 0,
        Strip = 1,
    }

    /// <summary>GPU description of one chain. Layout matches <c>ChainHeader</c> in SPFCommon.hlsl (112 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ChainHeader
    {
        public float2 HeadPrev;
        public float2 HeadCurr;
        public float ArcPrev;
        public float ArcCurr;
        public uint TrailStart;
        public uint TrailMask;
        public uint Newest;
        public uint Count;
        public float Spacing;
        public float NodeSpacing;
        public uint NodeOffset;
        public uint NodeCount;
        public float Radius;
        public float Depth;
        public float4 ColorA;
        public float4 ColorB;
        public uint Stripe;
        public uint Flags;
        public float NodeStride;
        public float Pad;

        public const int Stride = 112;
        public const uint FlagTranslucent = 1;
        public const uint FlagAdditive = 2;
    }

    /// <summary>Row delta for GPU-resident pools (layout matches RowDelta in PointCloud.compute, 48 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RowDelta
    {
        public uint Row;
        public uint Pad0, Pad1, Pad2;
        public InstanceData Data;

        public const int Stride = 48;
    }

    /// <summary>Trail mirror delta (layout matches TrailDelta in NodeExpand.compute, 12 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TrailDelta
    {
        public uint Slot;
        public float2 Value;

        public const int Stride = 12;
    }
}
