using System;
using System.Runtime.InteropServices;
using SPF.Presentation.Sprites;
using Unity.Mathematics;

namespace SPF.Presentation.Particles
{
    public enum ParticleBackend : byte { CpuBurst, GpuCompute }
    public enum ParticlePriority : byte { Decorative, Trail, Release, Impact, Hero }
    public enum ParticleShape : byte { Spark, Streak, Rune, Ember }

    public static class ParticleLimits
    {
        public const int HighCapacity = 1024, LowCapacity = 256, Emitters = 32, SpawnsPerFrame = 64, GroupSize = 64;
        public const int ReservedSpawns = 16, ReservedParticles = 64, CueOwners = 128;
        public const float MaxDeltaTime = .05f, MaxLife = 1.5f;
        public static float Delta(float dt) => math.isfinite(dt) ? math.clamp(dt, 0, MaxDeltaTime) : 0;
    }

    /// <summary>96-byte GPU/CPU ABI. Position is emitter-local for attached particles, world-space otherwise.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleState
    {
        public float4 PositionAge; // xy position, z age, w life
        public float4 VelocityDrag; // xy velocity, z drag, w gravity (screen up)
        public float4 Shape; // xy size, z rotation, w angular velocity
        public float4 Color;
        public float4 Visual; // depth, atlas cell, end scale, fade-in seconds
        public uint4 Attachment; // socket index+1 (0 = world), generation token, priority, reserved
        public const int Stride = 96;
        public bool Alive => PositionAge.w > 0 && PositionAge.z < PositionAge.w;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleSocket
    {
        public float4 Pose; // world xy, normalized direction xy
        public uint4 Identity; // generation token, active, reserved, reserved
        public const int Stride = 32;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleSpawn
    {
        public uint4 Target;
        public ParticleState State;
        public const int Stride = 112;
    }

    public struct ParticleCapabilities
    {
        public bool Compute, Kernels, Graphics, SupportedApi, Shader, Instancing, AtlasFormat;
        public int ShaderLevel, ComputeBuffers, VertexBuffers, GroupSize;
        public long MaxBufferBytes;
        public RenderTier SelectRenderTier(RenderTier requested, int capacity)
        {
            if (capacity < 1 || capacity > ParticleLimits.HighCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
            return requested == RenderTier.GpuDriven && Graphics && SupportedApi && Shader && Instancing &&
                ShaderLevel >= 45 && VertexBuffers >= 1 && MaxBufferBytes >= capacity * PackedSprite.Stride
                ? RenderTier.GpuDriven : RenderTier.DataTexture;
        }
        public ParticleBackend Select(bool forceCpu, int capacity)
        {
            if (capacity < 1 || capacity > ParticleLimits.HighCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
            return !forceCpu && Compute && Kernels && Graphics && SupportedApi && Shader && Instancing && AtlasFormat &&
                ShaderLevel >= 45 && ComputeBuffers >= 4 && VertexBuffers >= 1 && GroupSize >= ParticleLimits.GroupSize &&
                MaxBufferBytes >= capacity * ParticleState.Stride ? ParticleBackend.GpuCompute : ParticleBackend.CpuBurst;
        }
    }

    /// <summary>Only presentation-owned hashes and curves. Never touches a gameplay random stream.</summary>
    public static class ParticleMath
    {
        public static uint Hash(uint seed)
        {
            seed ^= seed >> 16; seed *= 0x7feb352du; seed ^= seed >> 15; seed *= 0x846ca68bu; return seed ^ (seed >> 16);
        }
        public static float Random01(ref uint seed) { seed = Hash(seed + 0x9e3779b9u); return (seed & 0x00ffffff) * (1f / 16777216f); }
        public static float2 Rotate(float2 p, float2 direction) => new float2(p.x * direction.x - p.y * direction.y, p.x * direction.y + p.y * direction.x);
        public static float2 WorldPosition(in ParticleState p, in ParticleSocket socket) => p.Attachment.x == 0 ? p.PositionAge.xy : socket.Pose.xy + Rotate(p.PositionAge.xy, socket.Pose.zw);
        public static ParticleState Step(ParticleState p, in ParticleSocket socket, float dt)
        {
            if (!p.Alive) return p;
            if (p.Attachment.x != 0 && (socket.Identity.y == 0 || socket.Identity.x != p.Attachment.y)) { p.PositionAge.z = p.PositionAge.w; return p; }
            p.PositionAge.z += dt;
            if (!p.Alive) return p;
            var velocity = (p.VelocityDrag.xy + new float2(0, p.VelocityDrag.w * dt)) / (1 + p.VelocityDrag.z * dt);
            p.VelocityDrag.xy = velocity;
            p.PositionAge.xy += velocity * dt;
            p.Shape.z += p.Shape.w * dt;
            return p;
        }
        public static float4 Uv(int shape) => new float4((shape * 32 + 1f) / 128f, 1f / 32f, 30f / 128f, 30f / 32f);
        public static PackedSprite Sprite(in ParticleState p, in ParticleSocket socket)
        {
            if (!p.Alive) return default;
            float t = math.saturate(p.PositionAge.z / p.PositionAge.w);
            float fade = math.min(1, p.PositionAge.z / math.max(.001f, p.Visual.w)) * (1 - t) * (1 - t);
            var color = p.Color; color.w *= fade;
            var size = p.Shape.xy * math.lerp(1, p.Visual.z, t);
            float angle = p.Shape.z + (p.Attachment.x == 0 ? 0 : math.atan2(socket.Pose.w, socket.Pose.z));
            return PackedSprite.Pack(WorldPosition(p, socket), size, Uv((int)p.Visual.y), p.Visual.x, color, angle);
        }
    }
}
