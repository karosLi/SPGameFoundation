using SPF.L1.Spatial;
using SPF.Presentation;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using Unity.Mathematics;
using UnityEngine;

namespace RpgFoundation.Presentation
{
    /// <summary>
    /// Draws the dungeon with the foundation's batches: the tile map as one quad mesh (rebuilt only when
    /// the floor changes), actors / items / projectiles as disc batches (both render tiers), health bars as
    /// overlay quads. Interpolates between ticks, drives the follow camera, and forwards feedback events
    /// (damage numbers...) to <see cref="Feedback"/>. Runs after the camera (order 500).
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class RpgWorldRenderer : MonoBehaviour
    {
        const float FloorDepth = 6f, WallDepth = 5f, ItemDepth = 0.9f, ActorDepth = 0.5f, ProjectileDepth = 0.2f;

        public SessionHost Host;
        public FollowCamera2D Camera;
        public float ViewSize = 9f;

        /// <summary>Feedback events drained this frame (damage numbers, level-up toasts...).</summary>
        public System.Action<FeedbackEvent> Feedback;

        RenderAssets m_Assets;
        CircleBatch m_Actors, m_Items, m_Glow;
        QuadBatch m_Tiles, m_Bars;
        int m_TileBuild = -1;
        uint m_TileVersion = uint.MaxValue;
        SimSession m_Session;

        public RenderTier Tier => m_Assets?.Tier ?? RenderTier.DataTexture;
        public int LastActorsDrawn { get; private set; }
        public int TileQuads => m_Tiles?.Count ?? 0;

        void OnDestroy() => Release();

        void Release()
        {
            m_Actors?.Dispose(); m_Items?.Dispose(); m_Glow?.Dispose();
            m_Tiles?.Dispose(); m_Bars?.Dispose();
            m_Actors = m_Items = m_Glow = null;
            m_Tiles = m_Bars = null;
            m_Assets?.Dispose();
            m_Assets = null;
        }

        void Bind(SimSession session)
        {
            Release();
            m_Session = session;
            var world = session.World;
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            int actors = world.Table(RpgKeys.Actor).Capacity;
            m_Actors = new CircleBatch(m_Assets, BlendKind.Opaque, actors * 3);
            m_Items = new CircleBatch(m_Assets, BlendKind.Opaque, world.Table(RpgKeys.Item).Capacity * 2);
            m_Glow = new CircleBatch(m_Assets, BlendKind.Additive, world.Table(RpgKeys.Projectile).Capacity * 2 + 8, shaded: false);
            var map = world.Resource(RpgKeys.Map);
            m_Tiles = new QuadBatch(map.Size.x * map.Size.y, overlay: false);
            m_Bars = new QuadBatch(actors * 2, overlay: true);
            m_TileBuild = -1;
        }

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var world = session.World;
            var game = world.Resource(RpgKeys.Game);
            var map = world.Resource(RpgKeys.Map);
            float alpha = session.InterpolationAlpha;
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));

            if (game.FloorBuilds != m_TileBuild || map.Version != m_TileVersion)
            {
                BuildTiles(map, game);
                m_TileBuild = game.FloorBuilds;
                m_TileVersion = map.Version;
            }

            var positions = world.Column(RpgKeys.Position);
            var prev = world.Column(RpgKeys.PrevPosition);
            var infos = world.Column(RpgKeys.Info);
            var healths = world.Column(RpgKeys.Health);
            var facings = world.Column(RpgKeys.Facing);
            var combat = world.Column(RpgKeys.Combat);
            var config = world.Resource(RpgKeys.Config);
            int count = world.Table(RpgKeys.Actor).Count;

            if (Camera != null && Camera.UpdateTarget == null)
                Camera.UpdateTarget = AimCamera;
            float4 view = Camera != null ? Camera.ViewRect : new float4(-1e5f, -1e5f, 1e5f, 1e5f);
            view += new float4(-2f, -2f, 2f, 2f);

            m_Actors.Count = 0;
            m_Bars.Clear();
            int drawn = 0;
            for (int i = 0; i < count; i++)
            {
                var info = infos[i];
                if (info.Has(ActorFlags.Dead)) continue;
                float2 p = math.lerp(prev[i], positions[i], alpha);
                if (p.x < view.x || p.y < view.y || p.x > view.z || p.y > view.w) continue;
                drawn++;
                bool hero = info.Has(ActorFlags.Hero);
                float4 color = hero ? new float4(0.3f, 0.6f, 1f, 1f) : config.Monsters[info.Kind - 1].Color;
                if (combat[i].HitFlash > 0f) color = math.lerp(color, new float4(1f, 1f, 1f, 1f), 0.7f);
                float depth = ActorDepth - i * 1e-4f;
                if (info.Has(ActorFlags.Boss))
                    m_Actors.Add(p, info.Radius * 1.15f, depth + 0.05f, new float4(0.25f, 0.1f, 0.35f, 1f));
                m_Actors.Add(p, info.Radius, depth, color);
                float2 eye = p + math.normalizesafe(facings[i], new float2(0f, -1f)) * info.Radius * 0.55f;
                m_Actors.Add(eye, info.Radius * 0.28f, depth - 0.01f, hero ? new float4(1f, 0.9f, 0.4f, 1f) : new float4(0.1f, 0.05f, 0.05f, 1f));

                var h = healths[i];
                if (hero || h.Current < h.Max)
                {
                    float w = math.max(info.Radius * 2f, 0.8f);
                    float2 min = p + new float2(-w * 0.5f, info.Radius + 0.15f);
                    m_Bars.Add(min, new float2(w, 0.12f), 0f, new Color32(20, 20, 20, 200));
                    var fill = hero ? new Color32(80, 200, 255, 230) : new Color32(230, 60, 50, 230);
                    m_Bars.Add(min, new float2(w * h.Fraction, 0.12f), -0.01f, fill);
                }
            }
            LastActorsDrawn = drawn;

            // Items: gold coins, potions, gear (pulsing).
            m_Items.Count = 0;
            var itemPos = world.Column(RpgKeys.ItemPosition);
            var itemInfo = world.Column(RpgKeys.ItemInfo);
            float pulse = 0.85f + 0.15f * math.sin(Time.time * 6f);
            for (int i = 0; i < world.Table(RpgKeys.Item).Count; i++)
            {
                var item = itemInfo[i];
                float2 p = itemPos[i];
                float4 c = item.Kind == ItemKind.Gold ? new float4(1f, 0.82f, 0.2f, 1f)
                    : item.Kind == ItemKind.Potion ? new float4(0.95f, 0.25f, 0.35f, 1f)
                    : new float4(0.75f, 0.45f, 1f, 1f);
                m_Items.Add(p, item.Radius * (item.Kind == ItemKind.Gear ? pulse : 1f), ItemDepth - i * 1e-5f, c);
            }

            // Projectiles and the stairs glow.
            m_Glow.Count = 0;
            var projPos = world.Column(RpgKeys.ProjectilePosition);
            var projPrev = world.Column(RpgKeys.ProjectilePrev);
            var projInfo = world.Column(RpgKeys.ProjectileInfo);
            for (int i = 0; i < world.Table(RpgKeys.Projectile).Count; i++)
            {
                var pr = projInfo[i];
                float2 p = math.lerp(projPrev[i], projPos[i], alpha);
                if (pr.Visual == ProjectileVisual.Fireball)
                {
                    m_Glow.Add(p, pr.Radius * 2.2f, ProjectileDepth, new float4(1f, 0.35f, 0.05f, 0.35f));
                    m_Glow.Add(p, pr.Radius, ProjectileDepth - 0.01f, new float4(1f, 0.8f, 0.3f, 0.9f));
                }
                else
                    m_Glow.Add(p, pr.Radius, ProjectileDepth, new float4(0.9f, 0.9f, 1f, 0.8f));
            }
            if (game.Flow != RpgFlow.Menu)
            {
                float2 stairs = map.AsView().CenterOf(game.StairsCell);
                float s = 0.5f + 0.5f * math.sin(Time.time * 2.5f);
                var glow = game.BossAlive ? new float4(0.6f, 0.15f, 0.2f, 0.25f) : new float4(0.3f, 0.9f, 1f, 0.2f + 0.2f * s);
                m_Glow.Add(stairs, 0.9f + 0.15f * s, ProjectileDepth + 0.3f, glow);
            }

            m_Tiles.Draw(bounds, dirty: false);
            m_Items.Draw(bounds);
            m_Actors.Draw(bounds);
            m_Glow.Draw(bounds);
            m_Bars.Draw(bounds);

            var feedback = world.Resource(RpgKeys.Feedback);
            if (Feedback != null)
                for (int i = 0; i < feedback.Count; i++) Feedback(feedback[i]);
            feedback.Clear();
        }

        /// <summary>Camera callback (runs before this renderer): follow the interpolated hero.</summary>
        void AimCamera(FollowCamera2D camera)
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            var world = session.World;
            var game = world.Resource(RpgKeys.Game);
            camera.Size = ViewSize;
            if (world.Registry.TryResolve(game.Hero, out _, out int row))
                camera.Target = math.lerp(world.Column(RpgKeys.PrevPosition)[row], world.Column(RpgKeys.Position)[row], session.InterpolationAlpha);
            else if (game.Flow == RpgFlow.Menu)
            {
                var map = world.Resource(RpgKeys.Map);
                camera.Target = map.AsView().CenterOf(map.Size / 2);
            }
        }

        void BuildTiles(TileMap map, RpgGameState game)
        {
            m_Tiles.Clear();
            var view = map.AsView();
            float t = map.TileSize;
            for (int y = 0; y < map.Size.y; y++)
            for (int x = 0; x < map.Size.x; x++)
            {
                var cell = new int2(x, y);
                bool solid = view.IsSolid(cell);
                if (solid && !TouchesFloor(view, cell)) continue;   // buried rock is never visible
                float2 min = map.Origin + (float2)cell * t;
                Color32 c;
                if (solid) c = new Color32(70, 64, 78, 255);
                else if (game.Flow != RpgFlow.Menu && math.all(cell == game.StairsCell)) c = new Color32(40, 120, 140, 255);
                else c = ((x + y) & 1) == 0 ? new Color32(34, 32, 40, 255) : new Color32(38, 36, 45, 255);
                m_Tiles.Add(min, new float2(t, t), solid ? WallDepth : FloorDepth, c);
            }
        }

        static bool TouchesFloor(in TileMapView view, int2 cell)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (view.InBounds(cell + new int2(dx, dy)) && !view.IsSolid(cell + new int2(dx, dy))) return true;
            return false;
        }
    }
}
