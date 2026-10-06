using System;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using Unity.Mathematics;
using UnityEngine;

namespace StoryFoundation.Presentation
{
    /// <summary>Procedural scene art: night sky, hills, the square or the bridge, lanterns, Mira's expressions.</summary>
    public sealed class StArt : IDisposable
    {
        public SpriteSheet Sheet { get; private set; }
        public int Pixel, Hill, House, Bridge, Lantern, Glow, Star, Moon;
        public readonly int[] Faces = new int[5];

        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);

        public static StArt Build()
        {
            var art = new StArt();
            var atlas = new SpriteAtlasBuilder();
            int Add(int w, int h, Action<PixelCanvas> draw) { var c = new PixelCanvas(w, h); draw(c); return atlas.Add(c); }
            art.Pixel = Add(4, 4, c => c.Rect(0, 0, 4, 4, C(255, 255, 255)));
            art.Hill = Add(64, 16, c => c.Ellipse(32, 0, 32f, 15f, C(255, 255, 255)));
            art.House = Add(16, 16, c =>
            {
                c.Rect(2, 0, 12, 9, C(255, 255, 255));
                for (int y = 0; y < 7; y++) c.Rect(1 + y, 9 + y, 14 - 2 * y, 1, C(255, 255, 255));
                c.Rect(6, 0, 4, 5, C(120, 120, 120)); c.Rect(4, 5, 2, 2, C(200, 200, 160)); c.Rect(10, 5, 2, 2, C(200, 200, 160));
            });
            art.Bridge = Add(48, 12, c =>
            {
                for (int x = 0; x < 48; x++) { int y = (int)(6f * math.sin(x / 47f * math.PI)); c.Rect(x, y, 1, 3, C(255, 255, 255)); }
                for (int x = 4; x < 48; x += 8) { int y = (int)(6f * math.sin(x / 47f * math.PI)); c.Rect(x, y + 3, 1, 4, C(255, 255, 255)); }
            });
            art.Lantern = Add(8, 11, c =>
            {
                c.Rect(3, 9, 2, 2, C(60, 40, 30));
                c.Ellipse(4, 5, 3.6f, 4.2f, C(255, 255, 255));
                c.Rect(1, 3, 6, 1, C(200, 200, 200)); c.Rect(1, 6, 6, 1, C(200, 200, 200));
            });
            art.Glow = Add(16, 16, c => { for (int r = 8; r >= 1; r--) c.Ellipse(8, 8, r, r, C(255, 255, 255, (byte)(255 * (1f - r / 8.5f) * (1f - r / 8.5f)))); });
            art.Star = Add(3, 3, c => { c.Set(1, 0, C(255, 255, 255)); c.Set(0, 1, C(255, 255, 255)); c.Set(1, 1, C(255, 255, 255)); c.Set(2, 1, C(255, 255, 255)); c.Set(1, 2, C(255, 255, 255)); });
            art.Moon = Add(16, 16, c => { c.Ellipse(8, 8, 7f, 7f, C(250, 245, 220)); c.Ellipse(11, 10, 5f, 5f, C(0, 0, 0, 0)); });
            for (int e = 0; e < 5; e++)
            {
                var expr = (Expression)e;
                art.Faces[e] = Add(24, 30, c => Face(c, expr));
            }
            art.Sheet = atlas.Build();
            return art;
        }

