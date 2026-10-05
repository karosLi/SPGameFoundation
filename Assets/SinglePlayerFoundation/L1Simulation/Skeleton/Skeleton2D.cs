using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.L1.Skeleton
{
    /// <summary>A bone in its bind pose, relative to its parent (parents come before children).</summary>
    public struct BoneDef
    {
        public int Parent;           // -1 for the root
        public float2 Position;      // bind offset from the parent, in parent space (metres, facing right)
        public float Rotation;       // bind rotation relative to the parent (radians)
        public float Length;         // to the tip along local +x (two-bone IK, probes)
    }

    /// <summary>A bone's local transform in a pose.</summary>
    public struct BoneLocal
    {
        public float2 Position;
        public float Rotation;
    }

    /// <summary>A bone's world transform (facing already applied: mirrored characters negate x and rotation).</summary>
    public struct BoneWorld
    {
        public float2 Position;
        public float Rotation;

        /// <summary>
        /// A point at <paramref name="local"/> in the bone's frame (e.g. the tip: (Length, 0)). Mirrored bones
        /// (facing left) also flip the frame's y axis, which is what keeps sprites and probes on the right side.
        /// </summary>
        public float2 Transform(float2 local, float facing)
        {
            math.sincos(Rotation, out float s, out float c);
            float y = facing < 0f ? -local.y : local.y;
            return Position + new float2(local.x * c - y * s, local.x * s + y * c);
        }
    }

    /// <summary>One keyframe of one bone channel: rotation and offset added to the bind pose.</summary>
    public struct BoneKey
    {
        public float Time;
        public float Rotation;
        public float2 Offset;
    }

    public struct ClipInfo
    {
        public float Duration;
        public bool Loop;
    }

    /// <summary>Blittable view of a skeleton's data for Burst jobs (bones, clips, keys).</summary>
    public struct SkeletonView
    {
        [ReadOnly] public NativeArray<BoneDef> Bones;
        [ReadOnly] public NativeArray<BoneKey> Keys;
        [ReadOnly] public NativeArray<int2> Channels;   // [clip * bones + bone] = (first key, key count)
        [ReadOnly] public NativeArray<ClipInfo> Clips;
        public int BoneCount => Bones.Length;
    }

    /// <summary>
    /// Plays one clip per character with a crossfade from the previous one. A struct in a column: advanced by the
    /// simulation (gameplay reads bone positions for hitboxes) and sampled again by the renderer at the
    /// interpolated time, from the same data, so what you see is what hits.
    /// </summary>
    public struct Animator2D
    {
        public int Clip;
        public float Time;
        public int FromClip;
        public float FromTime;
        public float Fade;          // 0..1 progress of the crossfade (1 = done)
        public float FadeLength;
        public float Speed;

        public static Animator2D Start(int clip) => new Animator2D { Clip = clip, FromClip = -1, Fade = 1f, Speed = 1f };

        /// <summary>Switches clip; <paramref name="restart"/> replays the same clip from the start.</summary>
        public void Play(int clip, float fadeSeconds = 0.1f, bool restart = false)
        {
            if (clip == Clip && !restart) return;
            FromClip = Clip;
            FromTime = Time;
            Clip = clip;
            Time = 0f;
            FadeLength = fadeSeconds;
            Fade = fadeSeconds > 0f ? 0f : 1f;
        }

        public void Advance(float dt)
        {
            Time += dt * Speed;
            FromTime += dt * Speed;
            if (Fade < 1f) Fade = FadeLength > 0f ? math.min(1f, Fade + dt / FadeLength) : 1f;
        }

        /// <summary>The non-looping clip has played to its end.</summary>
        public bool Finished(in SkeletonView view) => !view.Clips[Clip].Loop && Time >= view.Clips[Clip].Duration;
    }

    /// <summary>Pose math: sampling, blending, forward kinematics and two-bone IK. All Burst-compatible.</summary>
    public static class Skeletal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float2 Rotate(float2 v, float angle)
        {
            math.sincos(angle, out float s, out float c);
            return new float2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        static float ClipTime(in ClipInfo clip, float time)
        {
            if (clip.Duration <= 0f) return 0f;
            if (clip.Loop) return time - math.floor(time / clip.Duration) * clip.Duration;
            return math.clamp(time, 0f, clip.Duration);
        }

        /// <summary>Samples <paramref name="clip"/> at <paramref name="time"/> into <paramref name="pose"/> (bind + keys).</summary>
        public static void Sample(in SkeletonView view, int clip, float time, NativeArray<BoneLocal> pose, int at = 0)
        {
            int bones = view.BoneCount;
            var info = view.Clips[clip];
            float t = ClipTime(info, time);
            for (int b = 0; b < bones; b++)
            {
                var bind = view.Bones[b];
                int2 range = view.Channels[clip * bones + b];
                float rotation = 0f;
                float2 offset = 0f;
                if (range.y == 1)
                {
                    var k = view.Keys[range.x];
                    rotation = k.Rotation;
                    offset = k.Offset;
                }
                else if (range.y > 1)
                {
                    int last = range.x + range.y - 1;
                    int i = range.x;
                    while (i < last && view.Keys[i + 1].Time <= t) i++;
                    var k0 = view.Keys[i];
                    if (i == last)
                    {
                        // Past the last key: a looping clip wraps back towards the first key.
                        var k1 = view.Keys[range.x];
                        float span = info.Duration - k0.Time;
                        float w = info.Loop && span > 1e-5f ? math.saturate((t - k0.Time) / span) : 0f;
                        rotation = math.lerp(k0.Rotation, k1.Rotation, w);
                        offset = math.lerp(k0.Offset, k1.Offset, w);
                    }
                    else
                    {
                        var k1 = view.Keys[i + 1];
                        float w = math.saturate((t - k0.Time) / math.max(k1.Time - k0.Time, 1e-5f));
                        w = w * w * (3f - 2f * w);   // ease between keys
                        rotation = math.lerp(k0.Rotation, k1.Rotation, w);
                        offset = math.lerp(k0.Offset, k1.Offset, w);
                    }
                }
                pose[at + b] = new BoneLocal { Position = bind.Position + offset, Rotation = bind.Rotation + rotation };
            }
        }

        /// <summary>The animator's pose: current clip, crossfaded from the previous one while fading.</summary>
        public static void Evaluate(in SkeletonView view, in Animator2D animator, NativeArray<BoneLocal> pose, NativeArray<BoneLocal> scratch, int poseAt = 0, int scratchAt = 0)
        {
            Sample(view, animator.Clip, animator.Time, pose, poseAt);
            if (animator.Fade >= 1f || animator.FromClip < 0) return;
            Sample(view, animator.FromClip, animator.FromTime, scratch, scratchAt);
            float w = animator.Fade;
            for (int b = 0; b < view.BoneCount; b++)
            {
                var a = scratch[scratchAt + b];
                var c = pose[poseAt + b];
                pose[poseAt + b] = new BoneLocal { Position = math.lerp(a.Position, c.Position, w), Rotation = math.lerp(a.Rotation, c.Rotation, w) };
            }
        }

        /// <summary>Forward kinematics: local pose → world, around <paramref name="root"/>, mirrored when facing left.</summary>
        public static void ToWorld(in SkeletonView view, NativeArray<BoneLocal> local, float2 root, float facing, float scale, NativeArray<BoneWorld> world, int localAt = 0, int worldAt = 0)
        {
            float mirror = facing < 0f ? -1f : 1f;
            int bones = view.BoneCount;
            for (int b = 0; b < bones; b++)
            {
                int parent = view.Bones[b].Parent;
                var l = local[localAt + b];
                if (parent < 0)
                    world[worldAt + b] = new BoneWorld { Position = l.Position * scale, Rotation = l.Rotation };
                else
                {
                    var p = world[worldAt + parent];
                    world[worldAt + b] = new BoneWorld { Position = p.Position + Rotate(l.Position * scale, p.Rotation), Rotation = p.Rotation + l.Rotation };
                }
            }
            // Mirror and place after the chain is built (unmirrored maths above).
            for (int b = 0; b < bones; b++)
            {
                var w = world[worldAt + b];
                world[worldAt + b] = new BoneWorld { Position = root + new float2(w.Position.x * mirror, w.Position.y), Rotation = mirror > 0f ? w.Rotation : math.PI - w.Rotation };
            }
        }

        /// <summary>
        /// Two-bone IK in local space (arm: upper, lower), in the unmirrored character frame: rotates
        /// <paramref name="upper"/> and <paramref name="lower"/> so the lower bone's tip reaches
        /// <paramref name="target"/> (character space, before mirroring), bending to the <paramref name="bendSign"/> side.
        /// </summary>
        public static void TwoBoneIK(in SkeletonView view, NativeArray<BoneLocal> local, int upper, int lower, float2 target, float bendSign = 1f, int at = 0)
        {
            // Upper bone's parent frame in character space.
            float2 parentPos = 0f;
            float parentRot = 0f;
            int chain = view.Bones[upper].Parent;
            // Walk the chain to accumulate (parents precede children, so walk from the root down).
            Span2 path = default;
            int depth = 0;
            for (int b = chain; b >= 0 && depth < 16; b = view.Bones[b].Parent) path.Set(depth++, b);
            for (int d = depth - 1; d >= 0; d--)
            {
                var l = local[at + path.Get(d)];
                parentPos += Rotate(l.Position, parentRot);
                parentRot += l.Rotation;
            }
            float2 basePos = parentPos + Rotate(local[at + upper].Position, parentRot);
            float l1 = view.Bones[upper].Length, l2 = view.Bones[lower].Length;
            float2 d2 = target - basePos;
            float dist = math.clamp(math.length(d2), math.abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            float cosInner = math.clamp((l1 * l1 + l2 * l2 - dist * dist) / (2f * l1 * l2), -1f, 1f);
            float cosA = math.clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            float aim = math.atan2(d2.y, d2.x);
            float upperWorld = aim + bendSign * math.acos(cosA);
            var u = local[at + upper];
            u.Rotation = upperWorld - parentRot;
            local[at + upper] = u;
            var lo = local[at + lower];
            lo.Rotation = -bendSign * (math.PI - math.acos(cosInner));
            local[at + lower] = lo;
        }

        /// <summary>Fixed 16-entry index buffer (no allocation in Burst).</summary>
        struct Span2
        {
            int4 m_A, m_B, m_C, m_D;
            public void Set(int i, int v)
            {
                switch (i >> 2)
                {
                    case 0: m_A[i & 3] = v; break;
                    case 1: m_B[i & 3] = v; break;
                    case 2: m_C[i & 3] = v; break;
                    default: m_D[i & 3] = v; break;
                }
            }
            public int Get(int i) => (i >> 2) switch { 0 => m_A[i & 3], 1 => m_B[i & 3], 2 => m_C[i & 3], _ => m_D[i & 3] };
        }
    }

    /// <summary>
    /// A skeleton and its clips in native arrays (built once, read by jobs through <see cref="View"/>). Author it
    /// in code: bones, then clips of per-bone keys (rotation in degrees and offset, both relative to the bind pose).
    /// </summary>
    public sealed class SkeletonAsset : IDisposable
    {
        public NativeArray<BoneDef> Bones;
        public NativeArray<BoneKey> Keys;
        public NativeArray<int2> Channels;
        public NativeArray<ClipInfo> Clips;
        readonly Dictionary<string, int> m_BoneNames;
        readonly Dictionary<string, int> m_ClipNames;

        SkeletonAsset(Dictionary<string, int> bones, Dictionary<string, int> clips)
        {
            m_BoneNames = bones;
            m_ClipNames = clips;
        }

        public SkeletonView View => new SkeletonView { Bones = Bones, Keys = Keys, Channels = Channels, Clips = Clips };
        public int BoneCount => Bones.Length;
        public int ClipCount => Clips.Length;
        public int Bone(string name) => m_BoneNames.TryGetValue(name, out int i) ? i : -1;
        public int Clip(string name) => m_ClipNames.TryGetValue(name, out int i) ? i : -1;

        public void Dispose()
        {
            if (Bones.IsCreated) Bones.Dispose();
            if (Keys.IsCreated) Keys.Dispose();
            if (Channels.IsCreated) Channels.Dispose();
            if (Clips.IsCreated) Clips.Dispose();
        }

        public sealed class Builder
        {
            readonly List<BoneDef> m_Bones = new List<BoneDef>();
            readonly Dictionary<string, int> m_BoneNames = new Dictionary<string, int>();
            readonly List<(string name, ClipInfo info, List<(int bone, BoneKey key)> keys)> m_Clips = new List<(string, ClipInfo, List<(int, BoneKey)>)>();

            /// <summary>Adds a bone (parent by name, null for the root); rotation in degrees.</summary>
            public Builder Bone(string name, string parent, float2 position, float rotationDegrees, float length)
            {
                int p = parent == null ? -1 : m_BoneNames[parent];
                m_BoneNames[name] = m_Bones.Count;
                m_Bones.Add(new BoneDef { Parent = p, Position = position, Rotation = math.radians(rotationDegrees), Length = length });
                return this;
            }

            public ClipBuilder Clip(string name, float duration, bool loop)
            {
                var keys = new List<(int, BoneKey)>();
                m_Clips.Add((name, new ClipInfo { Duration = duration, Loop = loop }, keys));
                return new ClipBuilder(this, keys);
            }

            public sealed class ClipBuilder
            {
                readonly Builder m_Owner;
                readonly List<(int bone, BoneKey key)> m_Keys;
                internal ClipBuilder(Builder owner, List<(int, BoneKey)> keys) { m_Owner = owner; m_Keys = keys; }

                /// <summary>A key: rotation (degrees) and offset relative to the bind pose.</summary>
                public ClipBuilder Key(string bone, float time, float rotationDegrees, float2 offset = default)
                {
                    m_Keys.Add((m_Owner.m_BoneNames[bone], new BoneKey { Time = time, Rotation = math.radians(rotationDegrees), Offset = offset }));
                    return this;
                }
            }

            public SkeletonAsset Build()
            {
                var clipNames = new Dictionary<string, int>();
                int bones = m_Bones.Count;
                int keyCount = 0;
                foreach (var c in m_Clips) keyCount += c.keys.Count;
                var asset = new SkeletonAsset(new Dictionary<string, int>(m_BoneNames), clipNames)
                {
                    Bones = new NativeArray<BoneDef>(m_Bones.ToArray(), Allocator.Persistent),
                    Keys = new NativeArray<BoneKey>(math.max(keyCount, 1), Allocator.Persistent),
                    Channels = new NativeArray<int2>(math.max(bones * m_Clips.Count, 1), Allocator.Persistent),
                    Clips = new NativeArray<ClipInfo>(math.max(m_Clips.Count, 1), Allocator.Persistent),
                };
                int write = 0;
                for (int c = 0; c < m_Clips.Count; c++)
                {
                    var (name, info, keys) = m_Clips[c];
                    clipNames[name] = c;
                    asset.Clips[c] = info;
                    for (int b = 0; b < bones; b++)
                    {
                        int first = write;
                        // Keys of this bone, in time order (stable for equal times).
                        var own = new List<BoneKey>();
                        foreach (var (bone, key) in keys) if (bone == b) own.Add(key);
                        own.Sort((x, y) => x.Time.CompareTo(y.Time));
                        foreach (var k in own) asset.Keys[write++] = k;
                        asset.Channels[c * bones + b] = new int2(first, write - first);
                    }
                }
                return asset;
            }
        }
    }
}
