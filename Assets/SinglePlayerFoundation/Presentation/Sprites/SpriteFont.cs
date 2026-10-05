using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Sprites
{
    /// <summary>
    /// Tiny built-in 3x5 pixel font (digits and a few symbols) rendered into an atlas, for world-space numbers
    /// (damage, gold) drawn in the same sprite batch as everything else, i.e. no UI text per number.
    /// </summary>
    public sealed class SpriteFont
    {
        public const string Glyphs = "0123456789+-!xG";
        static readonly string[] s_Bitmaps =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
            "000010111010000", "000000111000000", "010010010000010", "000101010101000", "111100101101111",
        };

        readonly int m_First;
        public int Scale { get; }

        /// <summary>Adds the glyphs to <paramref name="atlas"/> (each 3x5 scaled by <paramref name="scale"/>, outlined).</summary>
        public SpriteFont(SpriteAtlasBuilder atlas, int scale = 2)
        {
            Scale = scale;
            m_First = atlas.AddStrip(Glyphs.Length, 3 * scale + 2, 5 * scale + 2, (canvas, g) =>
            {
                string bits = s_Bitmaps[g];
                for (int row = 0; row < 5; row++)
                for (int col = 0; col < 3; col++)
                    if (bits[row * 3 + col] == '1')
                        canvas.Rect(1 + col * scale, 1 + (4 - row) * scale, scale, scale, new Color32(255, 255, 255, 255));
                canvas.Outline(new Color32(20, 12, 16, 255));
            });
        }

        public int Frame(char c)
        {
            int i = Glyphs.IndexOf(c);
            return i < 0 ? -1 : m_First + i;
        }

        /// <summary>
        /// Draws <paramref name="text"/> centred at <paramref name="center"/>; glyph height in world units is
        /// <paramref name="height"/>. Unknown characters are skipped (spaces advance).
        /// </summary>
        public void Draw(SpriteBatch batch, SpriteSheet sheet, string text, float2 center, float height, float depth, float4 color)
        {
            float glyphW = height * (3f * Scale + 2f) / (5f * Scale + 2f);
            float advance = glyphW * 0.8f;
            float x = center.x - advance * (text.Length - 1) * 0.5f;
            for (int i = 0; i < text.Length; i++, x += advance)
            {
                int frame = Frame(text[i]);
                if (frame < 0) continue;
                batch.Add(new float2(x, center.y), new float2(glyphW, height), sheet[frame].Uv, depth, color);
            }
        }

        /// <summary>Allocation-free integer drawing (damage numbers every frame).</summary>
        public void DrawNumber(SpriteBatch batch, SpriteSheet sheet, int value, char prefix, char suffix, float2 center, float height, float depth, float4 color)
        {
            System.Span<char> chars = stackalloc char[12];
            int n = 0;
            if (prefix != '\0') chars[n++] = prefix;
            int start = n;
            int v = math.abs(value);
            do { chars[n++] = (char)('0' + v % 10); v /= 10; } while (v > 0 && n < 10);
            for (int a = start, b = n - 1; a < b; a++, b--) { char t = chars[a]; chars[a] = chars[b]; chars[b] = t; }
            if (suffix != '\0') chars[n++] = suffix;
            float glyphW = height * (3f * Scale + 2f) / (5f * Scale + 2f);
            float advance = glyphW * 0.8f;
            float x = center.x - advance * (n - 1) * 0.5f;
            for (int i = 0; i < n; i++, x += advance)
            {
                int frame = Frame(chars[i]);
                if (frame >= 0) batch.Add(new float2(x, center.y), new float2(glyphW, height), sheet[frame].Uv, depth, color);
            }
        }
    }
}
