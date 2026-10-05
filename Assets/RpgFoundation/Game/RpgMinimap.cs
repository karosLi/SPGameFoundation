using SPF.L1.Spatial;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace RpgFoundation.Game
{
    /// <summary>
    /// Corner minimap with fog of war: one texel per tile, revealed around the hero as it explores
    /// (walls only where seen), with the stairs, unopened chests, nearby monsters and the hero marked.
    /// Presentation only: the explored set restarts with each floor build (and after loading a save).
    /// </summary>
    public sealed class RpgMinimap : MonoBehaviour
    {
        public const int RevealRadius = 7;
        const float Refresh = 0.15f;

        static readonly Color32 Fog = new Color32(0, 0, 0, 140);
        static readonly Color32 FloorColor = new Color32(95, 92, 110, 230);
        static readonly Color32 WallColor = new Color32(45, 42, 55, 235);
        static readonly Color32 HeroColor = new Color32(255, 255, 255, 255);
        static readonly Color32 MonsterColor = new Color32(235, 60, 50, 255);
        static readonly Color32 EliteColor = new Color32(255, 150, 40, 255);
        static readonly Color32 StairsColor = new Color32(90, 230, 255, 255);
        static readonly Color32 ChestColor = new Color32(255, 210, 70, 255);

        RpgGameBootstrap m_Game;
        Texture2D m_Texture;
        Color32[] m_Pixels;
        bool[] m_Explored;
        int2 m_Size;
        int m_FloorBuilds = -1;
        float m_Next;

        public RawImage Image { get; private set; }
        public int ExploredCount { get; private set; }

        public void Build(RectTransform parent, RpgGameBootstrap game)
        {
            m_Game = game;
            var go = new GameObject("Minimap", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.80f, 0.62f);
            rect.anchorMax = new Vector2(0.99f, 0.86f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image = go.AddComponent<RawImage>();
            Image.raycastTarget = false;
        }

        public bool IsExplored(int2 cell) => m_Explored != null && math.all(cell >= 0) && math.all(cell < m_Size) && m_Explored[cell.y * m_Size.x + cell.x];

        void Update()
        {
            var state = m_Game != null ? m_Game.State : null;
            if (state == null || state.Flow == RpgFlow.Menu || Time.unscaledTime < m_Next) return;
            m_Next = Time.unscaledTime + Refresh;
            var session = m_Game.Session;
            session.Sync();
            var world = session.World;
            var map = world.Resource(RpgKeys.Map);
            if (m_Texture == null || math.any(m_Size != map.Size)) Allocate(map.Size);
            if (state.FloorBuilds != m_FloorBuilds)
            {
                m_FloorBuilds = state.FloorBuilds;
                System.Array.Clear(m_Explored, 0, m_Explored.Length);
                ExploredCount = 0;
            }
            var view = map.AsView();
            int2 heroCell = new int2(-100);
            if (world.Registry.TryResolve(state.Hero, out _, out int heroRow))
            {
                heroCell = view.CellOf(world.Column(RpgKeys.Position)[heroRow]);
                Reveal(view, heroCell);
            }

            for (int i = 0; i < m_Pixels.Length; i++)
                m_Pixels[i] = !m_Explored[i] ? Fog : map.Tiles[i] != 0 ? WallColor : FloorColor;
            if (IsExplored(state.StairsCell)) Plot(state.StairsCell, StairsColor, 1);

            var props = world.Column(RpgKeys.PropInfo);
            var propPositions = world.Column(RpgKeys.PropPosition);
            for (int i = 0; i < world.Table(RpgKeys.Prop).Count; i++)
            {
                var cell = view.CellOf(propPositions[i]);
                if (props[i].Kind == PropKind.Chest && !props[i].Active && IsExplored(cell)) Plot(cell, ChestColor, 0);
            }
            // Monsters only near the hero (what the hero can currently sense).
            var infos = world.Column(RpgKeys.Info);
            var positions = world.Column(RpgKeys.Position);
            for (int i = 0; i < world.Table(RpgKeys.Actor).Count; i++)
            {
                var info = infos[i];
                if (info.Team != Team.Monsters || info.Has(ActorFlags.Dead)) continue;
                var cell = view.CellOf(positions[i]);
                if (math.lengthsq(cell - heroCell) > RevealRadius * RevealRadius) continue;
                Plot(cell, info.Has(ActorFlags.Elite) || info.Has(ActorFlags.Boss) ? EliteColor : MonsterColor, 0);
            }
            Plot(heroCell, HeroColor, 1);
            m_Texture.SetPixels32(m_Pixels);
            m_Texture.Apply(false);
        }

        void Allocate(int2 size)
        {
            if (m_Texture != null) Destroy(m_Texture);
            m_Size = size;
            m_Texture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            m_Pixels = new Color32[size.x * size.y];
            m_Explored = new bool[size.x * size.y];
            Image.texture = m_Texture;
            m_FloorBuilds = -1;
        }

        /// <summary>Marks tiles within the reveal radius that the hero can see (walls included, nothing behind them).</summary>
        void Reveal(in TileMapView view, int2 center)
        {
            for (int y = -RevealRadius; y <= RevealRadius; y++)
            for (int x = -RevealRadius; x <= RevealRadius; x++)
            {
                if (x * x + y * y > RevealRadius * RevealRadius) continue;
                int2 cell = center + new int2(x, y);
                if (!view.InBounds(cell)) continue;
                int index = view.Index(cell);
                if (m_Explored[index]) continue;
                // A tile is seen when the line to it is clear up to the tile itself (so wall faces show).
                float2 from = view.CenterOf(center), to = view.CenterOf(cell);
                float2 near = to - math.normalizesafe(to - from) * (view.TileSize * 0.75f);
                if (math.lengthsq(to - from) > view.TileSize * view.TileSize && !view.LineOfSight(from, near)) continue;
                m_Explored[index] = true;
                ExploredCount++;
            }
        }

        void Plot(int2 cell, Color32 color, int radius)
        {
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                int2 c = cell + new int2(x, y);
                if (math.any(c < 0) || math.any(c >= m_Size)) continue;
                m_Pixels[c.y * m_Size.x + c.x] = color;
            }
        }

        void OnDestroy()
        {
            if (m_Texture != null) Destroy(m_Texture);
        }
    }
}
