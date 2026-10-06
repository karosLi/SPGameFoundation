using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using SPF.Shell.CameraRig;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SurvivorFoundation.Presentation
{
    /// <summary>
    /// Draws the survivor world. The bulk (enemies, bullets, gems: tens of thousands) is culled and written
    /// straight into reserved 32-byte packed sprite slots by Burst jobs (<see cref="SpriteBatch.Reserve"/>),
    /// so the main thread does no per-sprite work; the hero, blades, ground and effects are added directly.
    /// Bullets go to an additive batch (glow, no depth write), everything else to cut-out opaque batches.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class SvRenderer : MonoBehaviour
    {
        const float GroundDepth = 9f, GemDepth = 4f, ActorDepth = 2f, DepthPerY = 0.0005f, BulletDepth = 0.5f;
        const float GroundTile = 2f;

        public SessionHost Host;
        public FollowCamera2D Camera;
        public System.Action<SvFeedback> Feedback;

        RenderAssets m_Assets;
        SvArt m_Art;
        SpriteBatch m_Ground, m_Opaque, m_Additive, m_Effects;
        SpriteEffects m_Fx;
        SimSession m_Session;
        NativeArray<float4> m_EnemyColors;
        NativeArray<int> m_Counts;

        public RenderTier Tier => m_Assets?.Tier ?? RenderTier.DataTexture;
        public int SpritesDrawn { get; private set; }
        public int BulletsDrawn { get; private set; }
        public int EnemiesDrawn { get; private set; }
        /// <summary>Instance-data API payload last frame (all batches), including texture padding and indirect arguments.</summary>
        public long BytesUploaded { get; private set; }
        public SvArt Art => m_Art;

        void OnDestroy() => Release();

        void Release()
        {
            m_Ground?.Dispose(); m_Opaque?.Dispose(); m_Additive?.Dispose(); m_Effects?.Dispose();
            m_Ground = m_Opaque = m_Additive = m_Effects = null;
            m_Art?.Dispose();
            m_Art = null;
            if (m_EnemyColors.IsCreated) m_EnemyColors.Dispose();
            if (m_Counts.IsCreated) m_Counts.Dispose();
            m_Assets?.Dispose();
            m_Assets = null;
        }

        void Bind(SimSession session)
        {
            Release();
            m_Session = session;
            var world = session.World;
            var config = world.Resource(SvKeys.Config);
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = SvArt.Build(config.EnemyKinds, k => { var c = config.Enemies[k].Color; return new Color(c.x, c.y, c.z, 1f); });
            var tier = m_Assets.Tier;
            var atlas = m_Art.Sheet.Texture;
            m_Ground = new SpriteBatch(tier, atlas, BlendKind.Opaque, 1024, queueOffset: -10);
            m_Opaque = new SpriteBatch(tier, atlas, BlendKind.Opaque, world.Table(SvKeys.Enemy).Capacity + world.Table(SvKeys.Gem).Capacity + 64);
            m_Additive = new SpriteBatch(tier, atlas, BlendKind.Additive, world.Table(SvKeys.Bullet).Capacity + 256);
            m_Effects = new SpriteBatch(tier, atlas, BlendKind.Translucent, 2048);
            // Preallocate dynamic pages and prefix textures at session bind, before counts grow in play.
            m_Ground.Warmup(m_Ground.Capacity);
            m_Opaque.Warmup(m_Opaque.Capacity);
            m_Additive.Warmup(m_Additive.Capacity);
            m_Effects.Warmup(m_Effects.Capacity);
            m_Fx = new SpriteEffects(512);
            m_EnemyColors = new NativeArray<float4>(math.max(config.EnemyKinds, 1), Allocator.Persistent);
            for (int k = 0; k < config.EnemyKinds; k++) m_EnemyColors[k] = new float4(1f);
            m_Counts = new NativeArray<int>(1, Allocator.Persistent);
        }

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var world = session.World;
            var game = world.Resource(SvKeys.Game);
            float alpha = session.InterpolationAlpha;
            float dt = Time.deltaTime;
            float time = Time.time;
            float2 hero = math.lerp(game.HeroPrev, game.Hero, alpha);
            if (Camera != null && Camera.UpdateTarget == null)
                Camera.UpdateTarget = AimCamera;
            float4 view = Camera != null ? Camera.ViewRect : new float4(hero - 20f, hero + 20f);
            view += new float4(-2f, -2f, 2f, 2f);
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));

            m_Ground.Clear(); m_Opaque.Clear(); m_Additive.Clear(); m_Effects.Clear();
            DrainFeedback(world);
            DrawGround(view);

            // Bulk: Burst jobs cull and pack straight into reserved batch slots.
            float tickDt = (float)session.Clock.StepSeconds;
            EnemiesDrawn = RunEnemies(world, alpha, view, time);
            RunGems(world, view);
            BulletsDrawn = RunBullets(world, alpha, tickDt, view);

            if (game.Flow != SvFlow.Menu)
            {
                var frame = m_Art.Hero.FrameAt(time);
                float side = game.Facing.x < 0f ? -1f : 1f;
                float flash = game.Invulnerable > 0f && ((int)(time * 20f) & 1) == 0 ? 0.7f : 0f;
                m_Opaque.Add(hero + new float2(0f, 0.45f), new float2(14f * side, 16f) / SvArt.PixelsPerUnit, m_Art.Sheet[frame].Uv, ActorDepth + hero.y * DepthPerY, new float4(1f), 0f, flash);
                int blades = SvRules.OrbitBlades(game);
                var s = world.Resource(SvKeys.Config).Settings;
                for (int k = 0; k < blades; k++)
                {
                    float a = game.OrbitAngle + k * math.PI * 2f / blades + alpha * tickDt * 3.2f;
                    float2 p = hero + new float2(math.cos(a), math.sin(a)) * s.OrbitRadius;
                    m_Opaque.Add(p, new float2(1f), m_Art.Sheet[m_Art.Blade].Uv, BulletDepth, new float4(1f), time * 12f);
                }
                float magnet = SvRules.Magnet(s, game);
                m_Effects.Add(hero, new float2(magnet * 2f), m_Art.Sheet[m_Art.Ring].Uv, GroundDepth - 0.5f, new float4(0.5f, 0.8f, 1f, 0.12f));
            }
            m_Fx.UpdateAndDraw(dt, m_Effects, m_Art.Sheet, m_Art.Font);

            m_Ground.Draw(bounds);
            m_Opaque.Draw(bounds);
            m_Additive.Draw(bounds);
            m_Effects.Draw(bounds);
            SpritesDrawn = m_Ground.Count + m_Opaque.Count + m_Additive.Count + m_Effects.Count;
            // API payload includes full data textures / indirect arguments, not just live packed sprites.
            BytesUploaded = m_Ground.BytesUploaded + m_Opaque.BytesUploaded + m_Additive.BytesUploaded + m_Effects.BytesUploaded;
        }

        /// <summary>Camera callback (runs before this renderer): follow the interpolated hero.</summary>
        void AimCamera(FollowCamera2D camera)
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            var game = session.World.Resource(SvKeys.Game);
            camera.Target = math.lerp(game.HeroPrev, game.Hero, session.InterpolationAlpha);
        }

        void DrawGround(float4 view)
        {
            var uv = m_Art.Sheet[m_Art.Ground].Uv;
            int2 min = (int2)math.floor(view.xy / GroundTile), max = (int2)math.floor(view.zw / GroundTile);
            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                    m_Ground.Add((new float2(x, y) + 0.5f) * GroundTile, new float2(GroundTile + 0.01f), uv, GroundDepth, new float4(1f));
        }

        int RunEnemies(SimWorld world, float alpha, float4 view, float time)
        {
            int count = world.Table(SvKeys.Enemy).Count;
            if (count == 0) return 0;
            int start = m_Opaque.Count;
            var slots = m_Opaque.Reserve(count);
            new EnemyDrawJob
            {
                Position = world.Column(SvKeys.Position), Prev = world.Column(SvKeys.PrevPosition), Info = world.Column(SvKeys.Info),
                Uv = m_Art.EnemyUv, Out = slots, Written = m_Counts, Alpha = alpha, View = view, Time = time,
                Count = math.min(count, slots.Length),
            }.Run();
            m_Opaque.Trim(start + m_Counts[0]);
            return m_Counts[0];
        }

        void RunGems(SimWorld world, float4 view)
        {
            int count = world.Table(SvKeys.Gem).Count;
            if (count == 0) return;
            int start = m_Opaque.Count;
            var slots = m_Opaque.Reserve(count);
            new GemDrawJob
            {
                Position = world.Column(SvKeys.GemPosition), Info = world.Column(SvKeys.GemInfo), Uv = m_Art.Sheet[m_Art.Gem].Uv,
                Out = slots, Written = m_Counts, View = view, Count = math.min(count, slots.Length),
            }.Run();
            m_Opaque.Trim(start + m_Counts[0]);
        }

        int RunBullets(SimWorld world, float alpha, float tickDt, float4 view)
        {
            int count = world.Table(SvKeys.Bullet).Count;
            if (count == 0) return 0;
            int start = m_Additive.Count;
            var slots = m_Additive.Reserve(count);
            new BulletDrawJob
            {
                Position = world.Column(SvKeys.BulletPosition), Info = world.Column(SvKeys.BulletInfo), Dead = world.Table(SvKeys.Bullet).DeadFlags,
                Uv = m_Art.BulletUv, Out = slots, Written = m_Counts, Back = (1f - alpha) * tickDt, View = view, Count = math.min(count, slots.Length),
            }.Run();
            m_Additive.Trim(start + m_Counts[0]);
            return m_Counts[0];
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct EnemyDrawJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Position, Prev;
            [ReadOnly] public NativeArray<EnemyInfo> Info;
            [ReadOnly] public NativeArray<float4> Uv;
            public NativeArray<PackedSprite> Out;
            public NativeArray<int> Written;
            public float Alpha, Time;
            public float4 View;
            public int Count;

            public void Execute()
            {
                int n = 0;
                for (int i = 0; i < Count; i++)
                {
                    float2 p = math.lerp(Prev[i], Position[i], Alpha);
                    if (p.x < View.x || p.y < View.y || p.x > View.z || p.y > View.w) continue;
                    var info = Info[i];
                    if (info.Has(EnemyFlags.Dead)) continue;
                    int frame = ((int)(Time * 5f + i * 0.37f)) & 1;
                    float size = info.Radius * 2.6f;
                    float side = Position[i].x < Prev[i].x ? -1f : 1f;
                    float4 tint = info.Has(EnemyFlags.Elite) ? new float4(1.3f, 0.9f, 0.6f, 1f) : new float4(1f);
                    Out[n++] = PackedSprite.Pack(p + new float2(0f, size * 0.4f), new float2(size * side, size), Uv[(info.Kind - 1) * 2 + frame],
                        ActorDepth + p.y * DepthPerY, tint, 0f, math.saturate(info.Flash * 8f));
                }
                Written[0] = n;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct GemDrawJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Position;
            [ReadOnly] public NativeArray<GemInfo> Info;
            public float4 Uv;
            public NativeArray<PackedSprite> Out;
            public NativeArray<int> Written;
            public float4 View;
            public int Count;

            public void Execute()
            {
                int n = 0;
                for (int i = 0; i < Count; i++)
                {
                    float2 p = Position[i];
                    if (p.x < View.x || p.y < View.y || p.x > View.z || p.y > View.w) continue;
                    int v = Info[i].Value;
                    float4 tint = v >= 10 ? new float4(1.6f, 0.5f, 0.5f, 1f) : v >= 3 ? new float4(0.5f, 1.5f, 0.6f, 1f) : new float4(0.5f, 0.8f, 1.6f, 1f);
                    float size = v >= 10 ? 0.75f : 0.5f;
                    Out[n++] = PackedSprite.Pack(p, new float2(size * 0.78f, size), Uv, GemDepth, tint);
                }
                Written[0] = n;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct BulletDrawJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Position;
            [ReadOnly] public NativeArray<BulletInfo> Info;
            [ReadOnly] public NativeArray<byte> Dead;
            [ReadOnly] public NativeArray<float4> Uv;
            public NativeArray<PackedSprite> Out;
            public NativeArray<int> Written;
            public float Back;
            public float4 View;
            public int Count;

            public void Execute()
            {
                int n = 0;
                for (int i = 0; i < Count; i++)
                {
                    if (Dead[i] != 0) continue;
                    var b = Info[i];
                    float2 p = Position[i] - b.Velocity * Back;   // interpolated between ticks
                    if (p.x < View.x || p.y < View.y || p.x > View.z || p.y > View.w) continue;
                    float size = b.Radius * (b.Team == BulletTeam.Enemy ? 2.8f : 3.2f);
                    float rotation = math.atan2(b.Velocity.y, b.Velocity.x);
                    Out[n++] = PackedSprite.Pack(p, new float2(size), Uv[(int)b.Visual], BulletDepth, new float4(1f), rotation);
                }
                Written[0] = n;
            }
        }

        void DrainFeedback(SimWorld world)
        {
            var feedback = world.Resource(SvKeys.Feedback);
            for (int i = 0; i < feedback.Count; i++)
            {
                var e = feedback[i];
                Feedback?.Invoke(e);
                switch (e.Kind)
                {
                    case SvFeedbackKind.Death:
                        if (m_Fx.Active < 400)
                            m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position, Size = new float2(math.max(e.Value * 3f, 0.8f)), Color = new float4(1f, 1f, 1f, 0.8f), Depth = ActorDepth - 0.1f });
                        break;
                    case SvFeedbackKind.LevelUp:
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = new SpriteClip(m_Art.Ring, 1, 1f, false), Position = e.Position, Size = new float2(6f), Life = 0.6f, ScaleFrom = 0.2f, ScaleTo = 1.2f, Fade = true, Color = new float4(1f, 0.9f, 0.4f, 0.9f), Depth = ActorDepth - 0.2f });
                        break;
                    case SvFeedbackKind.HeroHurt:
                        Camera?.Shake(0.08f, 0.12f);
                        m_Fx.Spawn(new SpriteEffects.Effect { NumberMode = true, Number = (int)math.round(e.Value), Prefix = '-', Position = e.Position + new float2(0f, 1f), Velocity = new float2(0f, 1.2f), Size = new float2(0.28f), Life = 0.7f, Fade = true, Color = new float4(1f, 0.35f, 0.3f, 1f), Depth = ActorDepth - 0.3f });
                        break;
                }
            }
            feedback.Clear();
        }
    }
}
