using SPF.Presentation;
using SPF.Presentation.Animation;
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
        Texture2D m_Normals;
        readonly float2[] m_Torches = new float2[32];
        int m_TorchCount;
        SimSession m_Session;
        int m_Built = -1;
        float m_Look;
        SpriteLocomotionClock m_HeroClock;
        float m_Time;

        public RenderTier Tier => m_Assets?.Tier ?? RenderTier.DataTexture;
        public int TileSprites => m_Tiles?.Count ?? 0;
        public int SpritesDrawn { get; private set; }
        /// <summary>Times the static tile batch was uploaded (once per level build).</summary>
        public int TileUploads { get; private set; }
        /// <summary>The current level is drawn lit (night).</summary>
        public bool Night { get; private set; }
        public int LightsUsed { get; private set; }
        public int Torches => m_TorchCount;
        public GameplayLocomotionState HeroLocomotion => m_HeroClock.State;
        public float HeroStridePhase => m_HeroClock.Phase;
        public float AnimationTime => m_Time;
        public int HeroFrame { get; private set; }

        void OnDestroy() => Release();

        void Release()
        {
            m_Tiles?.Dispose(); m_Dynamic?.Dispose(); m_Effects?.Dispose();
            m_Tiles = m_Dynamic = m_Effects = null;
            m_Art?.Dispose();
            m_Art = null;
            if (m_Normals != null) SPF.Presentation.RenderObjects.Destroy(m_Normals);
            m_Normals = null;
            m_Assets?.Dispose();
            m_Assets = null;
        }

        void Bind(SimSession session)
        {
            Release();
            m_Session = session;
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = PlArt.Build();
            m_Normals = NormalMapBaker.Bake(m_Art.Sheet);
            var atlas = m_Art.Sheet.Texture;
            var size = PlModule.MapSize;
            m_Tiles = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, size.x * size.y, queueOffset: -10);
            m_Dynamic = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, 1024);
            m_Effects = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Translucent, 512);
            // Preallocate dynamic pages and prefix textures at session bind, before counts grow in play.
            m_Dynamic.Warmup(m_Dynamic.Capacity);
            m_Effects.Warmup(m_Effects.Capacity);
            m_Fx = new SpriteEffects(128);
            m_Built = -1;
            m_HeroClock = default;
            HeroFrame = m_Art.HeroIdle.First;
            m_Time = 0f;
            m_Look = 0f;
        }

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var world = session.World;
            var game = world.Resource(PlKeys.Game);
            float alpha = session.InterpolationAlpha;
            float dt = session.State == SessionState.Running ? Time.deltaTime : 0f;
            m_Time += dt;
            float time = m_Time;
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));
            if (Camera != null && Camera.UpdateTarget == null) Camera.UpdateTarget = AimCamera;

            bool rebuild = game.LevelBuilds != m_Built;
            if (rebuild)
            {
                BuildTiles(world);
                SetupNight(game);
                m_Built = game.LevelBuilds;
                TileUploads++;
                m_Fx.Clear();
                m_HeroClock = default;
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

                var m = game.Motor;
                m_HeroClock.Advance(dt, math.abs(m.Velocity.x), game.Tuning.RunSpeed,
                    m.Grounded || game.Riding >= 0, game.Flow != PlFlow.Dying);
                if (game.Flow != PlFlow.Dying || ((int)(time * 12f) & 1) == 0)
                {
                    float2 hero = math.lerp(game.HeroPrev, game.Hero, alpha);
                    int frame = !m.Grounded && game.Riding < 0
                        ? (m.Velocity.y > 0f ? m_Art.HeroJump : m_Art.HeroFall)
                        : m_HeroClock.State == GameplayLocomotionState.Run ? m_HeroClock.Frame(m_Art.HeroRun)
                        : m_HeroClock.State == GameplayLocomotionState.Walk ? m_HeroClock.Frame(m_Art.HeroWalk)
                        : m_Art.HeroIdle.FrameAt(m_HeroClock.Time);
                    HeroFrame = frame;
                    m_Dynamic.Add(hero + new float2(0f, 0.12f), new float2(game.Facing, 1.125f), m_Art.Sheet[frame].Uv, ActorDepth - 0.1f, new float4(1f));
                }
            }
            if (Night && game.Flow != PlFlow.Menu) Light(game, alpha, time);
            m_Fx.UpdateAndDraw(dt, m_Effects, m_Art.Sheet, null);

            m_Tiles.Draw(bounds, dirty: rebuild);
            m_Dynamic.Draw(bounds);
            m_Effects.Draw(bounds);
            SpritesDrawn = m_Tiles.Count + m_Dynamic.Count + m_Effects.Count;
        }

        void SetupNight(PlGameState game)
        {
            int level = math.clamp(game.Level, 0, PlLevels.Count - 1);
            Night = PlLevels.IsNight(level);
            m_Tiles.SetLighting(Night ? m_Normals : null);
            m_Dynamic.SetLighting(Night ? m_Normals : null);
            if (Camera != null && Camera.Camera != null)
                Camera.Camera.backgroundColor = Night ? new Color(0.05f, 0.06f, 0.14f) : new Color(0.45f, 0.7f, 0.95f);
            m_TorchCount = 0;
            foreach (var marker in PlLevels.Asset(level).Markers)
                if (marker.Symbol == 't' && m_TorchCount < m_Torches.Length)
                    m_Torches[m_TorchCount++] = new float2(marker.Cell.x + 0.5f, marker.Cell.y + 0.5f);
        }

        /// <summary>Night lighting: the hero's lantern plus the torches nearest the camera (the shader takes eight).</summary>
        void Light(PlGameState game, float alpha, float time)
        {
            SpriteLighting.Begin(new Color(0.16f, 0.18f, 0.32f));
            float2 hero = math.lerp(game.HeroPrev, game.Hero, alpha);
            SpriteLighting.Add(hero + new float2(game.Facing * 0.3f, 0.4f), 6.5f, new Color(1f, 0.9f, 0.7f), 1.1f, 1.5f);
            float2 view = Camera != null ? Camera.Target : hero;
            for (int pick = 0; pick < SpriteLighting.MaxLights - 1; pick++)
            {
                // Selection by distance without sorting or allocating: take the nearest not yet taken.
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < m_TorchCount; i++)
                {
                    float d = math.distancesq(m_Torches[i], view);
                    if (d < bestD && (pick == 0 || d > m_LastPicked || d == m_LastPicked && i > m_LastIndex)) { bestD = d; best = i; }
                }
                if (best < 0 || bestD > 30f * 30f) break;
                m_LastPicked = bestD;
                m_LastIndex = best;
                float flicker = 0.9f + 0.1f * math.sin(time * 13f + best * 2.1f) + 0.05f * math.sin(time * 29f + best);
                SpriteLighting.Add(m_Torches[best] + new float2(0f, 0.4f), 5f, new Color(1f, 0.6f, 0.25f), 1.3f * flicker, 1.1f);
            }
            LightsUsed = SpriteLighting.Count;
            SpriteLighting.Apply();
            for (int i = 0; i < m_TorchCount; i++)
            {
                m_Dynamic.Add(m_Torches[i], new float2(0.5f, 1f), m_Art.Sheet[m_Art.Torch.FrameAt(time + i * 0.17f)].Uv, PropDepth + 0.1f, new float4(1.6f, 1.6f, 1.6f, 1f));
                m_Effects.Add(m_Torches[i] + new float2(0f, 0.35f), new float2(1.6f), m_Art.Sheet[m_Art.Glow].Uv, FxDepth, new float4(1f, 0.6f, 0.25f, 0.35f));
            }
        }

        float m_LastPicked;
        int m_LastIndex;

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
            return PlLevels.Asset(math.clamp(game.Level, 0, PlLevels.Count - 1)).Width;
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
