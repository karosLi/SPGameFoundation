using System;
using SPF.Contracts;
using SPF.Presentation.Sprites;
using Unity.Mathematics;

namespace SPF.Presentation.Combat
{
    /// <summary>Read-only, detached world-space labels. The caller supplies accepted damage, never
    /// a requested attack amount. Scope belongs to one session/timeline/level and must be reset on rebind.
    /// All storage is allocated at construction; glyphs reuse the game's existing atlas and SpriteBatch.</summary>
    public sealed class DamageNumberPool
    {
        public const float MergeSeconds = .12f;
        public const int MaxDisplay = 999999;
        public struct Label
        {
            public EntityHandle Target;
            public float2 Anchor;
            public double Amount;
            public float Age;
            public bool Critical;
            public int Lane;
            public ulong Sequence;
            public long Tick;
        }
        readonly Label[] m_Labels;
        readonly float4[] m_Placed;
        object m_Owner;
        uint m_Revision;
        int m_Level;
        ulong m_LastSequence;
        int m_NormalAdmissions, m_CriticalAdmissions, m_NormalAttempts, m_CriticalAttempts;
        DamageNumberBudget m_Budget;
        public int Active { get; private set; }
        public int Capacity => m_Labels.Length;
        public DamageNumberBudget Budget => m_Budget;
        public DamageNumberDiagnostics Stats;
        public Label Read(int index)
        {
            if ((uint)index >= (uint)Active) throw new ArgumentOutOfRangeException(nameof(index));
            return m_Labels[index];
        }
        public DamageNumberPool(int capacity = 64)
        {
            if (capacity < 1 || capacity > 256) throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Labels = new Label[capacity]; m_Placed = new float4[capacity];
            m_Budget = DamageNumberBudget.ForQuality(0);
        }
        /// <summary>Clear presentation and dedup state on every bind, timeline restore, level reset or
        /// hidden-to-visible transition. An equal simulation tick is insufficient to identify a timeline.</summary>
        public bool Bind(object owner, uint revision, int levelVersion)
        {
            if (ReferenceEquals(owner, m_Owner) && revision == m_Revision && levelVersion == m_Level) return false;
            m_Owner = owner; m_Revision = revision; m_Level = levelVersion; Clear(); return true;
        }
        public void Clear()
        {
            Active = m_NormalAdmissions = m_CriticalAdmissions = m_NormalAttempts = m_CriticalAttempts = 0; m_LastSequence = 0; Stats = default;
        }
        public void BeginFrame(float dt, int quality)
        {
            m_Budget = DamageNumberBudget.ForQuality(quality);
            m_NormalAdmissions = m_CriticalAdmissions = m_NormalAttempts = m_CriticalAttempts = 0;
            Stats.Glyphs = Stats.Visible = Stats.GlyphDrops = Stats.OverlapDrops = 0; Stats.ScreenArea = 0;
            dt = math.isfinite(dt) ? math.max(0f, dt) : 0f;
            int count = 0;
            for (int i = 0; i < Active; i++)
            {
                var e = m_Labels[i]; e.Age += dt;
                if (e.Age >= Lifetime(e.Critical)) { Stats.Expired++; continue; }
                m_Labels[count++] = e;
            }
            Active = count;
            while (Active > math.min(Capacity, m_Budget.Active)) { Remove(Victim(default, false)); Stats.Evicted++; }
            // A lower quality also contracts already-live per-target lanes, without changing damage.
            for (int i = Active - 1; i >= 0; i--)
            {
                int same = 0;
                for (int j = 0; j < Active; j++) if (m_Labels[j].Target == m_Labels[i].Target) same++;
                if (same > m_Budget.PerTarget) { Remove(Victim(m_Labels[i].Target, true)); Stats.Evicted++; i = math.min(i, Active); }
            }
        }
        static float Lifetime(bool critical) => critical ? .65f : .72f;
        int Victim(EntityHandle target, bool sameTarget)
        {
            int candidate = -1;
            for (int i = 0; i < Active; i++)
            {
                var e = m_Labels[i]; if (sameTarget && e.Target != target) continue;
                if (candidate < 0 || !e.Critical && m_Labels[candidate].Critical ||
                    e.Critical == m_Labels[candidate].Critical && e.Sequence < m_Labels[candidate].Sequence) candidate = i;
            }
            return candidate;
        }
        void Remove(int index)
        {
            for (int i = index + 1; i < Active; i++) m_Labels[i - 1] = m_Labels[i];
            Active--;
        }
        /// <summary>Facts must arrive in their journal's increasing sequence order. Repeated, stale,
        /// rejected and merged sequences are consumed exactly once. Merge uses the first event's anchor
        /// and age, and double-precision sum of actual float amounts; it never refreshes lifespan.</summary>
        public DamageNumberAdmission Emit(EntityHandle target, float2 acceptedPosition, float actualAmount, bool critical, ulong sequence, long tick = 0, double stepSeconds = 1d / 60)
        {
            if (sequence == 0 || sequence <= m_LastSequence) { Stats.Duplicates++; return DamageNumberAdmission.Duplicate; }
            m_LastSequence = sequence;
            if (target.IsNull || target.Index < 0 || !math.all(math.isfinite(acceptedPosition)) || !math.isfinite(actualAmount) || actualAmount <= 0 || tick < 0 || double.IsNaN(stepSeconds) || double.IsInfinity(stepSeconds) || stepSeconds <= 0)
            { Stats.Dropped++; return DamageNumberAdmission.Dropped; }
            if (critical ? ++m_CriticalAttempts > m_Budget.Emissions * 4 : ++m_NormalAttempts > m_Budget.Emissions * 8)
            { Stats.Dropped++; return DamageNumberAdmission.Dropped; }
            int same = 0, occupied = 0;
            for (int i = 0; i < Active; i++)
            {
                ref var e = ref m_Labels[i]; if (e.Target != target) continue;
                same++; occupied |= 1 << e.Lane;
                if (e.Critical == critical && e.Age <= MergeSeconds && tick >= e.Tick && (tick - e.Tick) * stepSeconds <= MergeSeconds)
                {
                    bool wasClipped = e.Amount > MaxDisplay; e.Amount += actualAmount;
                    if (!wasClipped && e.Amount > MaxDisplay) Stats.DisplayOverflows++;
                    Stats.Merged++; Stats.AcceptedAmount += actualAmount; return DamageNumberAdmission.Merged;
                }
            }
            int admissions = critical ? m_CriticalAdmissions : m_NormalAdmissions;
            int limit = critical ? m_Budget.Emissions : m_Budget.Emissions * 3 / 4;
            if (admissions >= limit || m_NormalAdmissions + m_CriticalAdmissions >= m_Budget.Emissions)
            { Stats.Dropped++; return DamageNumberAdmission.Dropped; }
            int victim = same >= m_Budget.PerTarget ? Victim(target, true) :
                Active >= math.min(Capacity, m_Budget.Active) ? Victim(default, false) : -1;
            if (victim >= 0)
            {
                if (!critical || m_Labels[victim].Critical) { Stats.Dropped++; return DamageNumberAdmission.Dropped; }
                if (m_Labels[victim].Target == target) occupied &= ~(1 << m_Labels[victim].Lane);
                Remove(victim); Stats.Evicted++;
            }
            int lane = 0; while ((occupied & (1 << lane)) != 0) lane++;
            m_Labels[Active++] = new Label { Target = target, Anchor = acceptedPosition, Amount = actualAmount,
                Critical = critical, Lane = lane, Sequence = sequence, Tick = tick };
            if (critical) m_CriticalAdmissions++; else m_NormalAdmissions++;
            Stats.Accepted++; Stats.AcceptedAmount += actualAmount;
            if (actualAmount > MaxDisplay) Stats.DisplayOverflows++;
            Stats.HighWater = math.max(Stats.HighWater, Active); return DamageNumberAdmission.Accepted;
        }
        public static int DisplayAmount(double amount) => amount >= MaxDisplay ? MaxDisplay : (int)Math.Ceiling(Math.Max(0, amount));
        public static int DigitCount(int amount)
        {
            int count = 1; while (amount >= 10) { amount /= 10; count++; } return count;
        }
        /// <summary>Higher-priority labels submit first. Complete labels only; fixed glyph, viewport-area
        /// and overlap budgets prohibit truncating a value or hiding all critical feedback behind a flood.
        /// Heights are proportional to the orthographic world-view height, so they remain legible at
        /// portrait/landscape resolution. Three bounded local offsets avoid cross-target overlap.</summary>
        public void Draw(SpriteBatch batch, SpriteSheet sheet, SpriteFont font, float4 view, float depth = -3f)
        {
            Stats.Glyphs = Stats.Visible = Stats.GlyphDrops = Stats.OverlapDrops = 0; Stats.ScreenArea = 0;
            if (batch == null || sheet == null || font == null || !math.all(math.isfinite(view)) || view.z <= view.x || view.w <= view.y) return;
            float viewHeight = view.w - view.y, viewArea = (view.z - view.x) * viewHeight;
            for (int priority = 1; priority >= 0; priority--)
            for (int i = 0; i < Active; i++)
            {
                var e = m_Labels[i]; if ((e.Critical ? 1 : 0) != priority) continue;
                int value = DisplayAmount(e.Amount); bool clipped = e.Amount > MaxDisplay;
                int glyphs = DigitCount(value) + (e.Critical ? 1 : 0) + (clipped ? 1 : 0);
                float t = e.Age / Lifetime(e.Critical);
                float settle = math.max(0, 1f - e.Age / .18f);
                float punch = 1f + (e.Critical ? 1.15f : .18f) * settle * settle;
                float height = viewHeight * (e.Critical ? .028f : .025f) * punch;
                float glyphWidth = height * (3f * font.Scale + 2f) / (5f * font.Scale + 2f);
                float width = glyphWidth * (1f + .8f * (glyphs - 1));
                float area = glyphs * glyphWidth * height / viewArea;
                if (glyphs > m_Budget.Glyphs - Stats.Glyphs || glyphs > batch.Capacity - batch.Count || Stats.ScreenArea + area > m_Budget.ScreenArea)
                { Stats.GlyphDrops++; continue; }
                float side = (e.Target.Index & 1) == 0 ? -1f : 1f;
                float2 center = e.Anchor + new float2(side * e.Age * .12f,
                    viewHeight * .025f + height * e.Lane * 1.25f + e.Age * (e.Critical ? 2.2f - .85f * e.Age : 1.1f));
                if (center.x + width * .5f < view.x || center.x - width * .5f > view.z || center.y + height * .5f < view.y || center.y - height * .5f > view.w) continue;
                bool placed = false; float4 rectangle = default;
                for (int attempt = 0; attempt < 3 && !placed; attempt++)
                {
                    float2 adjusted = center + new float2(0, height * 1.15f * attempt);
                    rectangle = new float4(adjusted - new float2(width, height) * .55f, adjusted + new float2(width, height) * .55f);
                    if (rectangle.x < view.x || rectangle.z > view.z || rectangle.y < view.y || rectangle.w > view.w) continue;
                    bool overlaps = false;
                    for (int j = 0; j < Stats.Visible; j++)
                    {
                        var r = m_Placed[j];
                        if (rectangle.x < r.z && rectangle.z > r.x && rectangle.y < r.w && rectangle.w > r.y) { overlaps = true; break; }
                    }
                    if (!overlaps) { center = adjusted; placed = true; }
                }
                if (!placed) { Stats.OverlapDrops++; continue; }
                var color = e.Critical ? new float4(1f, .80f, .18f, 1f) : new float4(.96f, .93f, .80f, 1f);
                color.w *= 1f - math.saturate((t - .55f) / .45f);
                font.DrawNumber(batch, sheet, value, e.Critical ? '!' : '\0', clipped ? '+' : '\0', center, height, depth, color);
                m_Placed[Stats.Visible++] = rectangle; Stats.Glyphs += glyphs; Stats.ScreenArea += area;
                Stats.GlyphHighWater = math.max(Stats.GlyphHighWater, Stats.Glyphs);
            }
        }
    }
    public enum DamageNumberAdmission : byte { Accepted, Merged, Duplicate, Dropped }
    public struct DamageNumberBudget
    {
        public int Active, PerTarget, Emissions, Glyphs;
        public float ScreenArea;
        public static DamageNumberBudget ForQuality(int quality)
        {
            switch (math.clamp(quality, 0, 3))
            {
                case 0: return new DamageNumberBudget { Active = 64, PerTarget = 4, Emissions = 32, Glyphs = 192, ScreenArea = .14f };
                case 1: return new DamageNumberBudget { Active = 48, PerTarget = 3, Emissions = 24, Glyphs = 144, ScreenArea = .12f };
                case 2: return new DamageNumberBudget { Active = 32, PerTarget = 2, Emissions = 16, Glyphs = 96, ScreenArea = .09f };
                default: return new DamageNumberBudget { Active = 20, PerTarget = 2, Emissions = 12, Glyphs = 64, ScreenArea = .07f };
            }
        }
    }
    public struct DamageNumberDiagnostics
    {
        public int Accepted, Merged, Duplicates, Dropped, Evicted, Expired, DisplayOverflows, HighWater;
        public int Glyphs, Visible, GlyphDrops, OverlapDrops, GlyphHighWater;
        public double AcceptedAmount;
        public float ScreenArea;
    }
}