        static void Face(PixelCanvas c, Expression e)
        {
            var hair = C(60, 35, 45);
            var skin = C(250, 220, 195);
            c.Rect(3, 0, 18, 9, C(170, 60, 70));                              // shoulders (red festival coat)
            c.Rect(9, 8, 6, 3, skin);                                           // neck
            c.Ellipse(12, 17, 8.5f, 9.5f, hair);                               // hair back
            c.Ellipse(12, 16, 7f, 8f, skin);                                    // face
            c.Rect(5, 21, 14, 4, hair); c.Rect(4, 13, 2, 10, hair); c.Rect(18, 13, 2, 10, hair);
            var ink = C(40, 30, 40);
            switch (e)
            {
                case Expression.Smile:
                    c.Rect(8, 17, 3, 1, ink); c.Rect(14, 17, 3, 1, ink); c.Set(8, 18, ink); c.Set(16, 18, ink);
                    c.Rect(10, 12, 5, 1, ink); c.Set(9, 13, ink); c.Set(15, 13, ink);
                    c.Rect(6, 14, 2, 1, C(240, 160, 160)); c.Rect(17, 14, 2, 1, C(240, 160, 160));
                    break;
                case Expression.Worried:
                    c.Rect(8, 16, 2, 2, ink); c.Rect(15, 16, 2, 2, ink);
                    c.Set(7, 19, ink); c.Set(8, 20, ink); c.Set(17, 19, ink); c.Set(16, 20, ink);
                    c.Rect(10, 12, 5, 1, ink);
                    break;
                case Expression.Sad:
                    c.Rect(8, 16, 3, 1, ink); c.Rect(14, 16, 3, 1, ink);
                    c.Rect(10, 12, 5, 1, ink); c.Set(9, 11, ink); c.Set(15, 11, ink);
                    c.Rect(9, 14, 1, 2, C(140, 190, 240));
                    break;
                case Expression.Surprised:
                    c.Rect(8, 16, 2, 3, ink); c.Rect(15, 16, 2, 3, ink);
                    c.Ellipse(12.5f, 12f, 1.6f, 1.8f, ink);
                    break;
                default:
                    c.Rect(8, 16, 2, 2, ink); c.Rect(15, 16, 2, 2, ink);
                    c.Rect(10, 12, 5, 1, ink);
                    break;
            }
            c.Outline(C(30, 20, 30));
        }

