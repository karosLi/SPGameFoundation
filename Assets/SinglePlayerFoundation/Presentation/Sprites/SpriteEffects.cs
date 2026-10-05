using Unity.Mathematics;

namespace SPF.Presentation.Sprites
{
    /// <summary>
    /// Pool of one-shot world effects (hit sparks, explosions, death puffs, rising numbers): each plays a
    /// clip (or shows a number) at a position, optionally drifting, scaling and fading, then frees itself.
    /// Fixed capacity; when full the oldest effect is replaced. Presentation-only (frame time).
    /// </summary>
    public sealed class SpriteEffects
    {
        public struct Effect
        {
            public SpriteClip Clip;
            public float2 Position;
            public float2 Velocity;
            public float2 Size;
            public float Rotation;
            public float Age;
            public float Life;          // seconds; 0 = clip duration
            public float4 Color;
            public float ScaleFrom, ScaleTo;
            public bool Fade;
            public int Number;          // >= 0 with NumberMode: drawn as digits
            public bool NumberMode;
            public char Prefix, Suffix;
            public float Depth;
        }

        readonly Effect[] m_Effects;
        int m_Next;

        public SpriteEffects(int capacity) => m_Effects = new Effect[capacity];

        public int Active { get; private set; }

        public void Spawn(in Effect effect)
        {
            var e = effect;
            if (e.Life <= 0f) e.Life = e.Clip.Duration;
            if (e.ScaleFrom == 0f && e.ScaleTo == 0f) e.ScaleFrom = e.ScaleTo = 1f;
            e.Age = 0f;
            // Prefer a free slot; otherwise overwrite the oldest (round robin).
            for (int k = 0; k < m_Effects.Length; k++)
            {
                int i = (m_Next + k) % m_Effects.Length;
                if (m_Effects[i].Life <= 0f || m_Effects[i].Age >= m_Effects[i].Life)
                {
                    m_Effects[i] = e;
                    m_Next = (i + 1) % m_Effects.Length;
                    return;
                }
            }
            m_Effects[m_Next] = e;
            m_Next = (m_Next + 1) % m_Effects.Length;
        }

        public void Clear()
        {
            for (int i = 0; i < m_Effects.Length; i++) m_Effects[i].Life = 0f;
        }

        /// <summary>Advances every effect and draws the live ones (sprites into <paramref name="batch"/>, numbers with <paramref name="font"/>).</summary>
        public void UpdateAndDraw(float dt, SpriteBatch batch, SpriteSheet sheet, SpriteFont font)
        {
            int active = 0;
            for (int i = 0; i < m_Effects.Length; i++)
            {
                ref var e = ref m_Effects[i];
                if (e.Life <= 0f || e.Age >= e.Life) continue;
                e.Age += dt;
                if (e.Age >= e.Life) continue;
                active++;
                e.Position += e.Velocity * dt;
                float t = e.Age / e.Life;
                float scale = math.lerp(e.ScaleFrom, e.ScaleTo, t);
                var color = e.Color;
                if (e.Fade) color.w *= 1f - t * t;
                if (e.NumberMode)
                {
                    font?.DrawNumber(batch, sheet, e.Number, e.Prefix, e.Suffix, e.Position, e.Size.y * scale, e.Depth, color);
                    continue;
                }
                int frame = e.Clip.FrameAt(e.Age);
                batch.Add(e.Position, e.Size * scale, sheet[frame].Uv, e.Depth, color, e.Rotation);
            }
            Active = active;
        }
    }
}
