using System;
using SPF.Contracts.Pooling;
using UnityEngine;
using UnityEngine.UI;

namespace SPF.Shell.UI
{
    /// <summary>
    /// A UGUI label that builds its mesh straight from a char buffer: no string is ever made, so HUD counters that
    /// change every frame (timers, kill counts) and typewriter reveals allocate nothing. Glyphs come from a dynamic
    /// font (the same one <see cref="UIFactory"/> labels use); characters are requested from the font atlas through
    /// a per-character string cache, so each distinct character costs one tiny string once per process.
    /// Supports '\n', word wrap to the rect width (CJK text breaks anywhere), the nine <see cref="TextAnchor"/>
    /// alignments and <see cref="MaxVisible"/> for typewriter reveals. No rich text.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BufferText : MaskableGraphic
    {
        static readonly string[] s_CharStrings = new string[0x10000];
        /// <summary>Printable ASCII, requested from the font atlas up front so HUD digits never grow it mid-game.</summary>
        static readonly string s_Ascii = BuildAscii();

        static string BuildAscii()
        {
            var chars = new char[126 - 32 + 1];
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)(32 + i);
            return new string(chars);
        }

        [SerializeField] Font m_Font;
        [SerializeField] int m_FontSize = 32;
        [SerializeField] TextAnchor m_Alignment = TextAnchor.UpperLeft;
        [SerializeField] float m_LineSpacing = 1.15f;
        [SerializeField] bool m_Wrap;

        readonly TextBuilder m_Builder = new TextBuilder(64);
        char[] m_Shown = new char[64];
        int m_ShownLength;
        int m_MaxVisible = int.MaxValue;
        float[] m_LineWidths = new float[8];
        int[] m_LineEnds = new int[8];
        int m_Rebuilds;
        int m_PrewarmedSize = -1;
        bool m_Populating, m_FontDirty;

        public Font Font
        {
            get => m_Font;
            set { if (m_Font == value) return; Unhook(); m_Font = value; Hook(); SetAllDirty(); }
        }

        public int FontSize { get => m_FontSize; set { if (m_FontSize == value) return; m_FontSize = value; SetVerticesDirty(); } }
        public TextAnchor Alignment { get => m_Alignment; set { m_Alignment = value; SetVerticesDirty(); } }
        public bool Wrap { get => m_Wrap; set { m_Wrap = value; SetVerticesDirty(); } }

        /// <summary>Characters drawn (typewriter); the layout always uses the whole text, so lines don't jump.</summary>
        public int MaxVisible
        {
            get => m_MaxVisible;
            set { value = Math.Max(0, value); if (m_MaxVisible == value) return; m_MaxVisible = value; SetVerticesDirty(); }
        }

        public int Length => m_ShownLength;

        /// <summary>Mesh rebuilds so far (one per visible change).</summary>
        public int Rebuilds => m_Rebuilds;

        public override Texture mainTexture => m_Font != null && m_Font.material != null ? m_Font.material.mainTexture : base.mainTexture;

        public char this[int index] => m_Shown[index];

        /// <summary>Starts a new text; finish with <see cref="Commit"/>.</summary>
        public TextBuilder Begin() => m_Builder.Clear();

        /// <summary>Shows what was built since <see cref="Begin"/>; returns true (and rebuilds the mesh) only if it changed.</summary>
        public bool Commit()
        {
            int length = m_Builder.Length;
            bool same = length == m_ShownLength;
            if (same)
                for (int i = 0; i < length; i++)
                    if (m_Builder[i] != m_Shown[i]) { same = false; break; }
            if (same) return false;
            if (m_Shown.Length < length) m_Shown = new char[Math.Max(length, m_Shown.Length * 2)];
            m_Builder.CopyTo(m_Shown);
            m_ShownLength = length;
            SetVerticesDirty();
            return true;
        }

        /// <summary>Shows a whole string (a dialogue line); only allocates if the buffer has to grow.</summary>
        public bool SetText(string text)
        {
            Begin().Append(text);
            return Commit();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Hook();
        }

