using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using Unity.Mathematics;
using UnityEngine;

namespace RpgFoundation.Presentation
{
    /// <summary>
    /// Draws the dungeon with sprites from the procedural atlas (<see cref="RpgArt"/>), on both render tiers:
    /// <list type="bullet">
    /// <item>tile map as a static sprite batch (re-uploaded only when the floor changes);</item>
    /// <item>actors with state animations picked from the simulated action phase (idle, walk, run, wind-up /
    /// strike, cast, hit, death), mirrored by facing, hit flash, weapon overlays posed by the phase;</item>
    /// <item>items, projectiles, shadows; effects from feedback events: hit sparks, slashes, explosions,
    /// frost nova, whirlwind, the boss's slam warning and shockwave, dash afterimages, deaths, level-ups;</item>
    /// <item>damage / heal / gold numbers in the pixel font (same batches, no UI text), screen shake on
    /// heavy impacts; health bars as overlay quads.</item>
    /// </list>
    /// Runs after the camera (order 500); interpolates between ticks.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class RpgWorldRenderer : MonoBehaviour
    {
        const float TileDepth = 6f, WallDepth = 5.9f, ActorDepth = 1f, DepthPerY = 0.002f;

        public SessionHost Host;
        public FollowCamera2D Camera;
        public float ViewSize = 7.5f;

        /// <summary>Feedback events drained this frame (the HUD shows level-ups, etc.).</summary>
        public System.Action<FeedbackEvent> Feedback;

        struct ActorView
        {
            public int Generation;
            public RpgActorAnimation Animation;
            public float Ghost;
        }

        RenderAssets m_Assets;
        RpgArt m_Art;
        SpriteBatch m_Tiles, m_Opaque, m_Effects, m_Additive;
        QuadBatch m_Bars;
        SpriteEffects m_Fx;
        ActorView[] m_Views;
        int m_TileBuild = -1;
        uint m_TileVersion = uint.MaxValue;
        SimSession m_Session;
        float m_Time;
        Unity.Mathematics.Random m_Random = new Unity.Mathematics.Random(17);

        public RenderTier Tier => m_Assets?.Tier ?? RenderTier.DataTexture;
        public int LastActorsDrawn { get; private set; }
        public int TileQuads => m_Tiles?.Count ?? 0;
        public int SpritesDrawn { get; private set; }
        public int EffectsActive => m_Fx?.Active ?? 0;
        public RpgArt Art => m_Art;
        public float AnimationTime => m_Time;
        public bool TryGetAnimation(EntityHandle actor, out RpgActorAnimation animation)
        {
            animation = default;
            if (actor.IsNull || m_Views == null || actor.Index < 0 || actor.Index >= m_Views.Length
                || m_Views[actor.Index].Generation != actor.Generation || m_Session == null
                || !m_Session.World.Registry.TryResolve(actor, out _, out _)) return false;
            animation = m_Views[actor.Index].Animation;
            return true;
        }

        void OnDestroy() => Release();

        void Release()
        {
            m_Tiles?.Dispose(); m_Opaque?.Dispose(); m_Effects?.Dispose(); m_Additive?.Dispose();
            m_Bars?.Dispose();
            m_Art?.Dispose();
            m_Tiles = m_Opaque = m_Effects = m_Additive = null;
            m_Bars = null;
            m_Art = null;
            m_Assets?.Dispose();
            m_Assets = null;
        }

        void Bind(SimSession session)
        {
            Release();
            m_Session = session;
            var world = session.World;
            var config = world.Resource(RpgKeys.Config);
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = RpgArt.Build(config.Monsters.Length, k =>
            {
                var c = config.Monsters[k].Color;
                return new Color(c.x, c.y, c.z, 1f);
            });
            var tier = m_Assets.Tier;
            var atlas = m_Art.Sheet.Texture;
            var map = world.Resource(RpgKeys.Map);
            int actors = world.Table(RpgKeys.Actor).Capacity;
            m_Tiles = new SpriteBatch(tier, atlas, BlendKind.Opaque, map.Size.x * map.Size.y, queueOffset: -10);
            m_Opaque = new SpriteBatch(tier, atlas, BlendKind.Opaque, actors * 3 + world.Table(RpgKeys.Item).Capacity * 2 + world.Table(RpgKeys.Projectile).Capacity + world.Table(RpgKeys.Prop).Capacity);
            m_Effects = new SpriteBatch(tier, atlas, BlendKind.Translucent, 4096);
            m_Additive = new SpriteBatch(tier, atlas, BlendKind.Additive, 1024);
            // Preallocate dynamic pages and prefix textures at session bind, before counts grow in play.
            m_Opaque.Warmup(m_Opaque.Capacity);
            m_Effects.Warmup(m_Effects.Capacity);
            m_Additive.Warmup(m_Additive.Capacity);
            m_Bars = new QuadBatch(actors * 2 + 8, overlay: true);
            m_Fx = new SpriteEffects(512);
            m_Views = new ActorView[world.Registry.Capacity];
            m_TileBuild = -1;
            m_Time = 0f;
        }

        void LateUpdate()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            if (session != m_Session) Bind(session);
            var world = session.World;
            var game = world.Resource(RpgKeys.Game);
            var map = world.Resource(RpgKeys.Map);
            var config = world.Resource(RpgKeys.Config);
            float alpha = session.InterpolationAlpha;
            float dt = session.State == SessionState.Running ? Time.deltaTime : 0f;
            m_Time += dt;
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));

            if (Camera != null && Camera.UpdateTarget == null)
                Camera.UpdateTarget = AimCamera;

            bool tilesDirty = game.FloorBuilds != m_TileBuild || map.Version != m_TileVersion;
            if (tilesDirty)
            {
                BuildTiles(map, game);
                m_TileBuild = game.FloorBuilds;
                m_TileVersion = map.Version;
                m_Fx.Clear();
                System.Array.Clear(m_Views, 0, m_Views.Length); // New floor or restored snapshot.
            }

            float4 view = Camera != null ? Camera.ViewRect : new float4(-1e5f, -1e5f, 1e5f, 1e5f);
            view += new float4(-3f, -3f, 3f, 3f);
            m_Opaque.Clear();
            m_Effects.Clear();
            m_Additive.Clear();
            m_Bars.Clear();

            DrainFeedback(world, config, game);
            DrawActors(world, config, game, alpha, dt, view);
            DrawProps(world, view);
            DrawItems(world, view);
            DrawProjectiles(world, alpha, view);
            DrawStairs(map, game);
            m_Fx.UpdateAndDraw(dt, m_Effects, m_Art.Sheet, m_Art.Font);

            m_Tiles.Draw(bounds, dirty: tilesDirty);
            m_Opaque.Draw(bounds);
            m_Effects.Draw(bounds);
            m_Additive.Draw(bounds);
            m_Bars.Draw(bounds);
            SpritesDrawn = m_Tiles.Count + m_Opaque.Count + m_Effects.Count + m_Additive.Count;
        }

        // ---- Actors ----

        void DrawActors(SPF.Runtime.World.SimWorld world, RpgRuntimeConfig config, RpgGameState game, float alpha, float dt, float4 view)
        {
            var positions = world.Column(RpgKeys.Position);
            var prev = world.Column(RpgKeys.PrevPosition);
            var infos = world.Column(RpgKeys.Info);
            var healths = world.Column(RpgKeys.Health);
            var facings = world.Column(RpgKeys.Facing);
            var combat = world.Column(RpgKeys.Combat);
            var loadouts = world.Column(RpgKeys.Loadout);
            var statuses = world.Column(RpgKeys.Status);
            var handles = world.Table(RpgKeys.Actor).Handles;
            int count = world.Table(RpgKeys.Actor).Count;
            int drawn = 0;
            for (int i = 0; i < count; i++)
            {
                var info = infos[i];
                float2 p = math.lerp(prev[i], positions[i], alpha);
                if (p.x < view.x || p.y < view.y || p.x > view.z || p.y > view.w) continue;
                drawn++;
                var handle = handles[i];
                ref var v = ref m_Views[handle.Index];
                if (v.Generation != handle.Generation)
                    v = new ActorView { Generation = handle.Generation };

                bool hero = info.Has(ActorFlags.Hero);
                var art = hero ? m_Art.Hero : m_Art.Monsters[info.Kind - 1];
                var c = combat[i];
                float2 facing = c.Phase != ActionPhase.None && c.Phase != ActionPhase.Stagger && math.lengthsq(c.Aim) > 0f ? c.Aim : facings[i];
                float side = facing.x < -0.05f ? -1f : 1f;

                // Actual tick displacement includes collision, patrol/chase speed and analog intent.
                // It remains stable between simulation ticks and does not depend on render FPS/culling.
                float speed = math.length(positions[i] - prev[i]) / (float)m_Session.Clock.StepSeconds;
                float runSpeed = hero ? config.Settings.HeroSpeed : config.Monsters[info.Kind - 1].Speed;
                SkillKind skill = c.PhaseSkill > 0 && c.PhaseSkill <= config.Skills.Length
                    ? config.Skills[c.PhaseSkill - 1].Kind : SkillKind.None;
                v.Animation.Sample(handle.Generation, art, c, skill, info.Has(ActorFlags.Dead), speed, runSpeed, dt);
                var clip = v.Animation.Clip;
                int frame = v.Animation.Frame;

                float depth = ActorDepth + p.y * DepthPerY;
                float flash = math.saturate(c.HitFlash / 0.15f) * 0.85f;
                float4 tint = new float4(1f);
                if (c.Invulnerable > 0f) tint = new float4(0.7f, 0.9f, 1f, 1f);
                if (!hero && world.Column(RpgKeys.Mods)[i].HasSource(ModSource.Slow)) tint = new float4(0.65f, 0.85f, 1.15f, 1f);
                var status = statuses[i];
                if (status.Burning) tint *= new float4(1.25f, 0.8f, 0.6f, 1f);
                if (status.Poisoned) tint *= new float4(0.75f, 1.2f, 0.6f, 1f);
                if (dt > 0f && (status.Burning || status.Poisoned) && m_Random.NextFloat() < 0.12f)
                    m_Fx.Spawn(new SpriteEffects.Effect
                    {
                        Clip = status.Burning ? m_Art.Spark : m_Art.Sparkle, Position = p + art.Center + m_Random.NextFloat2(-0.25f, 0.25f), Velocity = new float2(0f, 0.9f),
                        Size = new float2(0.3f), Life = 0.4f, Fade = true, Depth = depth - 0.02f,
                        Color = status.Burning ? new float4(1f, 0.55f, 0.15f, 0.9f) : new float4(0.5f, 1f, 0.3f, 0.8f),
                    });
                bool elite = info.Has(ActorFlags.Elite);
                float2 size = art.Size * (elite ? 1.15f : 1f);
                float2 center = p + art.Center * (elite ? 1.15f : 1f);
                m_Opaque.Add(center, new float2(size.x * side, size.y), m_Art.Sheet[frame].Uv, depth, tint, 0f, flash);
                if (elite && !info.Has(ActorFlags.Dead))
                {
                    // Pulsing aura coloured by the leading affix.
                    float pulse = 0.35f + 0.15f * math.sin(m_Time * 4f + i);
                    m_Additive.Add(p + new float2(0f, 0.05f), new float2(info.Radius * 3.2f, info.Radius * 1.6f), m_Art.Sheet[m_Art.Ring].Uv, depth + 0.4f, AffixColor(info.Affixes, pulse));
                }
                // Shadow under the feet.
                float shadowW = math.max(info.Radius * 2.1f, 0.6f);
                m_Effects.Add(p + new float2(0f, -0.04f), new float2(shadowW, shadowW * 0.45f), m_Art.Sheet[m_Art.Shadow].Uv, depth + 0.5f, new float4(1f));

                if (!info.Has(ActorFlags.Dead))
                {
                    DrawWeapon(art, loadouts[i].Weapon, c, clip, frame, p, facing, side, depth, flash, elite ? 1.15f : 1f);
                    if (c.Phase == ActionPhase.Dash && dt > 0f)
                    {
                        v.Ghost -= dt;
                        if (v.Ghost <= 0f)
                        {
                            v.Ghost = 0.04f;
                            m_Fx.Spawn(new SpriteEffects.Effect
                            {
                                Clip = new SpriteClip(frame, 1, 1f, false), Position = center, Size = new float2(size.x * side, size.y),
                                Life = 0.25f, Color = new float4(0.5f, 0.8f, 1f, 0.6f), Fade = true, Depth = depth + 0.01f,
                            });
                        }
                    }
                    var h = healths[i];
                    if (hero || h.Current < h.Max)
                    {
                        float w = math.max(info.Radius * 2f, 0.8f);
                        float2 min = p + new float2(-w * 0.5f, size.y + 0.05f);
                        m_Bars.Add(min, new float2(w, 0.1f), 0f, new Color32(20, 20, 20, 200));
                        var fill = hero ? new Color32(80, 200, 255, 230) : info.Has(ActorFlags.Boss) ? new Color32(200, 90, 255, 230) : new Color32(230, 60, 50, 230);
                        m_Bars.Add(min, new float2(w * h.Fraction, 0.1f), -0.01f, fill);
                    }
                }
            }
            LastActorsDrawn = drawn;
        }

        /// <summary>Weapon overlay posed by the action phase: swings arc through the aim, spears thrust, bows and staves point.</summary>
        void DrawWeapon(CharacterArt art, WeaponKind kind, in CombatState c, CharacterClip clip, int frame, float2 p, float2 facing, float side, float depth, float flash, float scale)
        {
            var weapon = m_Art.Weapons[(int)kind];
            if (weapon == null) return;
            float aim = math.atan2(facing.y, facing.x);
            float rest = side > 0f ? -0.7f : math.PI + 0.7f;
            float angle = rest, reach = 0f;
            float t = c.PhaseProgress;
            bool ranged = kind == WeaponKind.Bow || kind == WeaponKind.Staff;
            switch (c.Phase)
            {
                case ActionPhase.Windup:
                    angle = kind == WeaponKind.Spear || ranged ? aim : aim + side * math.lerp(0.3f, 1.9f, t);
                    reach = kind == WeaponKind.Spear ? -0.25f * t : 0f;
                    break;
                case ActionPhase.Recover when c.PhaseSkill == 0:
                    if (kind == WeaponKind.Spear || ranged) { angle = aim; reach = kind == WeaponKind.Spear ? math.lerp(0.55f, 0f, t) : 0f; }
                    else angle = aim - side * math.lerp(1.3f, 0.6f, t);
                    break;
                case ActionPhase.Cast:
                    angle = clip == CharacterClip.Cast ? aim : side > 0f ? 1.2f : math.PI - 1.2f;
                    break;
                case ActionPhase.Channel:
                    angle = aim + c.PhaseTime * 18f;   // whirlwind spin
                    break;
                default:
                    if (ranged) angle = side > 0f ? 0.5f : math.PI - 0.5f;
                    break;
            }
            float2 dir = new float2(math.cos(angle), math.sin(angle));
            float2 anchor = art.HandAt(clip, frame) * scale;
            float2 hand = p + new float2(anchor.x * side, anchor.y);
            float2 center = hand + dir * ((weapon.Size.x * 0.5f - weapon.Grip + reach) * scale);
            // Keep the weapon's top side up whichever way it points.
            float flipY = math.cos(angle) < 0f ? -1f : 1f;
            bool behind = math.sin(angle) > 0.3f && c.Phase != ActionPhase.Channel;
            m_Opaque.Add(center, new float2(weapon.Size.x, weapon.Size.y * flipY) * scale, m_Art.Sheet[weapon.Frame].Uv, depth + (behind ? 0.004f : -0.004f), new float4(1f), angle, flash);
        }

        // ---- Items, projectiles, stairs ----

        void DrawProps(SPF.Runtime.World.SimWorld world, float4 view)
        {
            var positions = world.Column(RpgKeys.PropPosition);
            var infos = world.Column(RpgKeys.PropInfo);
            for (int i = 0; i < world.Table(RpgKeys.Prop).Count; i++)
            {
                float2 p = positions[i];
                if (p.x < view.x || p.y < view.y || p.x > view.z || p.y > view.w) continue;
                var prop = infos[i];
                float depth = ActorDepth + p.y * DepthPerY;
                switch (prop.Kind)
                {
                    case PropKind.Chest:
                        m_Opaque.Add(p + new float2(0f, 0.3f), new float2(18, 15) / RpgArt.PixelsPerUnit * 1.3f, m_Art.Sheet[prop.Active ? m_Art.ChestOpen : m_Art.ChestClosed].Uv, depth, new float4(1f));
                        m_Effects.Add(p + new float2(0f, -0.02f), new float2(0.9f, 0.3f), m_Art.Sheet[m_Art.Shadow].Uv, depth + 0.5f, new float4(1f));
                        break;
                    case PropKind.Barrel:
                        m_Opaque.Add(p + new float2(0f, 0.3f), new float2(13, 15) / RpgArt.PixelsPerUnit * 1.3f, m_Art.Sheet[m_Art.Barrel].Uv, depth, new float4(1f));
                        m_Effects.Add(p + new float2(0f, -0.02f), new float2(0.7f, 0.28f), m_Art.Sheet[m_Art.Shadow].Uv, depth + 0.5f, new float4(1f));
                        break;
                    case PropKind.Spikes:
                        m_Opaque.Add(p, new float2(0.9f), m_Art.Sheet[prop.Active ? m_Art.SpikesUp : m_Art.SpikesDown].Uv, TileDepth - 0.4f, new float4(1f));
                        break;
                }
            }
        }

        void DrawItems(SPF.Runtime.World.SimWorld world, float4 view)
        {
            var positions = world.Column(RpgKeys.ItemPosition);
            var infos = world.Column(RpgKeys.ItemInfo);
            var config = world.Resource(RpgKeys.Config);
            float time = m_Time;
            for (int i = 0; i < world.Table(RpgKeys.Item).Count; i++)
            {
                float2 p = positions[i];
                if (p.x < view.x || p.y < view.y || p.x > view.z || p.y > view.w) continue;
                var item = infos[i];
                float bob = 0.06f * math.sin(time * 4f + i);
                float depth = ActorDepth + p.y * DepthPerY + 0.01f;
                int frame;
                float2 size;
                float rotation = 0f;
                switch (item.Kind)
                {
                    case ItemKind.Gold: frame = m_Art.Coin.FrameAt(time + i * 0.13f); size = new float2(9, 9) / RpgArt.PixelsPerUnit; break;
                    case ItemKind.Potion: frame = m_Art.Potion; size = new float2(10, 12) / RpgArt.PixelsPerUnit; break;
                    default:
                    {
                        var g = config.Gear[item.Value];
                        if (g.Slot == GearSlot.Weapon && m_Art.Weapons[(int)g.Weapon] != null)
                        {
                            var w = m_Art.Weapons[(int)g.Weapon];
                            frame = w.Frame; size = w.Size; rotation = 0.6f;
                        }
                        else { frame = m_Art.ArmourIcon; size = new float2(12, 12) / RpgArt.PixelsPerUnit; }
                        // Glow under gear.
                        m_Additive.Add(p, new float2(0.9f), m_Art.Sheet[m_Art.Disc].Uv, depth + 0.02f, new float4(0.7f, 0.4f, 1f, 0.35f + 0.15f * math.sin(time * 5f)));
                        break;
                    }
                }
                m_Opaque.Add(p + new float2(0f, 0.1f + bob), size, m_Art.Sheet[frame].Uv, depth, new float4(1f), rotation);
                m_Effects.Add(p + new float2(0f, -0.04f), new float2(0.35f, 0.14f), m_Art.Sheet[m_Art.Shadow].Uv, depth + 0.3f, new float4(1f));
            }
        }

        void DrawProjectiles(SPF.Runtime.World.SimWorld world, float alpha, float4 view)
        {
            var positions = world.Column(RpgKeys.ProjectilePosition);
            var prev = world.Column(RpgKeys.ProjectilePrev);
            var infos = world.Column(RpgKeys.ProjectileInfo);
            float time = m_Time;
            for (int i = 0; i < world.Table(RpgKeys.Projectile).Count; i++)
            {
                var pr = infos[i];
                float2 p = math.lerp(prev[i], positions[i], alpha);
                if (p.x < view.x || p.y < view.y || p.x > view.z || p.y > view.w) continue;
                float angle = math.atan2(pr.Velocity.y, pr.Velocity.x);
                float depth = ActorDepth + p.y * DepthPerY - 0.05f;
                switch (pr.Visual)
                {
                    case ProjectileVisual.Fireball:
                        m_Additive.Add(p, new float2(1.1f), m_Art.Sheet[m_Art.Disc].Uv, depth, new float4(1f, 0.45f, 0.1f, 0.55f));
                        m_Opaque.Add(p, new float2(14, 14) / RpgArt.PixelsPerUnit * 1.3f, m_Art.Sheet[m_Art.Fireball.FrameAt(time)].Uv, depth - 0.01f, new float4(1f), angle);
                        break;
                    case ProjectileVisual.Bolt:
                        m_Additive.Add(p, new float2(0.7f), m_Art.Sheet[m_Art.Disc].Uv, depth, new float4(0.6f, 0.3f, 1f, 0.5f));
                        m_Opaque.Add(p, new float2(10, 10) / RpgArt.PixelsPerUnit, m_Art.Sheet[m_Art.Bolt.FrameAt(time)].Uv, depth - 0.01f, new float4(1f));
                        break;
                    case ProjectileVisual.Spit:
                        m_Opaque.Add(p, new float2(8, 8) / RpgArt.PixelsPerUnit, m_Art.Sheet[m_Art.Spit].Uv, depth, new float4(1f));
                        break;
                    default:
                        m_Opaque.Add(p, new float2(12, 5) / RpgArt.PixelsPerUnit * 1.2f, m_Art.Sheet[m_Art.Arrow].Uv, depth, new float4(1f), angle);
                        break;
                }
            }
        }

        void DrawStairs(TileMap map, RpgGameState game)
        {
            if (game.Flow == RpgFlow.Menu) return;
            float2 stairs = map.AsView().CenterOf(game.StairsCell);
            float time = m_Time;
            m_Opaque.Add(stairs, new float2(1f), m_Art.Sheet[m_Art.Stairs.FrameAt(time)].Uv, TileDepth - 0.5f, game.BossAlive ? new float4(0.8f, 0.5f, 0.5f, 1f) : new float4(1f));
            var glow = game.BossAlive ? new float4(0.7f, 0.15f, 0.2f, 0.3f) : new float4(0.3f, 0.9f, 1f, 0.25f + 0.15f * math.sin(time * 3f));
            m_Additive.Add(stairs, new float2(1.8f), m_Art.Sheet[m_Art.Disc].Uv, TileDepth - 0.6f, glow);
        }

        public static float4 AffixColor(Affix affixes, float alpha)
        {
            if ((affixes & Affix.Burning) != 0) return new float4(1f, 0.45f, 0.1f, alpha);
            if ((affixes & Affix.Venomous) != 0) return new float4(0.4f, 1f, 0.2f, alpha);
            if ((affixes & Affix.Frenzied) != 0) return new float4(1f, 0.15f, 0.2f, alpha);
            if ((affixes & Affix.Swift) != 0) return new float4(0.3f, 0.8f, 1f, alpha);
            return new float4(1f, 0.85f, 0.3f, alpha);
        }

        // ---- Feedback: effects and numbers ----

        void DrainFeedback(SPF.Runtime.World.SimWorld world, RpgRuntimeConfig config, RpgGameState game)
        {
            var feedback = world.Resource(RpgKeys.Feedback);
            for (int i = 0; i < feedback.Count; i++)
            {
                var e = feedback[i];
                Spawn(e, config);
                Feedback?.Invoke(e);
            }
            feedback.Clear();
        }

        void Spawn(in FeedbackEvent e, RpgRuntimeConfig config)
        {
            float depthFx = ActorDepth + e.Position.y * DepthPerY - 0.2f;
            float2 drift = new float2(m_Random.NextFloat(-0.4f, 0.4f), 1.6f);
            switch (e.Kind)
            {
                case FeedbackKind.Damage:
                case FeedbackKind.Crit:
                case FeedbackKind.HeroHurt:
                {
                    bool crit = e.Kind == FeedbackKind.Crit;
                    bool hurt = e.Kind == FeedbackKind.HeroHurt;
                    float2 at = e.Position + new float2(0f, 0.35f);
                    if (e.Source == HitSource.Burn || e.Source == HitSource.Poison)
                    {
                        // Damage over time: small coloured numbers, no spark or shake.
                        m_Fx.Spawn(new SpriteEffects.Effect
                        {
                            NumberMode = true, Number = math.max((int)math.round(e.Value), 1), Prefix = hurt ? '-' : '\0', Position = at + new float2(0f, 0.3f), Velocity = drift * 0.6f,
                            Size = new float2(0.22f), Life = 0.6f, Fade = true, Depth = depthFx - 0.1f,
                            Color = e.Source == HitSource.Burn ? new float4(1f, 0.6f, 0.2f, 1f) : new float4(0.55f, 1f, 0.35f, 1f),
                        });
                        break;
                    }
                    float sparkSize = crit ? 1.1f : 0.7f;
                    if (e.Source == HitSource.Explosion || e.Source == HitSource.Slam) sparkSize = 0.9f;
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Spark, Position = at - e.Direction * 0.2f, Size = new float2(sparkSize), Color = hurt ? new float4(1f, 0.5f, 0.5f, 1f) : new float4(1f), Depth = depthFx, Rotation = m_Random.NextFloat(6.28f) });
                    m_Fx.Spawn(new SpriteEffects.Effect
                    {
                        NumberMode = true, Number = (int)math.round(e.Value), Prefix = hurt ? '-' : '\0', Suffix = crit ? '!' : '\0',
                        Position = at + new float2(0f, 0.2f), Velocity = drift, Size = new float2(0.3f), Life = crit ? 0.95f : 0.75f,
                        ScaleFrom = crit ? 1.9f : 1.3f, ScaleTo = crit ? 1.2f : 0.9f, Fade = true, Depth = depthFx - 0.1f,
                        Color = hurt ? new float4(1f, 0.35f, 0.3f, 1f) : crit ? new float4(1f, 0.78f, 0.2f, 1f) : new float4(1f, 0.97f, 0.9f, 1f),
                    });
                    if (crit) Camera?.Shake(0.08f, 0.15f);
                    if (hurt) Camera?.Shake(0.06f, 0.12f);
                    break;
                }
                case FeedbackKind.Heal:
                    m_Fx.Spawn(new SpriteEffects.Effect { NumberMode = true, Number = (int)math.round(e.Value), Prefix = '+', Position = e.Position + new float2(0f, 0.6f), Velocity = new float2(0f, 1.2f), Size = new float2(0.3f), Life = 0.9f, ScaleFrom = 1.4f, ScaleTo = 1f, Fade = true, Depth = depthFx - 0.1f, Color = new float4(0.4f, 1f, 0.5f, 1f) });
                    for (int k = 0; k < 4; k++)
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Sparkle, Position = e.Position + m_Random.NextFloat2(-0.4f, 0.4f) + new float2(0f, 0.4f), Velocity = new float2(0f, 0.8f), Size = new float2(0.35f), Delay = k * 0.08f, Color = new float4(1f), Depth = depthFx });
                    break;
                case FeedbackKind.Gold:
                    m_Fx.Spawn(new SpriteEffects.Effect { NumberMode = true, Number = (int)math.round(e.Value), Prefix = '+', Suffix = 'G', Position = e.Position + new float2(0f, 0.4f), Velocity = new float2(0f, 1f), Size = new float2(0.26f), Life = 0.8f, Fade = true, Depth = depthFx - 0.1f, Color = new float4(1f, 0.85f, 0.25f, 1f) });
                    break;
                case FeedbackKind.Item:
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Sparkle, Position = e.Position + new float2(0f, 0.3f), Size = new float2(0.6f), Color = new float4(0.9f, 0.7f, 1f, 1f), Depth = depthFx });
                    break;
                case FeedbackKind.LevelUp:
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Pillar, Position = e.Position + new float2(0f, 0.8f), Size = new float2(0.8f, 2f), Life = 1.2f, Fade = true, Color = new float4(1f), Depth = depthFx });
                    break;
                case FeedbackKind.Death:
                {
                    var art = e.Actor == 0 ? m_Art.Hero : m_Art.Monsters[math.clamp(e.Actor - 1, 0, m_Art.Monsters.Length - 1)];
                    if (e.Actor != 0)
                    {
                        // The corpse plays its death clip (falling away from the blow), then fades.
                        float side = e.Direction.x > 0f ? -1f : 1f;
                        var clip = art.Clip(CharacterClip.Death);
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = clip, Position = e.Position + art.Center, Size = new float2(art.Size.x * side, art.Size.y), Life = clip.Duration + 0.6f, Fade = true, Color = new float4(1f), Depth = ActorDepth + e.Position.y * DepthPerY + 0.01f });
                    }
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position + new float2(0f, 0.3f), Size = new float2(math.max(e.Value * 2.6f, 0.9f)), Delay = 0.25f, Color = new float4(1f, 1f, 1f, 0.9f), Depth = depthFx });
                    if (e.Actor > 0 && config.Monsters[e.Actor - 1].Boss) Camera?.Shake(0.25f, 0.5f);
                    break;
                }
                case FeedbackKind.Swing:
                {
                    // Slash crescent at the strike (after the wind-up) for melee weapons.
                    if (e.Weapon == WeaponKind.Bow || e.Weapon == WeaponKind.Staff) break;
                    var w = config.Weapons[(int)e.Weapon];
                    float reach = w.Range * 0.6f + 0.3f;
                    float size = e.Weapon == WeaponKind.Hammer || e.Weapon == WeaponKind.Axe ? 1.6f : e.Weapon == WeaponKind.Spear ? 1.3f : 1.2f;
                    if (e.Actor > 0 && config.Monsters[e.Actor - 1].Boss) size *= 1.6f;
                    m_Fx.Spawn(new SpriteEffects.Effect
                    {
                        Clip = m_Art.Slash, Position = e.Position + e.Direction * reach + new float2(0f, 0.35f), Size = new float2(size), Rotation = math.atan2(e.Direction.y, e.Direction.x),
                        Delay = e.Value, Color = e.Weapon == WeaponKind.Claw ? new float4(0.7f, 1f, 0.6f, 0.8f) : new float4(1f, 1f, 1f, 0.9f), Depth = depthFx,
                    });
                    break;
                }
                case FeedbackKind.Explosion:
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Explosion, Position = e.Position, Size = new float2(e.Value * 2.2f), Color = new float4(1f), Depth = depthFx - 0.2f });
                    Camera?.Shake(0.12f, 0.2f);
                    break;
                case FeedbackKind.Nova:
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = new SpriteClip(m_Art.Ring, 1, 1f, false), Position = e.Position, Size = new float2(e.Value * 2f), Life = 0.45f, ScaleFrom = 0.15f, ScaleTo = 1f, Fade = true, Color = new float4(0.6f, 0.9f, 1f, 0.9f), Depth = depthFx });
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = new SpriteClip(m_Art.Disc, 1, 1f, false), Position = e.Position, Size = new float2(e.Value * 2f), Life = 0.5f, ScaleFrom = 0.3f, ScaleTo = 1f, Fade = true, Color = new float4(0.5f, 0.8f, 1f, 0.35f), Depth = depthFx + 0.1f });
                    break;
                case FeedbackKind.Whirlwind:
                    for (int k = 0; k < 3; k++)
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = new SpriteClip(m_Art.Slash.First, 1, 1f, false), Position = e.Position + new float2(0f, 0.3f), Size = new float2(e.Value * 1.6f), Rotation = k * 2.09f, AngularVelocity = -14f, Life = 0.9f, Fade = true, Color = new float4(0.9f, 0.95f, 1f, 0.7f), Depth = depthFx });
                    break;
                case FeedbackKind.SlamWarning:
                {
                    float cast = 0.9f;
                    for (int s = 0; s < config.Skills.Length; s++) if (config.Skills[s].Kind == SkillKind.Slam) cast = config.Skills[s].CastTime;
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = new SpriteClip(m_Art.Disc, 1, 1f, false), Position = e.Position, Size = new float2(e.Value * 2f), Life = cast, ScaleFrom = 0.4f, ScaleTo = 1f, Color = new float4(1f, 0.2f, 0.15f, 0.4f), Depth = TileDepth - 1f });
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = new SpriteClip(m_Art.Ring, 1, 1f, false), Position = e.Position, Size = new float2(e.Value * 2f), Life = cast, Color = new float4(1f, 0.3f, 0.2f, 0.8f), Depth = TileDepth - 1.1f });
                    break;
                }
                case FeedbackKind.Slam:
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = new SpriteClip(m_Art.Ring, 1, 1f, false), Position = e.Position, Size = new float2(e.Value * 2.2f), Life = 0.4f, ScaleFrom = 0.2f, ScaleTo = 1f, Fade = true, Color = new float4(1f, 0.8f, 0.5f, 1f), Depth = depthFx });
                    for (int k = 0; k < 8; k++)
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position + new float2(math.cos(k * 0.785f), math.sin(k * 0.785f)) * e.Value * 0.6f, Size = new float2(0.9f), Color = new float4(0.8f, 0.7f, 0.6f, 0.8f), Depth = depthFx });
                    Camera?.Shake(0.3f, 0.4f);
                    break;
                case FeedbackKind.Dash:
                    for (int k = 0; k < 3; k++)
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Dust, Position = e.Position - e.Direction * (0.2f * k), Size = new float2(0.5f, 0.33f), Delay = k * 0.05f, Color = new float4(1f), Depth = depthFx + 0.2f });
                    break;
                case FeedbackKind.Chest:
                    for (int k = 0; k < 5; k++)
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Sparkle, Position = e.Position + m_Random.NextFloat2(-0.4f, 0.4f) + new float2(0f, 0.5f), Velocity = new float2(0f, 1f), Size = new float2(0.35f), Delay = k * 0.06f, Color = new float4(1f, 0.9f, 0.4f, 1f), Depth = depthFx });
                    break;
                case FeedbackKind.Barrel:
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Puff, Position = e.Position + new float2(0f, 0.3f), Size = new float2(0.9f), Color = new float4(0.75f, 0.55f, 0.35f, 0.9f), Depth = depthFx });
                    for (int k = 0; k < 4; k++)
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = m_Art.Dust, Position = e.Position + new float2(0f, 0.3f), Velocity = m_Random.NextFloat2Direction() * 2f, Size = new float2(0.3f), Color = new float4(0.6f, 0.4f, 0.2f, 1f), Depth = depthFx });
                    break;
                case FeedbackKind.Cast:
                    m_Fx.Spawn(new SpriteEffects.Effect { Clip = new SpriteClip(m_Art.Disc, 1, 1f, false), Position = e.Position + new float2(0f, 0.9f), Size = new float2(0.9f), Life = math.max(e.Value, 0.2f), ScaleFrom = 0.3f, ScaleTo = 1.1f, Fade = true, Color = new float4(1f, 0.6f, 0.2f, 0.6f), Depth = depthFx });
                    break;
            }
        }

        // ---- Camera, tiles ----

        /// <summary>Camera callback (runs before this renderer): follow the interpolated hero.</summary>
        void AimCamera(FollowCamera2D camera)
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            var world = session.World;
            var game = world.Resource(RpgKeys.Game);
            camera.Size = ViewSize;
            if (world.Registry.TryResolve(game.Hero, out _, out int row))
                camera.Target = math.lerp(world.Column(RpgKeys.PrevPosition)[row], world.Column(RpgKeys.Position)[row], session.InterpolationAlpha) + new float2(0f, 0.4f);
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
                uint hash = math.hash(cell);
                int frame = solid ? m_Art.Wall[hash % (uint)m_Art.Wall.Length] : m_Art.Floor[hash % (uint)m_Art.Floor.Length];
                // Walls with floor below them get a darker lower edge (fake height).
                float4 tint = solid ? (view.IsSolid(cell - new int2(0, 1)) ? new float4(0.85f, 0.85f, 0.9f, 1f) : new float4(1f)) : new float4(1f);
                m_Tiles.Add(view.CenterOf(cell), new float2(t, t), m_Art.Sheet[frame].Uv, solid ? WallDepth : TileDepth, tint);
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
