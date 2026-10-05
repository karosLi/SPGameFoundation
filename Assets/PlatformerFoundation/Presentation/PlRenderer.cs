using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using Unity.Mathematics;
using UnityEngine;

namespace PlatformerFoundation.Presentation
{
    /// <summary>
    /// Draws the platformer: the tile layers go into a static batch uploaded only when a level is built
    /// (<c>Draw(dirty: false)</c> otherwise), the few moving things into a per-frame batch. The camera
    /// gets a dead zone, the level bounds and a look-ahead in the facing direction.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class PlRenderer : MonoBehaviour
    {
        const float TileDepth = 5f, PropDepth = 3f, ActorDepth = 2f, FxDepth = 1f;

        public SessionHost Host;
        public FollowCamera2D Camera;
        public System.Action<PlFeedback> Feedback;

        RenderAssets m_Assets;
        PlArt m_Art;
        SpriteBatch m_Tiles, m_Dynamic, m_Effects;
        SpriteEffects m_Fx;
        SimSession m_Session;
        int m_Built = -1;
        float m_Look;

        public RenderTier Tier => m_Assets?.Tier ?? RenderTier.DataTexture;
        public int TileSprites => m_Tiles?.Count ?? 0;
        public int SpritesDrawn { get; private set; }
        /// <summary>Times the static tile batch was uploaded (once per level build).</summary>
        public int TileUploads { get; private set; }

        void OnDestroy() => Release();

        void Release()
        {
            m_Tiles?.Dispose(); m_Dynamic?.Dispose(); m_Effects?.Dispose();
            m_Tiles = m_Dynamic = m_Effects = null;
            m_Art?.Dispose();
            m_Art = null;
            m_Assets?.Dispose();
            m_Assets = null;
        }

        void Bind(SimSession session)
        {
            Release();
            m_Session = session;
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = PlArt.Build();
            var atlas = m_Art.Sheet.Texture;
            var size = PlModule.MapSize;
            m_Tiles = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, size.x * size.y, queueOffset: -10);
            m_Dynamic = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, 1024);
            m_Effects = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Translucent, 512);
            m_Fx = new SpriteEffects(128);
            m_Built = -1;
        }

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var world = session.World;
            var game = world.Resource(PlKeys.Game);
            float alpha = session.InterpolationAlpha;
            float time = Time.time;
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));
            if (Camera != null && Camera.UpdateTarget == null) Camera.UpdateTarget = AimCamera;

            bool rebuild = game.LevelBuilds != m_Built;
            if (rebuild)
            {
                BuildTiles(world);
                m_Built = game.LevelBuilds;
                TileUploads++;
                m_Fx.Clear();
                Camera?.Snap();
            }

            m_Dynamic.Clear();
            m_Effects.Clear();
            DrainFeedback(world);
            if (game.Flow != PlFlow.Menu)
            {
                var coins = world.Column(PlKeys.CoinPosition);
                var dead = world.Table(PlKeys.Coin).DeadFlags;
                for (int i = 0; i < world.Table(PlKeys.Coin).Count; i++)
                    if (dead[i] == 0)
                        m_Dynamic.Add(coins[i], new float2(10f) / PlArt.PixelsPerUnit, m_Art.Sheet[m_Art.Coin.FrameAt(time + i * 0.1f)].Uv, PropDepth, new float4(1f));

                var platPos = world.Column(PlKeys.PlatformPosition);
                var platPrev = world.Column(PlKeys.PlatformPrev);
                for (int i = 0; i < world.Table(PlKeys.Platform).Count; i++)
                    m_Dynamic.Add(math.lerp(platPrev[i], platPos[i], alpha), new float2(3f, 0.5f), m_Art.Sheet[m_Art.Platform].Uv, PropDepth, new float4(1f));

                var wPos = world.Column(PlKeys.WalkerPosition);
                var wPrev = world.Column(PlKeys.WalkerPrev);
                var wInfo = world.Column(PlKeys.WalkerInfo);
                for (int i = 0; i < world.Table(PlKeys.Walker).Count; i++)
                {
                    var w = wInfo[i];
                    float2 p = math.lerp(wPrev[i], wPos[i], alpha);
                    if (w.Dead)
                        m_Dynamic.Add(p + new float2(0f, -0.2f), new float2(1f, 0.375f), m_Art.Sheet[m_Art.WalkerSquashed].Uv, ActorDepth, new float4(1f, 1f, 1f, 1f));
                    else
                        m_Dynamic.Add(p + new float2(0f, 0.04f), new float2(-w.Dir * 1f, 0.875f), m_Art.Sheet[m_Art.Walker.FrameAt(time)].Uv, ActorDepth, new float4(1f));
                }

                m_Dynamic.Add(game.GoalPosition + new float2(0f, 0.5f), new float2(1f, 2f), m_Art.Sheet[m_Art.Flag.FrameAt(time)].Uv, PropDepth, new float4(1f));

                if (game.Flow != PlFlow.Dying || ((int)(time * 12f) & 1) == 0)
                {
                    float2 hero = math.lerp(game.HeroPrev, game.Hero, alpha);
                    var m = game.Motor;
                    int frame = !m.Grounded && game.Riding < 0 ? (m.Velocity.y > 0f ? m_Art.HeroJump : m_Art.HeroFall)
                        : math.abs(m.Velocity.x) > 0.5f ? m_Art.HeroRun.FrameAt(time) : m_Art.HeroIdle.FrameAt(time);
                    m_Dynamic.Add(hero + new float2(0f, 0.12f), new float2(game.Facing, 1.125f), m_Art.Sheet[frame].Uv, ActorDepth - 0.1f, new float4(1f));
                }
            }
            m_Fx.UpdateAndDraw(Time.deltaTime, m_Effects, m_Art.Sheet, null);

            m_Tiles.Draw(bounds, dirty: rebuild);
            m_Dynamic.Draw(bounds);
            m_Effects.Draw(bounds);
            SpritesDrawn = m_Tiles.Count + m_Dynamic.Count + m_Effects.Count;
        }

        void AimCamera(FollowCamera2D camera)
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            var game = session.World.Resource(PlKeys.Game);
            float2 hero = math.lerp(game.HeroPrev, game.Hero, session.InterpolationAlpha);
            // Look ahead where the hero runs; the dead zone keeps small hops from moving the view.
            m_Look = math.lerp(m_Look, game.Facing * 2.5f, 1f - math.exp(-3f * Time.deltaTime));
            camera.Target = hero + new float2(m_Look, 1f);
            camera.DeadZone = new float2(1f, 1.5f);
            var size = PlModule.MapSize;
            camera.Bounds = new float4(0f, -2f, LevelWidth(game), size.y);
        }

        static float LevelWidth(PlGameState game)
        {
            int width = 0;
            foreach (var row in PlLevels.All[math.clamp(game.Level, 0, PlLevels.All.Length - 1)]) width = math.max(width, row.Length);
            return width;
        }

        void BuildTiles(SPF.Runtime.World.SimWorld world)
        {
            m_Tiles.Clear();
            var map = world.Resource(PlKeys.Map);
            var hazards = world.Resource(PlKeys.Hazards);
            var size = map.Size;
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    var cell = new int2(x, y);
                    float2 centre = new float2(x + 0.5f, y + 0.5f);
                    byte t = map[cell];
                    if (t == PlTile.Solid)
                    {
                        bool top = y + 1 >= size.y || map[cell + new int2(0, 1)] == 0;
                        m_Tiles.Add(centre, new float2(1.001f), m_Art.Sheet[top ? m_Art.GroundTop : m_Art.Ground].Uv, TileDepth, new float4(1f));
                    }
                    else if (t == PlTile.OneWay)
                        m_Tiles.Add(centre, new float2(1.001f), m_Art.Sheet[m_Art.Plank].Uv, TileDepth, new float4(1f));
                    if (hazards[cell] == PlTile.Spikes)
                        m_Tiles.Add(centre, new float2(1f), m_Art.Sheet[m_Art.Spikes].Uv, TileDepth - 0.1f, new float4(1f));
                }
        }

        void DrainFeedback(SPF.Runtime.World.SimWorld world)
        {
            var feedback = world.Resource(PlKeys.Feedback);
            for (int i = 0; i < feedback.Count; i++)
            {
                var e = feedback[i];
                Feedback?.Invoke(e);
                switch (e.Kind)
                {
                    case PlFeedbackKind.Coin:
                    case PlFeedbackKind.Stomp:
                    case PlFeedbackKind.Goal:
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Sparkle, Position = e.Position + new float2(0f, 0.3f), Size = new float2(0.8f), Color = new float4(1f), Depth = FxDepth });
                        break;
                    case PlFeedbackKind.Die:
                        Camera?.Shake(0.2f, 0.3f);
                        break;
                }
            }
            feedback.Clear();
        }
    }
}