        protected override void OnDisable()
        {
            Unhook();
            base.OnDisable();
        }

        void Hook() { if (m_Font != null) Font.textureRebuilt += OnFontRebuilt; }
        void Unhook() { Font.textureRebuilt -= OnFontRebuilt; }

        void OnFontRebuilt(Font font)
        {
            // The atlas can rebuild inside UGUI's graphic rebuild (another label requesting glyphs): marking dirty there
            // is not allowed, so re-mesh on the next LateUpdate. Our own requests happen before we read any glyph.
            if (font == m_Font && !m_Populating) m_FontDirty = true;
        }

        void LateUpdate()
        {
            if (!m_FontDirty) return;
            m_FontDirty = false;
            SetVerticesDirty();
        }

        static string CharString(char c) => s_CharStrings[c] ??= c.ToString();

        static bool IsCjk(char c) => c >= 0x2E80 && c <= 0x9FFF || c >= 0xAC00 && c <= 0xD7AF || c >= 0xF900 && c <= 0xFAFF || c >= 0xFF00 && c <= 0xFFEF;

        bool Glyph(char c, int size, out CharacterInfo info)
        {
            if (m_Font.GetCharacterInfo(c, out info, size, FontStyle.Normal)) return true;
            m_Font.RequestCharactersInTexture(CharString(c), size, FontStyle.Normal);
            return m_Font.GetCharacterInfo(c, out info, size, FontStyle.Normal);
        }

        int AddLine(int lines, int end, float width)
        {
            if (lines == m_LineEnds.Length)
            {
                Array.Resize(ref m_LineEnds, lines * 2);
                Array.Resize(ref m_LineWidths, lines * 2);
            }
            m_LineEnds[lines] = end;
            m_LineWidths[lines] = width;
            return lines + 1;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            m_Rebuilds++;
            if (m_Font == null || m_ShownLength == 0) return;

            float scale = canvas != null ? Mathf.Max(canvas.scaleFactor, 0.01f) : 1f;
            int size = Mathf.Clamp(Mathf.RoundToInt(m_FontSize * scale), 1, 512);
            float inv = 1f / scale;
            var rect = GetPixelAdjustedRect();

            // Pass 0: make sure every glyph is in the atlas before reading any (a request can rebuild the atlas
            // and move earlier glyphs). ASCII once per size, then only characters outside it.
            m_Populating = true;
            if (size != m_PrewarmedSize)
            {
                m_Font.RequestCharactersInTexture(s_Ascii, size, FontStyle.Normal);
                m_PrewarmedSize = size;
            }
            for (int i = 0; i < m_ShownLength; i++)
            {
                char c = m_Shown[i];
                if (c >= 32 && c <= 126) continue;
                if (!m_Font.GetCharacterInfo(c, out _, size, FontStyle.Normal))
                    m_Font.RequestCharactersInTexture(CharString(c), size, FontStyle.Normal);
            }
            m_Populating = false;

            // Pass 1: line breaks (explicit, and word wrap at the rect width).
            int lines = 0;
            int lineStart = 0, lastBreak = -1;
            float width = 0f, widthAtBreak = 0f;
            for (int i = 0; i < m_ShownLength; i++)
            {
                char c = m_Shown[i];
                if (c == '\n')
                {
                    lines = AddLine(lines, i, width);
                    lineStart = i + 1; lastBreak = -1; width = 0f;
                    continue;
                }
                float advance = Glyph(c, size, out var info) ? info.advance * inv : 0f;
                if (m_Wrap && width + advance > rect.width && i > lineStart)
                {
                    if (c == ' ') { lines = AddLine(lines, i, width); lineStart = i + 1; lastBreak = -1; width = 0f; continue; }
                    if (lastBreak > lineStart && !IsCjk(c))
                    {
                        lines = AddLine(lines, lastBreak, widthAtBreak);
                        lineStart = lastBreak + 1;
                        width = 0f;
                        for (int k = lineStart; k < i; k++) width += Glyph(m_Shown[k], size, out var ki) ? ki.advance * inv : 0f;
                        lastBreak = -1;
                    }
                    else
                    {
                        lines = AddLine(lines, i, width);
                        lineStart = i; lastBreak = -1; width = 0f;
                    }
                }
                if (c == ' ') { lastBreak = i; widthAtBreak = width; }
                width += advance;
            }
            lines = AddLine(lines, m_ShownLength, width);

            // Pass 2: quads.
            float lineHeight = m_FontSize * m_LineSpacing;
            float ascent = m_FontSize * 0.8f;
            float total = lines * lineHeight;
            int h = (int)m_Alignment % 3, v = (int)m_Alignment / 3;
            float top = v == 0 ? rect.yMax : v == 1 ? rect.center.y + total * 0.5f : rect.yMin + total;
            var color32 = (Color32)color;
            var vert = UIVertex.simpleVert;
            vert.color = color32;
            int start = 0, drawn = 0, quads = 0;
            for (int line = 0; line < lines; line++)
            {
                int end = m_LineEnds[line];
                float x = h == 0 ? rect.xMin : h == 1 ? rect.center.x - m_LineWidths[line] * 0.5f : rect.xMax - m_LineWidths[line];
                float baseline = top - ascent - line * lineHeight;
                for (int i = start; i < end; i++)
                {
                    char c = m_Shown[i];
                    if (c == '\n') continue;
                    if (drawn++ >= m_MaxVisible) return;
                    if (!Glyph(c, size, out var info)) continue;
                    if (c != ' ')
                    {
                        float x0 = x + info.minX * inv, x1 = x + info.maxX * inv;
                        float y0 = baseline + info.minY * inv, y1 = baseline + info.maxY * inv;
                        int b = quads * 4;
                        vert.position = new Vector3(x0, y0); vert.uv0 = info.uvBottomLeft; vh.AddVert(vert);
                        vert.position = new Vector3(x0, y1); vert.uv0 = info.uvTopLeft; vh.AddVert(vert);
                        vert.position = new Vector3(x1, y1); vert.uv0 = info.uvTopRight; vh.AddVert(vert);
                        vert.position = new Vector3(x1, y0); vert.uv0 = info.uvBottomRight; vh.AddVert(vert);
                        vh.AddTriangle(b, b + 1, b + 2);
                        vh.AddTriangle(b, b + 2, b + 3);
                        quads++;
                    }
                    x += info.advance * inv;
                }
                start = end;
                if (start < m_ShownLength && (m_Shown[start] == '\n' || m_Shown[start] == ' ')) start++;
            }
        }

