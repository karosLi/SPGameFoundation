using SPF.L1.Navigation;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DefenseFoundation.Presentation
{
    /// <summary>
    /// Draws the tower-defense board: tiles in a static batch (re-uploaded only when the map changes), the
    /// current enemy route as an A* path preview (smoothed), towers with turrets turned to their target,
    /// enemies with health bars, shots and effects; highlights the selected tile (green / red buildable).
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class TdRenderer : MonoBehaviour
    {
        const float TileDepth = 6f, PathDepth = 5.5f, TowerDepth = 3f, EnemyDepth = 2f, ShotDepth = 1f, UiDepth = 0.5f;

        public SessionHost Host;
        public FollowCamera2D Camera;
        public System.Action<TdFeedback> Feedback;

        /// <summary>Tile the player selected (-1 = none) and whether a tower could be built there.</summary>
        public int2 Selected = new int2(-1);
        public bool SelectedBuildable;

        RenderAssets m_Assets;
        TdArt m_Art;
        SpriteBatch m_Tiles, m_Dynamic, m_Glow;
        SpriteEffects m_Fx;
        SimSession m_Session;
        uint m_MapVersion = uint.MaxValue;
        NativeArray<int2> m_Path;
        int m_PathLength;
        AStarScratch m_Scratch;

        public RenderTier Tier => m_Assets?.Tier ?? RenderTier.DataTexture;
        public int PathLength => m_PathLength;
        public int SpritesDrawn { get; private set; }
        public int TileUploads { get; private set; }

        void OnDestroy() => Release();

        void Release()
        {
            m_Tiles?.Dispose(); m_Dynamic?.Dispose(); m_Glow?.Dispose();
            m_Tiles = m_Dynamic = m_Glow = null;
            m_Art?.Dispose(); m_Art = null;
            if (m_Path.IsCreated) m_Path.Dispose();
            m_Scratch.Dispose();
            m_Assets?.Dispose(); m_Assets = null;
        }

        void Bind(SimSession session)
        {
            Release();
            m_Session = session;
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = TdArt.Build();
            var atlas = m_Art.Sheet.Texture;
            var size = TdRules.MapSize;
            m_Tiles = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, size.x * size.y * 2, queueOffset: -10);
            m_Dynamic = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, 4096);
            m_Glow = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Translucent, 4096);
            m_Fx = new SpriteEffects(256);
            m_Path = new NativeArray<int2>(size.x * size.y, Allocator.Persistent);
            m_Scratch = new AStarScratch(size.x * size.y, Allocator.Persistent);
            m_MapVersion = uint.MaxValue;
        }

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var world = session.World;
            var game = world.Resource(TdKeys.Game);
            var rules = world.Resource(TdKeys.Rules);
            var map = world.Resource(TdKeys.Map);
            var view = map.AsView();
            float alpha = session.InterpolationAlpha, time = Time.time;
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));

            bool rebuild = map.Version != m_MapVersion;
            if (rebuild)
            {
                m_MapVersion = map.Version;
                TileUploads++;
                m_Tiles.Clear();
                for (int y = 0; y < map.Size.y; y++)
                    for (int x = 0; x < map.Size.x; x++)
                    {
                        var cell = new int2(x, y);
                        byte t = map[cell];
                        m_Tiles.Add(view.CenterOf(cell), new float2(1.001f), m_Art.Sheet[(x + y) % 2 == 0 ? m_Art.Grass : m_Art.Grass2].Uv, TileDepth, new float4(1f));
                        if (t == TdTile.Rock) m_Tiles.Add(view.CenterOf(cell), new float2(1f), m_Art.Sheet[m_Art.Rock].Uv, TileDepth - 0.1f, new float4(1f));
                    }
                m_Tiles.Add(view.CenterOf(rules.Spawn), new float2(1f), m_Art.Sheet[m_Art.Portal].Uv, TileDepth - 0.1f, new float4(1f));
                m_Tiles.Add(view.CenterOf(rules.Base), new float2(1.2f), m_Art.Sheet[m_Art.Castle].Uv, TileDepth - 0.1f, new float4(1f));
                // Route preview: A* from the spawn to the base on the current map, smoothed.
                m_PathLength = GridAStar.FindPath(view, rules.Spawn, rules.Base, m_Path, ref m_Scratch, diagonal: true);
                m_PathLength = GridAStar.Smooth(view, m_Path, m_PathLength);
            }

            m_Dynamic.Clear();
            m_Glow.Clear();
            DrainFeedback(world);
            if (game.Flow != TdFlow.Menu)
            {
                // Route: dots along the smoothed path.
                for (int i = 1; i < m_PathLength; i++)
                {
                    float2 a = view.CenterOf(m_Path[i - 1]), b = view.CenterOf(m_Path[i]);
                    int dots = (int)math.ceil(math.distance(a, b) / 0.5f);
                    for (int k = 0; k < dots; k++)
                    {
                        float phase = math.frac(time * 0.8f - (i * 10 + k) * 0.05f);
                        m_Glow.Add(math.lerp(a, b, k / (float)dots), new float2(0.18f), m_Art.Sheet[m_Art.Dot].Uv, PathDepth, new float4(1f, 0.9f, 0.5f, 0.25f + 0.35f * phase));
                    }
                }
                var towers = world.Column(TdKeys.TowerInfo);
                for (int i = 0; i < world.Table(TdKeys.Tower).Count; i++)
                {
                    var t = towers[i];
                    float2 c = view.CenterOf(t.Cell);
                    m_Dynamic.Add(c, new float2(0.95f), m_Art.Sheet[m_Art.Base].Uv, TowerDepth, new float4(1f));
                    m_Dynamic.Add(c, new float2(0.8f), m_Art.Sheet[m_Art.Turrets[(int)t.Kind]].Uv, TowerDepth - 0.1f, new float4(1f), t.Aim);
                    for (int l = 1; l < t.Level; l++)
                        m_Dynamic.Add(c + new float2(-0.3f + l * 0.18f, -0.35f), new float2(0.16f), m_Art.Sheet[m_Art.Dot].Uv, TowerDepth - 0.2f, new float4(1f, 0.85f, 0.2f, 1f));
                }
                var positions = world.Column(TdKeys.Position);
                var prev = world.Column(TdKeys.PrevPosition);
                var infos = world.Column(TdKeys.Info);
                for (int i = 0; i < world.Table(TdKeys.Enemy).Count; i++)
                {
                    var e = infos[i];
                    float2 p = math.lerp(prev[i], positions[i], alpha);
                    float size = e.Kind == 2 ? 0.75f : e.Kind == 3 ? 0.4f : 0.55f;
                    float4 tint = e.Slow > 0f ? new float4(0.6f, 0.85f, 1.4f, 1f) : new float4(1f);
                    m_Dynamic.Add(p, new float2(size), m_Art.Sheet[m_Art.Enemies[e.Kind - 1].FrameAt(time + i * 0.13f)].Uv, EnemyDepth, tint);
                    float hp = math.saturate(e.Hp / math.max(e.MaxHp, 1f));
                    if (hp < 1f)
                    {
                        m_Dynamic.Add(p + new float2(0f, size * 0.6f), new float2(0.6f, 0.08f), m_Art.Sheet[m_Art.Dot].Uv, EnemyDepth - 0.1f, new float4(0.15f, 0.15f, 0.15f, 1f));
                        m_Dynamic.Add(p + new float2(-0.3f + 0.3f * hp, size * 0.6f), new float2(0.6f * hp, 0.08f), m_Art.Sheet[m_Art.Dot].Uv, EnemyDepth - 0.15f, new float4(1.2f, 0.3f, 0.3f, 1f));
                    }
                }
                var shots = world.Column(TdKeys.ShotPosition);
                var shotInfo = world.Column(TdKeys.ShotInfo);
                var dead = world.Table(TdKeys.Shot).DeadFlags;
                for (int i = 0; i < world.Table(TdKeys.Shot).Count; i++)
                    if (dead[i] == 0)
                    {
                        var s = shotInfo[i];
                        m_Glow.Add(shots[i], new float2(s.Kind == TowerKind.Cannon ? 0.3f : 0.2f), m_Art.Sheet[m_Art.Dot].Uv, ShotDepth,
                            s.Kind == TowerKind.Frost ? new float4(0.6f, 0.9f, 1f, 1f) : s.Kind == TowerKind.Cannon ? new float4(0.3f, 0.3f, 0.3f, 1f) : new float4(1f, 0.95f, 0.6f, 1f));
                    }
                if (view.InBounds(Selected))
                {
                    m_Glow.Add(view.CenterOf(Selected), new float2(1f), m_Art.Sheet[m_Art.Frame].Uv, UiDepth, SelectedBuildable ? new float4(0.4f, 1f, 0.4f, 0.9f) : new float4(1f, 0.4f, 0.3f, 0.9f));
                    for (int i = 0; i < world.Table(TdKeys.Tower).Count; i++)
                        if (math.all(towers[i].Cell == Selected))
                        {
                            float range = rules.Tower(towers[i].Kind, towers[i].Level).Range;
                            m_Glow.Add(view.CenterOf(Selected), new float2(range * 2f), m_Art.Sheet[m_Art.Ring].Uv, UiDepth, new float4(1f, 1f, 1f, 0.5f));
                        }
                }
            }
            m_Fx.UpdateAndDraw(Time.deltaTime, m_Glow, m_Art.Sheet, null);
            m_Tiles.Draw(bounds, dirty: rebuild);
            m_Dynamic.Draw(bounds);
            m_Glow.Draw(bounds);
            SpritesDrawn = m_Tiles.Count + m_Dynamic.Count + m_Glow.Count;
        }

        void DrainFeedback(SPF.Runtime.World.SimWorld world)
        {
            var feedback = world.Resource(TdKeys.Feedback);
            for (int i = 0; i < feedback.Count; i++)
            {
                var e = feedback[i];
                Feedback?.Invoke(e);
                if (e.Kind == TdFeedbackKind.Death)
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position, Size = new float2(0.8f), Color = new float4(1f, 1f, 1f, 0.8f), Depth = ShotDepth });
                else if (e.Kind == TdFeedbackKind.Built)
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position, Size = new float2(1.2f), Color = new float4(0.9f, 0.8f, 0.6f, 0.8f), Depth = ShotDepth });
            }
            feedback.Clear();
        }
    }
}
