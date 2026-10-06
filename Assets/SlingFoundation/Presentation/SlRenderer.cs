using SPF.L1.Physics;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using Unity.Mathematics;
using UnityEngine;

namespace SlingFoundation.Presentation
{
    /// <summary>
    /// Draws the physics world: every body is one rotated packed sprite (the 32-byte instance carries the
    /// rotation), the ground is a static batch uploaded once per level, and the aim shows the sling band and a
    /// ballistic preview computed from the same launch rule the simulation uses.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class SlRenderer : MonoBehaviour
    {
        const float GroundDepth = 6f, BodyDepth = 3f, BirdDepth = 2f, FxDepth = 1f, SlingDepth = 2.5f;

        public SessionHost Host;
        public FollowCamera2D Camera;
        public System.Action<SlFeedback> Feedback;
        /// <summary>Current drag while aiming (set by the game shell); zero when not aiming.</summary>
        public float2 Aim;

        RenderAssets m_Assets;
        SlArt m_Art;
        SpriteBatch m_Ground, m_Bodies, m_Effects;
        SpriteEffects m_Fx;
        SimSession m_Session;
        int m_Built = -1;

        public RenderTier Tier => m_Assets?.Tier ?? RenderTier.DataTexture;
        public int SpritesDrawn { get; private set; }
        public int BodiesDrawn { get; private set; }

        void OnDestroy() => Release();

        void Release()
        {
            m_Ground?.Dispose(); m_Bodies?.Dispose(); m_Effects?.Dispose();
            m_Ground = m_Bodies = m_Effects = null;
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
            m_Art = SlArt.Build();
            var atlas = m_Art.Sheet.Texture;
            m_Ground = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, 512, queueOffset: -10);
            m_Bodies = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, SlRules.Capacity + 64);
            m_Effects = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Translucent, 512);
            // Preallocate dynamic pages and prefix textures at session bind, before counts grow in play.
            m_Bodies.Warmup(m_Bodies.Capacity);
            m_Effects.Warmup(m_Effects.Capacity);
            m_Fx = new SpriteEffects(192);
            m_Built = -1;
        }

        int SpriteFor(PieceKind kind, float hp, float time, int id) => kind switch
        {
            PieceKind.Wood => m_Art.Wood,
            PieceKind.Stone => m_Art.Stone,
            PieceKind.Glass => m_Art.Glass,
            PieceKind.Target => hp < SlRules.Toughness(PieceKind.Target) ? m_Art.TargetHurt : m_Art.Target,
            PieceKind.Bird => ((int)(time * 1.3f + id) % 5 == 0) ? m_Art.BirdBlink : m_Art.Bird,
            _ => -1,
        };

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var world = session.World;
            var game = world.Resource(SlKeys.Game);
            var physics = world.Resource(SlKeys.Physics);
            physics.Complete();   // the step job may still run from this frame's tick
            float time = Time.time;
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));
            if (Camera != null && Camera.UpdateTarget == null) Camera.UpdateTarget = Frame;

            bool rebuild = game.LevelBuilds != m_Built;
            if (rebuild)
            {
                BuildGround();
                m_Built = game.LevelBuilds;
                m_Fx.Clear();
                Camera?.Snap();
            }

            m_Bodies.Clear();
            m_Effects.Clear();
            DrainFeedback(world, physics, game);
            BodiesDrawn = 0;
            if (game.Flow != SlFlow.Menu)
            {
                var bodies = physics.Bodies;
                for (int i = 0; i < physics.HighWater; i++)
                {
                    var b = bodies[i];
                    if (b.Alive == 0) continue;
                    var kind = game.Kind[i];
                    int sprite = SpriteFor(kind, game.Hp[i], time, i);
                    if (sprite < 0) continue;
                    float2 size = b.Shape == ShapeKind.Circle ? new float2(b.Half.x * 2f) : b.Half * 2f;
                    // Damaged blocks darken as they lose toughness.
                    float health = kind == PieceKind.Wood || kind == PieceKind.Stone || kind == PieceKind.Glass
                        ? math.saturate(game.Hp[i] / SlRules.Toughness(kind)) : 1f;
                    float shade = 0.6f + 0.4f * health;
                    m_Bodies.Add(b.Position, size, m_Art.Sheet[sprite].Uv, kind == PieceKind.Bird ? BirdDepth : BodyDepth,
                        new float4(shade, shade, shade, 1f), b.Angle);
                    BodiesDrawn++;
                }

                // Sling, the waiting bird and the aim.
                float2 sling = SlRules.Sling;
                m_Bodies.Add(sling + new float2(0f, -0.4f), new float2(0.85f, 2f), m_Art.Sheet[m_Art.Sling].Uv, SlingDepth, new float4(1f));
                if (game.Flow == SlFlow.Aiming && game.BirdsLeft > 0)
                {
                    float2 pull = Aim;
                    float len = math.length(pull);
                    if (len > SlRules.MaxPull) pull *= SlRules.MaxPull / len;
                    float2 bird = sling + pull;
                    DrawBand(sling + new float2(-0.35f, 0.5f), bird);
                    DrawBand(sling + new float2(0.35f, 0.5f), bird);
                    m_Bodies.Add(bird, new float2(SlRules.BirdRadius * 2f), m_Art.Sheet[m_Art.Bird].Uv, BirdDepth, new float4(1f));
                    if (len > 0.3f) DrawPreview(sling, SlRules.LaunchVelocity(Aim), physics.Settings.Gravity);
                }
                // Birds still to come, lined up behind the sling.
                for (int i = 0; i < game.BirdsLeft - (game.Flow == SlFlow.Aiming ? 1 : 0); i++)
                    m_Bodies.Add(new float2(sling.x - 1.5f - i * 0.8f, SlRules.BirdRadius), new float2(SlRules.BirdRadius * 2f), m_Art.Sheet[m_Art.Bird].Uv, BirdDepth, new float4(1f));
            }
            m_Fx.UpdateAndDraw(Time.deltaTime, m_Effects, m_Art.Sheet, null);

            m_Ground.Draw(bounds, dirty: rebuild);
            m_Bodies.Draw(bounds);
            m_Effects.Draw(bounds);
            SpritesDrawn = m_Ground.Count + m_Bodies.Count + m_Effects.Count;
        }

        void DrawBand(float2 from, float2 to)
        {
            float2 d = to - from;
            float len = math.length(d);
            if (len < 1e-3f) return;
            m_Bodies.Add((from + to) * 0.5f, new float2(len, 0.12f), m_Art.Sheet[m_Art.Band].Uv, SlingDepth - 0.2f, new float4(1f), math.atan2(d.y, d.x));
        }

        void DrawPreview(float2 start, float2 velocity, float2 gravity)
        {
            for (int i = 1; i <= 22; i++)
            {
                float t = i * 0.07f;
                float2 p = start + velocity * t + 0.5f * gravity * t * t;
                float a = 1f - i / 24f;
                m_Effects.Add(p, new float2(0.16f), m_Art.Sheet[m_Art.Dot].Uv, FxDepth, new float4(1f, 1f, 1f, a));
            }
        }

        void Frame(FollowCamera2D camera)
        {
            // The whole field in view: the sling on the left, the structures on the right.
            float aspect = math.max(camera.Camera != null ? camera.Camera.aspect : 16f / 9f, 0.3f);
            camera.Size = math.max(8.5f, 16.5f / aspect);
            camera.Target = new float2(1.5f, camera.Size - 2f);
        }

        void BuildGround()
        {
            m_Ground.Clear();
            for (int x = -30; x < 40; x++)
            {
                m_Ground.Add(new float2(x + 0.5f, -0.5f), new float2(1.001f), m_Art.Sheet[m_Art.Ground].Uv, GroundDepth, new float4(1f));
                m_Ground.Add(new float2(x + 0.5f, -1.5f), new float2(1.001f), m_Art.Sheet[m_Art.Ground].Uv, GroundDepth, new float4(0.85f, 0.85f, 0.85f, 1f));
                m_Ground.Add(new float2(x + 0.5f, -0.1f), new float2(1.001f, 0.25f), m_Art.Sheet[m_Art.Grass].Uv, GroundDepth - 0.1f, new float4(1f));
            }
            float2 mound = SlRules.Sling;
            for (int x = -1; x < 1; x++)
            {
                m_Ground.Add(new float2(mound.x + x + 0.5f, 0.5f), new float2(1.001f), m_Art.Sheet[m_Art.Ground].Uv, GroundDepth, new float4(1f));
                m_Ground.Add(new float2(mound.x + x + 0.5f, 0.9f), new float2(1.001f, 0.25f), m_Art.Sheet[m_Art.Grass].Uv, GroundDepth - 0.1f, new float4(1f));
            }
        }

        static float4 ChipColor(PieceKind kind) => kind switch
        {
            PieceKind.Wood => new float4(0.75f, 0.52f, 0.3f, 1f),
            PieceKind.Stone => new float4(0.6f, 0.6f, 0.65f, 1f),
            PieceKind.Glass => new float4(0.75f, 0.9f, 1f, 0.9f),
            _ => new float4(0.5f, 0.85f, 0.4f, 1f),
        };

        void DrainFeedback(SPF.Runtime.World.SimWorld world, PhysicsWorld2D physics, SlGameState game)
        {
            var feedback = world.Resource(SlKeys.Feedback);
            for (int i = 0; i < feedback.Count; i++)
            {
                var e = feedback[i];
                Feedback?.Invoke(e);
                switch (e.Kind)
                {
                    case SlFeedbackKind.Break:
                    case SlFeedbackKind.TargetDown:
                    {
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position, Size = new float2(1.2f), Color = new float4(1f), Depth = FxDepth });
                        var color = ChipColor(e.Piece);
                        for (int k = 0; k < 6; k++)
                        {
                            float angle = k * 1.047f + e.Position.x;
                            float2 dir = new float2(math.cos(angle), math.sin(angle));
                            m_Fx.Spawn(new SpriteEffects.Effect
                            {
                                Clip = m_Art.Puff, Position = e.Position, Velocity = dir * 3f + new float2(0f, 2f), Size = new float2(0.25f),
                                Color = color, Depth = FxDepth, Life = 0.45f, AngularVelocity = 6f, ScaleFrom = 1f, ScaleTo = 0.3f,
                            });
                        }
                        if (e.Kind == SlFeedbackKind.TargetDown) Camera?.Shake(0.15f, 0.25f);
                        break;
                    }
                    case SlFeedbackKind.Hit:
                        if (e.Strength > 6f) Camera?.Shake(math.min(0.05f * e.Strength / 6f, 0.25f), 0.15f);
                        break;
                }
            }
            feedback.Clear();
        }
    }
}