        static Font s_SharedFont;

        /// <summary>
        /// A dynamic font of its own for buffer labels: its atlas rebuilds (glyphs evicted and re-added) then never
        /// make every UGUI <c>Text</c> on the shared built-in font regenerate, which allocates inside UGUI.
        /// Falls back to the built-in font where no OS font can be created.
        /// </summary>
        public static Font SharedFont
        {
            get
            {
                if (s_SharedFont != null) return s_SharedFont;
                try
                {
                    s_SharedFont = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", "Helvetica", "Arial", "Roboto", "Liberation Sans", "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC" }, 32);
                }
                catch (Exception) { s_SharedFont = null; }
                if (s_SharedFont == null) s_SharedFont = UIFactory.Font;
                return s_SharedFont;
            }
        }

        /// <summary>A label like <see cref="UIFactory.Label"/>, but allocation-free.</summary>
        public static BufferText Create(Transform parent, string name, int size, TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(16, 8);
            rect.offsetMax = new Vector2(-16, -8);
            var label = go.AddComponent<BufferText>();
            label.Font = SharedFont;
            label.m_FontSize = size;
            label.m_Alignment = anchor;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>Builds the mesh into <paramref name="vh"/> (tests, custom batching).</summary>
        public void PopulateMesh(VertexHelper vh) => OnPopulateMesh(vh);

        /// <summary>The shown text as a string (tests and debugging only; allocates).</summary>
        public override string ToString() => new string(m_Shown, 0, m_ShownLength);
    }
}
