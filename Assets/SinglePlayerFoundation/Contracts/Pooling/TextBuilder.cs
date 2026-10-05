using System;

namespace SPF.Contracts.Pooling
{
    /// <summary>
    /// Allocation-free text formatting into a reusable char buffer (HUD counters, timers). A string is only
    /// made by <see cref="ToStringIfChanged"/>, and only when the characters differ from the last one, so a HUD
    /// that is rebuilt every frame allocates when what it shows changes, not per frame.
    /// </summary>
    public sealed class TextBuilder
    {
        char[] m_Chars;
        public int Length { get; private set; }

        public TextBuilder(int capacity = 64) => m_Chars = new char[capacity];

        public TextBuilder Clear() { Length = 0; return this; }

        public char this[int index] => m_Chars[index];

        /// <summary>Copies the characters into <paramref name="destination"/> (which must be at least <see cref="Length"/> long).</summary>
        public void CopyTo(char[] destination) => Array.Copy(m_Chars, destination, Length);

        void Grow(int extra)
        {
            if (Length + extra <= m_Chars.Length) return;
            Array.Resize(ref m_Chars, Math.Max(m_Chars.Length * 2, Length + extra));
        }

        public TextBuilder Append(char c)
        {
            Grow(1);
            m_Chars[Length++] = c;
            return this;
        }

        public TextBuilder Append(string s)
        {
            if (string.IsNullOrEmpty(s)) return this;
            Grow(s.Length);
            s.CopyTo(0, m_Chars, Length, s.Length);
            Length += s.Length;
            return this;
        }

        /// <summary>Appends an integer, left-padded with zeros to <paramref name="minDigits"/>.</summary>
        public TextBuilder Append(long value, int minDigits = 1)
        {
            if (value < 0)
            {
                Append('-');
                if (value == long.MinValue) return Append("9223372036854775808");
                value = -value;
            }
            int digits = 1;
            for (long v = value; v >= 10; v /= 10) digits++;
            if (digits < minDigits) digits = minDigits;
            Grow(digits);
            for (int i = digits - 1; i >= 0; i--)
            {
                m_Chars[Length + i] = (char)('0' + (int)(value % 10));
                value /= 10;
            }
            Length += digits;
            return this;
        }

        public TextBuilder Append(int value, int minDigits = 1) => Append((long)value, minDigits);

        /// <summary>Appends a fixed-point number with <paramref name="decimals"/> digits after the point (rounded).</summary>
        public TextBuilder Append(float value, int decimals)
        {
            if (float.IsNaN(value)) return Append("NaN");
            if (value < 0f) { Append('-'); value = -value; }
            long scale = 1;
            for (int i = 0; i < decimals; i++) scale *= 10;
            long fixedPoint = (long)Math.Round(value * (double)scale, MidpointRounding.AwayFromZero);
            Append(fixedPoint / scale);
            if (decimals > 0)
            {
                Append('.');
                Append(fixedPoint % scale, decimals);
            }
            return this;
        }

        public bool ContentEquals(string s)
        {
            if (s == null || s.Length != Length) return false;
            for (int i = 0; i < Length; i++) if (s[i] != m_Chars[i]) return false;
            return true;
        }

        /// <summary>Returns <paramref name="previous"/> when it already holds these characters, else a new string.</summary>
        public string ToStringIfChanged(string previous) => ContentEquals(previous) ? previous : new string(m_Chars, 0, Length);

        public override string ToString() => new string(m_Chars, 0, Length);
    }

    /// <summary>Cached decimal strings for small non-negative integers (scores, counts in labels and floating text).</summary>
    public static class NumberStrings
    {
        const int Cached = 1024;
        static readonly string[] s_Strings = new string[Cached];

        public static string Get(int value)
        {
            if ((uint)value >= Cached) return value.ToString();
            return s_Strings[value] ??= value.ToString();
        }
    }
}
