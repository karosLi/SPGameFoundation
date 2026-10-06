using System.Collections.Generic;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using Unity.Mathematics;
using UnityEngine;

namespace PuzzleFoundation.Presentation
{
    /// <summary>
    /// Plays the board: each move's event log becomes tweens (swap, clears shrinking, bombs popping in,
    /// falls bouncing, new gems dropping from above), one animation step after another. The simulation has
    /// already finished the move; the renderer only shows it, and input waits while <see cref="Busy"/>.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class M3Renderer : MonoBehaviour
    {
        const int Pos = 0, Scale = 1;
        const float StepTime = 0.24f, SwapTime = 0.16f;

        struct Gem { public byte Color; public M3Special Special; public bool Alive; public float RemoveAt; }

        public SessionHost Host;
        public int2 Selected = new int2(-1);
        public int2 Hint = new int2(-1), HintTo = new int2(-1);
        public System.Action<M3Event> EventPlayed;

        RenderAssets m_Assets;
        M3Art m_Art;
        SpriteBatch m_Board, m_Gems, m_Overlay;
        SimSession m_Session;
        readonly TweenPlayer m_Tweens = new TweenPlayer();
        readonly List<Gem> m_GemList = new List<Gem>();
        readonly int[] m_At = new int[M3Board.Width * M3Board.Height];
        int m_LogSerial = -1, m_BoardSerial = -1;

        public bool Busy => m_Tweens.Busy;
        public TweenPlayer Tweens => m_Tweens;
        public int LiveGems { get; private set; }
        public int MovesAnimated { get; private set; }

        public static float2 CellCentre(int2 cell) => new float2(cell.x - M3Board.Width * 0.5f + 0.5f, cell.y - M3Board.Height * 0.5f + 0.5f);

        public static int2 CellAt(float2 world) => (int2)math.floor(world + new float2(M3Board.Width, M3Board.Height) * 0.5f);

        void OnDestroy() => Release();

        void Release()
        {
            m_Board?.Dispose(); m_Gems?.Dispose(); m_Overlay?.Dispose();
            m_Board = m_Gems = m_Overlay = null;
            m_Art?.Dispose(); m_Art = null;
            m_Assets?.Dispose(); m_Assets = null;
        }

        void Bind(SimSession session)
        {
            Release();
            m_Session = session;
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = M3Art.Build();
            m_Board = new SpriteBatch(m_Assets.Tier, m_Art.Sheet.Texture, BlendKind.Opaque, 128, queueOffset: -10);
            m_Gems = new SpriteBatch(m_Assets.Tier, m_Art.Sheet.Texture, BlendKind.Translucent, 512);
            m_Overlay = new SpriteBatch(m_Assets.Tier, m_Art.Sheet.Texture, BlendKind.Translucent, 64);
            // Preallocate dynamic pages and prefix textures at session bind, before counts grow in play.
            m_Board.Warmup(m_Board.Capacity);
            m_Gems.Warmup(m_Gems.Capacity);
            m_Overlay.Warmup(m_Overlay.Capacity);
            m_LogSerial = m_BoardSerial = -1;
        }

        int NewGem(byte color, M3Special special, float2 position, float scale)
        {
            int id = m_GemList.Count;
            m_GemList.Add(new Gem { Color = color, Special = special, Alive = true, RemoveAt = float.MaxValue });
            m_Tweens.Set(id, Pos, position);
            m_Tweens.Set(id, Scale, new float2(scale));
            return id;
        }

        void Rebuild(M3Board board)
        {
            m_GemList.Clear();
            m_Tweens.Clear();
            for (int y = 0; y < M3Board.Height; y++)
                for (int x = 0; x < M3Board.Width; x++)
                {
                    var c = new int2(x, y);
                    int i = M3Board.Index(c);
                    m_At[i] = board.Color[i] == 0 ? -1 : NewGem(board.Color[i], board.Special[i], CellCentre(c), 1f);
                }
        }

        /// <summary>Turns a move's events into tweens, step by step.</summary>
        void Play(M3Board board)
        {
            MovesAnimated++;
            float now = m_Tweens.Time;
            foreach (var e in board.Events)
            {
                float delay = e.Kind == M3EventKind.Swap ? 0f : SwapTime + (e.Step - 1) * StepTime;
                EventPlayed?.Invoke(e);
                switch (e.Kind)
                {
                    case M3EventKind.Swap:
                    case M3EventKind.SwapBack:
                    {
                        int ia = M3Board.Index(e.A), ib = M3Board.Index(e.B);
                        int ga = m_At[ia], gb = m_At[ib];
                        float d = e.Kind == M3EventKind.Swap ? 0f : SwapTime;
                        if (ga >= 0) m_Tweens.To(ga, Pos, CellCentre(e.B), SwapTime, d, Ease.InOutQuad);
                        if (gb >= 0) m_Tweens.To(gb, Pos, CellCentre(e.A), SwapTime, d, Ease.InOutQuad);
                        m_At[ia] = gb; m_At[ib] = ga;
                        break;
                    }
                    case M3EventKind.Clear:
                    {
                        int i = M3Board.Index(e.A), g = m_At[i];
                        if (g < 0) break;
                        m_Tweens.To(g, Scale, new float2(1.35f), StepTime * 0.35f, delay, Ease.OutQuad);
                        m_Tweens.To(g, Scale, new float2(0f), StepTime * 0.5f, delay + StepTime * 0.35f, Ease.InQuad);
                        var gem = m_GemList[g]; gem.RemoveAt = now + delay + StepTime; m_GemList[g] = gem;
                        m_At[i] = -1;
                        break;
                    }
                    case M3EventKind.MakeBomb:
                    {
                        int id = NewGem(e.Color, M3Special.Bomb, CellCentre(e.A), 0f);
                        m_Tweens.To(id, Scale, new float2(1f), StepTime, delay + StepTime * 0.6f, Ease.OutBack);
                        m_At[M3Board.Index(e.A)] = id;
                        break;
                    }
                    case M3EventKind.Fall:
                    {
                        int from = M3Board.Index(e.A), to = M3Board.Index(e.B), g = m_At[from];
                        if (g < 0) break;
                        m_Tweens.To(g, Pos, CellCentre(e.B), StepTime, delay, Ease.OutBounce);
                        m_At[to] = g;
                        m_At[from] = -1;
                        break;
                    }
                    case M3EventKind.Spawn:
                    {
                        int id = NewGem(e.Color, M3Special.None, CellCentre(e.A), 1f);
                        m_Tweens.To(id, Pos, CellCentre(e.B), StepTime, delay, Ease.OutBounce);
                        m_At[M3Board.Index(e.B)] = id;
                        break;
                    }
                    case M3EventKind.Shuffle:
                        m_Tweens.Finish();
                        Rebuild(board);
                        return;
                }
            }
        }

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var board = session.World.Resource(M3Keys.Board);
            if (board.BoardSerial != m_BoardSerial) { m_BoardSerial = board.BoardSerial; m_LogSerial = board.LogSerial; Rebuild(board); }
            else if (board.LogSerial != m_LogSerial) { m_LogSerial = board.LogSerial; Play(board); }
            m_Tweens.Advance(Time.deltaTime);

            var bounds = new Bounds(Vector3.zero, new Vector3(1e4f, 1e4f, 100f));
            m_Board.Clear(); m_Gems.Clear(); m_Overlay.Clear();
            for (int y = 0; y < M3Board.Height; y++)
                for (int x = 0; x < M3Board.Width; x++)
                    m_Board.Add(CellCentre(new int2(x, y)), new float2(1.001f), m_Art.Sheet[(x + y) % 2 == 0 ? m_Art.Cell : m_Art.Cell2].Uv, 5f, new float4(1f));
            int live = 0;
            float t = m_Tweens.Time;
            for (int g = 0; g < m_GemList.Count; g++)
            {
                var gem = m_GemList[g];
                if (!gem.Alive) continue;
                if (t >= gem.RemoveAt) { gem.Alive = false; m_GemList[g] = gem; continue; }
                float2 p = m_Tweens.Get(g, Pos, float2.zero);
                if (p.y > M3Board.Height * 0.5f + 0.2f) continue;   // still above the board
                float s = m_Tweens.Get(g, Scale, new float2(1f)).x;
                if (s <= 0.01f) continue;
                live++;
                m_Gems.Add(p, new float2(0.86f * s), m_Art.Sheet[m_Art.Gems[(gem.Color - 1) % m_Art.Gems.Length]].Uv, 3f, new float4(1f));
                if (gem.Special == M3Special.Bomb)
                    m_Gems.Add(p, new float2(0.5f * s), m_Art.Sheet[m_Art.Bomb].Uv, 2.9f, new float4(1f, 1f, 1f, 0.75f + 0.25f * math.sin(t * 8f)));
            }
            LiveGems = live;
            if (M3Board.InBounds(Selected))
                m_Overlay.Add(CellCentre(Selected), new float2(1f), m_Art.Sheet[m_Art.Frame].Uv, 2f, new float4(1f, 1f, 0.6f, 1f));
            if (M3Board.InBounds(Hint))
            {
                float pulse = 0.5f + 0.5f * math.sin(t * 6f);
                m_Overlay.Add(CellCentre(Hint), new float2(1f), m_Art.Sheet[m_Art.Frame].Uv, 2f, new float4(0.5f, 1f, 0.6f, pulse));
                m_Overlay.Add(CellCentre(HintTo), new float2(1f), m_Art.Sheet[m_Art.Frame].Uv, 2f, new float4(0.5f, 1f, 0.6f, pulse));
            }
            m_Board.Draw(bounds);
            m_Gems.Draw(bounds);
            m_Overlay.Draw(bounds);
        }
    }

    public sealed class M3Art : System.IDisposable
    {
        public SpriteSheet Sheet { get; private set; }
        public int[] Gems;
        public int Cell, Cell2, Bomb, Frame;

        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);

        public static M3Art Build()
        {
            var art = new M3Art();
            var atlas = new SpriteAtlasBuilder();
            int Single(int w, int h, System.Action<PixelCanvas> draw) { var c = new PixelCanvas(w, h); draw(c); return atlas.Add(c); }
            var colors = new[] { C(230, 60, 70), C(250, 160, 40), C(250, 220, 60), C(80, 200, 90), C(70, 140, 240), C(170, 80, 220) };
            art.Gems = new int[colors.Length];
            for (int k = 0; k < colors.Length; k++)
            {
                var col = colors[k];
                art.Gems[k] = Single(16, 16, c =>
                {
                    switch (k % 3)
                    {
                        case 0: c.Ellipse(8, 8, 6.5f, 6.5f, col); break;
                        case 1: for (int i = 0; i < 7; i++) c.Rect(8 - i, 1 + i, i * 2 + 1, 14 - 2 * i, col); break;
                        default: c.Rect(2, 2, 12, 12, col); break;
                    }
                    c.Ellipse(6, 10, 2f, 1.5f, C(255, 255, 255, 180));
                    c.Outline(C(30, 25, 40));
                });
            }
            art.Cell = Single(8, 8, c => c.Rect(0, 0, 8, 8, C(46, 40, 70)));
            art.Cell2 = Single(8, 8, c => c.Rect(0, 0, 8, 8, C(54, 48, 82)));
            art.Bomb = Single(12, 12, c => { c.Ring(6, 6, 3.5f, 5.5f, C(255, 255, 255)); c.Ellipse(6, 6, 2f, 2f, C(255, 240, 120)); });
            art.Frame = Single(16, 16, c => { c.Rect(0, 0, 16, 2, C(255, 255, 255)); c.Rect(0, 14, 16, 2, C(255, 255, 255)); c.Rect(0, 0, 2, 16, C(255, 255, 255)); c.Rect(14, 0, 2, 16, C(255, 255, 255)); });
            art.Sheet = atlas.Build();
            return art;
        }

        public void Dispose() { Sheet?.Dispose(); Sheet = null; }
    }
}
