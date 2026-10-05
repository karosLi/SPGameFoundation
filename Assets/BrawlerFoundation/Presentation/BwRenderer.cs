using SPF.L1.Skeleton;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BrawlerFoundation.Presentation
{
    /// <summary>
    /// Draws the brawl: every fighter's pose is evaluated at the interpolated time (Burst, parallel over fighters)
    /// and its ten cut-out parts are written straight into the sprite batch as packed instances; the arena is a
    /// static batch; health bars, shadows and hit sparks are ordinary sprites.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class BwRenderer : MonoBehaviour
    {
        const float ArenaDepth = 8f, ShadowDepth = 6f, FighterDepth = 4f, FxDepth = 1f;
        const int MaxFighters = 64;

        public SessionHost Host;
        public FollowCamera2D Camera;
        public System.Action<BwFeedback> Feedback;

        RenderAssets m_Assets;
        BwArt m_Art;
        SpriteBatch m_Arena, m_Fighters, m_Effects;
        SpriteEffects m_Fx;
        SimSession m_Session;
        NativeArray<Animator2D> m_Animators;
        NativeArray<float2> m_Roots;
        NativeArray<float> m_Facing, m_Flash, m_Depth;
        NativeArray<float4> m_Tint;
        NativeArray<int> m_Skin;
        NativeArray<BoneLocal> m_Scratch;
        NativeArray<BoneWorld> m_World;
        bool m_ArenaBuilt;

        public RenderTier Tier => m_Assets?.Tier ?? RenderTier.DataTexture;
        public int SpritesDrawn { get; private set; }
        public int PartsDrawn { get; private set; }

        void OnDestroy() => Release();

        void Release()
        {
            m_Arena?.Dispose(); m_Fighters?.Dispose(); m_Effects?.Dispose();
            m_Arena = m_Fighters = m_Effects = null;
            m_Art?.Dispose();
            m_Art = null;
            m_Assets?.Dispose();
            m_Assets = null;
            if (m_Animators.IsCreated)
            {
                m_Animators.Dispose(); m_Roots.Dispose(); m_Facing.Dispose(); m_Flash.Dispose(); m_Depth.Dispose();
                m_Tint.Dispose(); m_Skin.Dispose(); m_Scratch.Dispose(); m_World.Dispose();
            }
        }

        void Bind(SimSession session)
        {
            Release();
            m_Session = session;
            var rig = session.World.Resource(BwKeys.Rig);
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = BwArt.Build(rig);
            var atlas = m_Art.Sheet.Texture;
            m_Arena = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, 1024, queueOffset: -10);
            m_Fighters = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, MaxFighters * (m_Art.Parts.Length + 4));
            m_Effects = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Translucent, 512);
            m_Fx = new SpriteEffects(128);
            int bones = rig.Asset.BoneCount;
            m_Animators = new NativeArray<Animator2D>(MaxFighters, Allocator.Persistent);
            m_Roots = new NativeArray<float2>(MaxFighters, Allocator.Persistent);
            m_Facing = new NativeArray<float>(MaxFighters, Allocator.Persistent);
            m_Flash = new NativeArray<float>(MaxFighters, Allocator.Persistent);
            m_Depth = new NativeArray<float>(MaxFighters, Allocator.Persistent);
            m_Tint = new NativeArray<float4>(MaxFighters, Allocator.Persistent);
            m_Skin = new NativeArray<int>(MaxFighters, Allocator.Persistent);
            m_Scratch = new NativeArray<BoneLocal>(MaxFighters * bones * 2, Allocator.Persistent);
            m_World = new NativeArray<BoneWorld>(MaxFighters * bones, Allocator.Persistent);
            m_ArenaBuilt = false;
        }

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var world = session.World;
            var game = world.Resource(BwKeys.Game);
            var rig = world.Resource(BwKeys.Rig);
            float alpha = session.InterpolationAlpha;
            float tickDt = 1f / 60f;
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));
            if (Camera != null && Camera.UpdateTarget == null) Camera.UpdateTarget = Frame;
            if (!m_ArenaBuilt) { BuildArena(); m_ArenaBuilt = true; }

            m_Fighters.Clear();
            m_Effects.Clear();
            DrainFeedback(world);
            PartsDrawn = 0;
            int count = game.Flow == BwFlow.Menu ? 0 : math.min(world.Table(BwKeys.Fighter).Count, MaxFighters);
            if (count > 0)
            {
                var pos = world.Column(BwKeys.Position);
                var prev = world.Column(BwKeys.Prev);
                var info = world.Column(BwKeys.Info);
                var anim = world.Column(BwKeys.Anim);
                for (int i = 0; i < count; i++)
                {
                    var f = info[i];
                    float2 p = math.lerp(prev[i], pos[i], alpha);
                    m_Animators[i] = anim[i];
                    m_Roots[i] = p;
                    m_Facing[i] = f.Facing;
                    m_Flash[i] = f.Flash * 0.8f;
                    m_Skin[i] = f.Team == 0 ? 0 : math.clamp(f.Variant, 1, BwArt.Skins - 1);
                    // Fighters further back (higher y never happens; use x order for stable overlap) and KO'd ones fade.
                    m_Depth[i] = FighterDepth - i * 0.02f;
                    float fade = f.State == FighterState.KO ? math.saturate(1f - (f.StateTime - BwRules.KoTime + 0.4f) / 0.4f) : 1f;
                    m_Tint[i] = new float4(1f, 1f, 1f, 1f) * new float4(fade, fade, fade, 1f);
                    m_Fighters.Add(p + new float2(0f, 0.03f), new float2(1.1f, 0.3f), m_Art.Sheet[m_Art.Shadow].Uv, ShadowDepth, new float4(1f, 1f, 1f, 1f));
                    if (f.Team == 1 && f.State != FighterState.KO)
                    {
                        float hp = math.saturate(f.Hp / math.max(f.MaxHp, 1f));
                        m_Fighters.Add(p + new float2(0f, 2.15f), new float2(0.9f, 0.1f), m_Art.Sheet[m_Art.Bar].Uv, FxDepth + 0.2f, new float4(0.15f, 0.1f, 0.1f, 1f));
                        m_Fighters.Add(p + new float2(-0.45f + 0.45f * hp, 2.15f), new float2(0.9f * hp, 0.08f), m_Art.Sheet[m_Art.Bar].Uv, FxDepth + 0.1f, new float4(0.9f, 0.25f, 0.2f, 1f));
                    }
                }
                int parts = m_Art.Parts.Length;
                var output = m_Fighters.Reserve(count * parts);
                if (output.Length == count * parts)
                {
                    var pose = new SkeletonPoseJob
                    {
                        View = rig.View, Animators = m_Animators, Roots = m_Roots, Facing = m_Facing, Scale = 1f,
                        TimeOffset = alpha * tickDt, Scratch = m_Scratch, World = m_World,
                    }.Schedule(count, 8);
                    new SkeletonSpriteJob
                    {
                        Bones = rig.Asset.BoneCount, World = m_World, Attachments = m_Art.Parts, Facing = m_Facing, Tint = m_Tint,
                        Flash = m_Flash, Depth = m_Depth, Skin = m_Skin, Palette = m_Art.Palette, LayerStep = 0.002f, Out = output,
                    }.Schedule(count, 8, pose).Complete();
                    PartsDrawn = count * parts;
                }
                else m_Fighters.Trim(m_Fighters.Count - output.Length);
            }
            m_Fx.UpdateAndDraw(Time.deltaTime, m_Effects, m_Art.Sheet, null);
            m_Arena.Draw(bounds, dirty: false);
            m_Fighters.Draw(bounds);
            m_Effects.Draw(bounds);
            SpritesDrawn = m_Arena.Count + m_Fighters.Count + m_Effects.Count;
        }

        void Frame(FollowCamera2D camera)
        {
            float aspect = math.max(camera.Camera != null ? camera.Camera.aspect : 16f / 9f, 0.3f);
            camera.Size = math.max(3.2f, (BwRules.ArenaHalf + 0.6f) / aspect);
            camera.Target = new float2(0f, camera.Size - 0.8f);
        }

        void BuildArena()
        {
            m_Arena.Clear();
            for (int x = -14; x < 14; x++)
            {
                m_Arena.Add(new float2(x + 0.5f, -0.5f), new float2(1.001f), m_Art.Sheet[m_Art.Floor].Uv, ArenaDepth, new float4(1f));
                m_Arena.Add(new float2(x + 0.5f, -1.5f), new float2(1.001f), m_Art.Sheet[m_Art.Floor].Uv, ArenaDepth, new float4(0.8f, 0.8f, 0.8f, 1f));
                for (int y = 0; y < 10; y++)
                    m_Arena.Add(new float2(x + 0.5f, y + 0.5f), new float2(1.001f), m_Art.Sheet[m_Art.Wall].Uv, ArenaDepth + 0.5f, new float4(1f));
            }
            m_Arena.Draw(new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f)), dirty: true);
        }

        void DrainFeedback(SPF.Runtime.World.SimWorld world)
        {
            var feedback = world.Resource(BwKeys.Feedback);
            for (int i = 0; i < feedback.Count; i++)
            {
                var e = feedback[i];
                Feedback?.Invoke(e);
                switch (e.Kind)
                {
                    case BwFeedbackKind.Hit:
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position, Size = new float2(0.5f), Color = new float4(1f, 0.95f, 0.7f, 1f), Depth = FxDepth });
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position, Size = new float2(0.45f), Color = new float4(1f), Depth = FxDepth - 0.1f, Life = 0.12f, ScaleFrom = 1.4f, ScaleTo = 0.6f });
                        Camera?.Shake(0.04f + e.Value * 0.004f, 0.1f);
                        break;
                    case BwFeedbackKind.KO:
                        for (int k = 0; k < 4; k++)
                            m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position, Velocity = new float2(math.cos(k * 1.57f + 0.4f), math.sin(k * 1.57f + 0.4f)) * 1.5f, Size = new float2(0.35f), Color = new float4(1f, 0.9f, 0.4f, 1f), Depth = FxDepth, Life = 0.6f });
                        Camera?.Shake(0.15f, 0.25f);
                        break;
                }
            }
            feedback.Clear();
        }
    }
}