        public void Dispose()
        {
            Sheet?.Dispose();
            Sheet = null;
        }
    }

    /// <summary>Draws the current scene from the story state (rebuilt only when the state's version changes).</summary>
    [DefaultExecutionOrder(500)]
    public sealed class StRenderer : MonoBehaviour
    {
        public SessionHost Host;
        RenderAssets m_Assets;
        StArt m_Art;
        SpriteBatch m_Scene, m_Glow;
        SimSession m_Session;
        int m_Version = -1;
        float m_Portrait;   // 0..1 fade/slide of Mira's portrait

        public int SpritesDrawn { get; private set; }
        public float PortraitShown => m_Portrait;

        void OnDestroy()
        {
            m_Scene?.Dispose(); m_Glow?.Dispose();
            m_Art?.Dispose();
            m_Assets?.Dispose();
        }

        void Bind(SimSession session)
        {
            OnDestroy();
            m_Session = session;
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = StArt.Build();
            m_Scene = new SpriteBatch(m_Assets.Tier, m_Art.Sheet.Texture, BlendKind.Opaque, 256);
            m_Glow = new SpriteBatch(m_Assets.Tier, m_Art.Sheet.Texture, BlendKind.Additive, 64);
            // Preallocate dynamic pages and prefix textures at session bind, before counts grow in play.
            m_Scene.Warmup(m_Scene.Capacity);
            m_Glow.Warmup(m_Glow.Capacity);
            m_Version = -1;
        }

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var s = session.World.Resource(StKeys.State);
            float target = s.InStory && s.MiraShown ? 1f : 0f;
            m_Portrait = math.lerp(m_Portrait, target, 1f - math.exp(-8f * Time.deltaTime));
            m_Scene.Clear();
            m_Glow.Clear();
            var sheet = m_Art.Sheet;
            float t = Time.time;

            // Sky, stars, moon, hills.
            for (int i = 0; i < 6; i++)
            {
                float k = i / 5f;
                m_Scene.Add(new float2(0f, 5f - i * 1.6f), new float2(30f, 1.62f), sheet[m_Art.Pixel].Uv, 9f, new float4(math.lerp(0.04f, 0.16f, k), math.lerp(0.05f, 0.12f, k), math.lerp(0.16f, 0.3f, k), 1f));
            }
            for (int i = 0; i < 24; i++)
            {
                float2 p = new float2(math.frac(i * 0.618f) * 16f - 8f, 1.5f + math.frac(i * 0.377f) * 3.2f);
                float tw = 0.6f + 0.4f * math.sin(t * 2f + i);
                m_Scene.Add(p, new float2(0.09f), sheet[m_Art.Star].Uv, 8.5f, new float4(tw, tw, tw * 0.9f, 1f));
            }
            m_Scene.Add(new float2(5f, 3.6f), new float2(1.1f), sheet[m_Art.Moon].Uv, 8.4f, new float4(1f));
            m_Scene.Add(new float2(-4f, -1.9f), new float2(12f, 3f), sheet[m_Art.Hill].Uv, 8f, new float4(0.08f, 0.12f, 0.16f, 1f));
            m_Scene.Add(new float2(4.5f, -2.2f), new float2(14f, 3.2f), sheet[m_Art.Hill].Uv, 7.9f, new float4(0.06f, 0.1f, 0.13f, 1f));
            m_Scene.Add(new float2(0f, -4.2f), new float2(30f, 2f), sheet[m_Art.Pixel].Uv, 7f, new float4(0.12f, 0.1f, 0.12f, 1f));

            if (s.Background == 0)
            {
                for (int i = 0; i < 4; i++)
                    m_Scene.Add(new float2(-6.5f + i * 2.2f, -2.4f), new float2(1.8f), sheet[m_Art.House].Uv, 6f, new float4(0.25f, 0.22f, 0.3f, 1f));
            }
            else
                m_Scene.Add(new float2(-1f, -2.6f), new float2(8f, 2f), sheet[m_Art.Bridge].Uv, 6f, new float4(0.35f, 0.25f, 0.2f, 1f));

            // Lanterns on a string; lit ones glow.
            m_Scene.Add(new float2(-3f, 1.2f), new float2(7f, 0.03f), sheet[m_Art.Pixel].Uv, 5.5f, new float4(0.4f, 0.35f, 0.3f, 1f));
            for (int i = 0; i < 3; i++)
            {
                float2 p = new float2(-5.5f + i * 2.5f, 0.75f + 0.05f * math.sin(t * 1.5f + i));
                bool lit = i < s.Lanterns;
                m_Scene.Add(p, new float2(0.55f, 0.75f), sheet[m_Art.Lantern].Uv, 5f, lit ? new float4(1.6f, 0.9f, 0.4f, 1f) : new float4(0.45f, 0.3f, 0.3f, 1f));
                if (lit)
                {
                    float flicker = 0.85f + 0.15f * math.sin(t * 11f + i * 2f);
                    m_Glow.Add(p, new float2(2.6f), sheet[m_Art.Glow].Uv, 4f, new float4(1f, 0.6f, 0.25f, 0.6f * flicker));
                }
            }

            // Mira slides in from the right.
            if (m_Portrait > 0.01f)
            {
                float2 at = new float2(4.2f + (1f - m_Portrait) * 3f, -0.4f);
                float bob = 0.04f * math.sin(t * 1.3f);
                m_Scene.Add(at + new float2(0f, bob), new float2(4f, 5f), sheet[m_Art.Faces[(int)s.Face]].Uv, 3f, new float4(m_Portrait, m_Portrait, m_Portrait, 1f));
            }
            var bounds = new Bounds(Vector3.zero, new Vector3(1e4f, 1e4f, 100f));
            m_Scene.Draw(bounds);
            m_Glow.Draw(bounds);
            SpritesDrawn = m_Scene.Count + m_Glow.Count;
        }
    }
}
