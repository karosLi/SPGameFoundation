using System;
using SPF.Presentation.Sprites;
using Unity.Mathematics;

namespace SPF.Presentation.Combat
{
    /// <summary>Bounded visual-only burst pool. No GameObjects, materials, particles, strings or callbacks
    /// per event. Stable seed depends on caller event identity only; simulation RNG is never consumed.
    /// A bounded recent-key ring rejects repeated presentation of the same event. Spatial merges never
    /// extend life, preventing sustained fire from keeping an immortal effect alive.</summary>
    public sealed class CombatVfxPool
    {
        struct Burst { public VfxProfile Profile; public float2 Position; public float Age, Scale, Rotation; public uint Seed; }
        readonly Burst[] m_Bursts;
        readonly ulong[] m_Recent;
        int m_RecentCursor, m_Emissions, m_LowAttempts, m_MediumAttempts, m_CriticalAttempts;
        VfxBudget m_Budget;
        public int Active { get; private set; }
        public int Capacity => m_Bursts.Length;
        public VfxDiagnostics Stats;
        public VfxBudget Budget => m_Budget;

        public CombatVfxPool(int capacity = 96)
        {
            if (capacity < 1 || capacity > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Bursts = new Burst[capacity]; m_Recent = new ulong[capacity * 2];
            m_Budget = VfxBudget.ForQuality(0);
        }
        public void Clear()
        {
            Active = m_RecentCursor = m_Emissions = m_LowAttempts = m_MediumAttempts = m_CriticalAttempts = 0; Stats = default;
            Array.Clear(m_Bursts, 0, m_Bursts.Length); Array.Clear(m_Recent, 0, m_Recent.Length);
        }
        public void BeginFrame(float dt, int quality)
        {
            m_Budget = VfxBudget.ForQuality(quality); m_Emissions = m_LowAttempts = m_MediumAttempts = m_CriticalAttempts = 0;
            Stats.Sprites = Stats.SpriteDrops = 0; Stats.ScreenArea = 0;
            dt = math.isfinite(dt) ? math.max(0, dt) : 0;
            int live = 0;
            for (int i = 0; i < Active; i++)
            {
                var b = m_Bursts[i]; b.Age += dt;
                if (b.Age >= b.Profile.Duration) { Stats.Expired++; continue; }
                m_Bursts[live++] = b;
            }
            Active = live;
            while (Active > m_Budget.Active) { Remove(LowestPriority()); Stats.Evicted++; }
        }
        int LowestPriority()
        {
            int candidate = 0;
            for (int i = 1; i < Active; i++)
                if (m_Bursts[i].Profile.Priority < m_Bursts[candidate].Profile.Priority ||
                    m_Bursts[i].Profile.Priority == m_Bursts[candidate].Profile.Priority && m_Bursts[i].Age > m_Bursts[candidate].Age) candidate = i;
            return candidate;
        }
        void Remove(int i) { for (int j = i + 1; j < Active; j++) m_Bursts[j - 1] = m_Bursts[j]; Active--; }
        void Remember(ulong key) { if (key != 0) { m_Recent[m_RecentCursor] = key; m_RecentCursor = (m_RecentCursor + 1) % m_Recent.Length; } }
        public static uint VisualSeed(ulong key)
        {
            uint x = (uint)key ^ (uint)(key >> 32) ^ 0x9e3779b9u;
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return x == 0 ? 1u : x;
        }
        /// <param name="key">Unique nonzero sequence/tick + source identity. Zero opts out of deduplication.</param>
        public VfxAdmission Emit(in VfxProfile profile, float2 position, ulong key, float scale = 1f, float rotation = 0f)
        {
            if (!math.all(math.isfinite(position)) || !math.isfinite(scale) || scale <= 0 || !math.isfinite(rotation) ||
                !math.isfinite(profile.Duration) || profile.Duration <= 0 || !math.all(math.isfinite(profile.CoreSize)) ||
                !math.all(math.isfinite(profile.CoreColor)) || !math.all(math.isfinite(profile.AccentColor)) ||
                !math.isfinite(profile.GlowSize + profile.RingSize + profile.SparkTravel + profile.MergeRadius + profile.MergeSeconds))
            { Stats.Dropped++; return VfxAdmission.Dropped; }
            // Bound searching work as well as live objects. Priority 3 has its own lane:
            // duplicate/rejected death or muzzle requests must not consume the hero-critical reserve.
            bool attemptLimit = profile.Priority < 2 ? ++m_LowAttempts > m_Budget.Emissions * 4
                : profile.Priority < 3 ? ++m_MediumAttempts > m_Budget.Emissions * 2
                : ++m_CriticalAttempts > m_Budget.Emissions;
            if (attemptLimit)
            { Stats.Dropped++; return VfxAdmission.Dropped; }
            if (key != 0) for (int i = 0; i < m_Recent.Length; i++) if (m_Recent[i] == key) { Stats.Duplicates++; return VfxAdmission.Duplicate; }
            for (int i = 0; i < Active; i++)
            {
                ref var b = ref m_Bursts[i];
                if (b.Profile.Id == profile.Id && b.Age < profile.MergeSeconds &&
                    math.distancesq(b.Position, position) <= profile.MergeRadius * profile.MergeRadius)
                { Remember(key); Stats.Merged++; return VfxAdmission.Merged; }
            }
            // Leave one quarter of new-event slots for weapon/death/hero-critical feedback.
            int emissionLimit = profile.Priority < 2 ? m_Budget.Emissions * 3 / 4 : profile.Priority < 3 ? m_Budget.Emissions - 1 : m_Budget.Emissions;
            if (m_Emissions >= emissionLimit) { Stats.Dropped++; return VfxAdmission.Dropped; }
            if (Active >= math.min(Capacity, m_Budget.Active))
            {
                int oldest = LowestPriority();
                if (m_Bursts[oldest].Profile.Priority >= math.min((int)profile.Priority, 3)) { Stats.Dropped++; return VfxAdmission.Dropped; }
                Remove(oldest); Stats.Evicted++;
            }
            var safe = profile; safe.Priority = (byte)math.min(3, (int)profile.Priority); safe.Duration = math.min(2f, profile.Duration);
            safe.Sparks = (byte)math.min(8, (int)profile.Sparks);
            m_Bursts[Active++] = new Burst { Profile = safe, Position = position, Scale = math.min(scale, 4f), Rotation = rotation, Seed = VisualSeed(key) };
            m_Emissions++; Remember(key); Stats.Accepted++; return VfxAdmission.Accepted;
        }

        /// <summary>Caller clears batch once. Readable high-priority cores precede optional ring/spark/glow
        /// submission. Each transparent quad, including its empty corners, spends the screen-area budget.</summary>
        public void Draw(SpriteBatch batch, in CombatVfxUv uv, float4 view, float depth = -2f)
        {
            float area = math.max(0.01f, (view.z - view.x) * (view.w - view.y));
            for (int layer = 0; layer < 4; layer++) for (int priority = 3; priority >= 0; priority--)
            for (int i = 0; i < Active; i++)
            {
                var b = m_Bursts[i]; var p = b.Profile; if (p.Priority != priority) continue;
                float t = math.saturate(b.Age / p.Duration), fade = (1f - t) * (1f - t);
                if (layer == 0)
                {
                    float4 color = p.CoreColor; color.w *= fade;
                    Add(batch, b.Position, p.CoreSize * b.Scale * (1f - 0.5f * t), uv.Core, color, b.Rotation, depth, view, area);
                }
                else if (layer == 1 && p.RingSize > 0)
                {
                    float4 color = p.AccentColor; color.w *= fade * 0.65f;
                    Add(batch, b.Position, new float2(p.RingSize * b.Scale * (0.35f + t)), uv.Ring, color, 0, depth, view, area);
                }
                else if (layer == 2)
                {
                    int sparks = math.min((int)p.Sparks, m_Budget.SparksPerEffect);
                    var random = new Unity.Mathematics.Random(b.Seed);
                    for (int s = 0; s < sparks; s++)
                    {
                        float a = random.NextFloat(0, math.PI * 2f), speed = random.NextFloat(0.5f, 1f);
                        float2 dir = new float2(math.cos(a), math.sin(a));
                        float4 color = p.AccentColor; color.w *= 1f - t;
                        Add(batch, b.Position + dir * (p.SparkTravel * b.Scale * t * speed),
                            new float2((0.10f + 0.16f * speed) * (1f - t * 0.5f), 0.04f) * b.Scale,
                            uv.Streak, color, a, depth, view, area);
                    }
                }
                else if (layer == 3 && m_Budget.Glow && p.GlowSize > 0)
                {
                    float4 color = p.AccentColor; color.w *= fade * 0.22f;
                    Add(batch, b.Position, new float2(p.GlowSize * b.Scale), uv.Glow, color, 0, depth, view, area);
                }
            }
        }
        void Add(SpriteBatch batch, float2 position, float2 size, float4 uv, float4 color, float rotation, float depth, float4 view, float area)
        {
            size = math.min(math.abs(size), new float2(16f));
            float radius = math.length(size) * 0.5f;
            if (position.x + radius < view.x || position.x - radius > view.z || position.y + radius < view.y || position.y - radius > view.w || color.w <= 0.005f) return;
            float coverage = size.x * size.y / area;
            if (Stats.Sprites >= m_Budget.Sprites || Stats.ScreenArea + coverage > m_Budget.ScreenArea ||
                !batch.Add(position, size, uv, depth, color, rotation)) { Stats.SpriteDrops++; return; }
            Stats.Sprites++; Stats.ScreenArea += coverage;
        }
    }
}
