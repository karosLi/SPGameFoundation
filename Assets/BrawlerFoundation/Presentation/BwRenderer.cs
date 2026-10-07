using SPF.Presentation.ArtDirection;
using SPF.Presentation.Particles;
using SPF.Contracts.Weapons;
using SPF.Contracts.Combat;
using SPF.L2.Weapons;
using SPF.L1.Skeleton;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Combat;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using SPF.Runtime.World;
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
        public bool NaturalCharacters;
        public int QualityLevel { get; private set; }
        public void SetQualityLevel(int level) => QualityLevel = math.clamp(level, 0, 3);
        public GameplayCharacterPresenter Characters => m_Characters;
        GameplayCharacterPresenter m_Characters;
        WeaponParticlePresenter m_WeaponParticles;
        long m_WeaponTick=-1;
        uint m_WeaponRevision;
        SPF.L2.Skills.ActionPoseClock m_PoseClock;
        uint m_PoseRevision;
        WeaponRuntime m_WeaponRuntime;
        float2[] m_WeaponTrailPoints;
        long[] m_WeaponTrailBirths;
        bool[] m_WeaponTrailValid;
        uint m_WeaponCueSequence;
        bool m_HasWeaponCue;
        public uint LastWeaponCueSequence => m_WeaponCueSequence;
        public WeaponParticlePresenter WeaponParticles => m_WeaponParticles;
        DamageNumberPool m_DamageNumbers;
        public readonly DamageNumberLayout DamageLayout = new DamageNumberLayout();
        SpriteBatch m_DamageGlyphs;
        AppliedDamageJournal m_DamageJournal;
        DamageFactCursor m_DamageCursor;
        uint m_DamageRevision;
        public DamageNumberPool DamageNumbers => m_DamageNumbers;
        public int DamageNumberGlyphsDrawn => m_DamageGlyphs?.Count ?? 0;
        public long DamageNumberBytesUploaded => m_DamageGlyphs?.BytesUploaded ?? 0;
        public ulong MissedDamageFacts => m_DamageCursor.Missed;

        bool m_BoundNatural;
        public FollowCamera2D Camera;
        public System.Action<BwFeedback> Feedback;

        SanctuaryBackdrop m_Sanctuary;
        RenderAssets m_Assets;
        BwArt m_Art;
        SpriteBatch m_Arena, m_Fighters, m_Effects, m_NaturalShadows;
        SpriteEffects m_Fx;
        SimSession m_Session;
        MonotonicInterpolation m_Interpolation;
        ViewTimelineStamp m_Timeline;
        bool m_ResumePending;
        FollowCamera2D m_BoundCamera;
        System.Action<FollowCamera2D> m_CameraCallback;
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

        void OnDisable()
        {
            DetachCamera(); ClearTransientState(); m_Interpolation.Reset();
            m_ResumePending = true;
        }

        void ClearTransientState()
        {
            m_Fx?.Clear(); m_Characters?.Clear(); m_WeaponParticles?.Clear();
            m_DamageNumbers?.Clear(); m_DamageGlyphs?.Clear();
            m_HasWeaponCue = false;
            if (m_WeaponTrailValid != null) System.Array.Clear(m_WeaponTrailValid, 0, m_WeaponTrailValid.Length);
        }

        void BindCamera()
        {
            if (m_BoundCamera != Camera) { DetachCamera(); m_BoundCamera = Camera; }
            if (m_BoundCamera != null && m_BoundCamera.UpdateTarget == null)
            {
                if (m_CameraCallback == null) m_CameraCallback = Frame;
                m_BoundCamera.UpdateTarget = m_CameraCallback;
            }
        }

        void DetachCamera()
        {
            if (m_BoundCamera != null && m_BoundCamera.UpdateTarget == m_CameraCallback)
                m_BoundCamera.UpdateTarget = null;
            m_BoundCamera = null;
        }

        void Release()
        {
            DetachCamera(); m_Timeline.Reset(); m_Session = null; m_PoseClock = null; m_WeaponRuntime = null;
            m_WeaponTrailPoints = null; m_WeaponTrailBirths = null; m_WeaponTrailValid = null;
            m_DamageGlyphs?.Dispose(); m_DamageGlyphs = null; m_DamageNumbers = null;
            m_DamageJournal = null; m_DamageCursor = default; m_DamageRevision = 0;
            m_WeaponCueSequence = 0; m_HasWeaponCue = false; m_ResumePending = false;
            m_Fx = null;
            m_Sanctuary?.Dispose(); m_Sanctuary=null;
            m_WeaponParticles?.Dispose(); m_WeaponParticles=null; m_WeaponTick=-1;
            m_NaturalShadows?.Dispose(); m_NaturalShadows = null;
            m_Characters?.Dispose(); m_Characters = null;
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
            if (session != m_Session) m_Interpolation.Reset();
            Release();
            m_Timeline.Update(session, session.TimelineRevision, session.World.LevelVersion);
            m_Session = session; m_BoundNatural = NaturalCharacters;
            var rig = session.World.Resource(BwKeys.Rig);
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = BwArt.Build(rig, NaturalCharacters, session.World.HasResource(BwWeapons.Key), session.World.HasResource(AppliedDamageJournal.Key));
            if(NaturalCharacters)m_Sanctuary=new SanctuaryBackdrop(m_Assets.Tier,SanctuaryScene.Terrace);
            if (NaturalCharacters) m_Characters = new GameplayCharacterPresenter(m_Assets.Tier, math.clamp(session.World.Table(BwKeys.Fighter).Capacity, 1, 128), includeWeapons: session.World.HasResource(BwWeapons.Key));
            if(NaturalCharacters&&session.World.HasResource(BwWeapons.Key))m_WeaponParticles=new WeaponParticlePresenter(m_Assets.Tier, lowQuality: m_Assets.Tier!=RenderTier.GpuDriven);
            if(m_WeaponParticles!=null)
            {
                var equipped=session.World.Resource(BwWeapons.Key);int capacity=equipped.Projectiles.Length;
                m_WeaponTrailPoints=new float2[capacity];m_WeaponTrailBirths=new long[capacity];m_WeaponTrailValid=new bool[capacity];
                // A newly bound view starts at the current cue head; time spent hidden is not replayed.
                m_WeaponRuntime=equipped;m_WeaponRevision=equipped.Revision;m_WeaponTick=equipped.Tick;
                m_WeaponCueSequence=equipped.Equipment.CueSequence;m_HasWeaponCue=m_WeaponCueSequence!=0;
            }
            var atlas = m_Art.Sheet.Texture;
            if (session.World.HasResource(AppliedDamageJournal.Key))
            {
                m_DamageNumbers = new DamageNumberPool();
                m_DamageNumbers.Bind(session, session.TimelineRevision, session.World.LevelVersion);
                m_DamageJournal = session.World.Resource(AppliedDamageJournal.Key);
                m_DamageRevision = m_DamageJournal.Revision;
                m_DamageCursor = m_DamageJournal.CreateCursor();
                // Damage has an independent, fully warmed glyph budget; it cannot crowd out particles.
                m_DamageGlyphs = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Translucent, 192, queueOffset: 180);
                m_DamageGlyphs.Warmup(m_DamageGlyphs.Capacity);
            }
            m_Arena = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, 1024, queueOffset: -10);
            m_Fighters = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Opaque, MaxFighters * (m_Art.Parts.Length + 4));
            if (NaturalCharacters) { m_NaturalShadows = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Translucent, m_Characters.Capacity + 256, queueOffset: -60); m_NaturalShadows.Warmup(m_NaturalShadows.Capacity); }
            m_Effects = new SpriteBatch(m_Assets.Tier, atlas, BlendKind.Translucent, 512);
            // Preallocate dynamic pages and prefix textures at session bind, before counts grow in play.
            m_Fighters.Warmup(m_Fighters.Capacity);
            m_Effects.Warmup(m_Effects.Capacity);
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

        void LateUpdate() => RenderFrame();

        /// <summary>Same live presentation path; exposed for calibrated synchronous probes.</summary>
        public void RenderFrame() => RenderFrame(Time.deltaTime);

        /// <summary>Same read-only presentation path with an explicit visual timestep for synchronous
        /// probes. Normal LateUpdate keeps Unity's unmodified delta; this never advances simulation.</summary>
        public void RenderFrame(float presentationDeltaTime)
        {
            if (!math.isfinite(presentationDeltaTime) || presentationDeltaTime < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(presentationDeltaTime));
            if (!isActiveAndEnabled) return;
            var session = Host != null ? Host.Session : null;
            if (session == null || session.State == SessionState.Disposed)
            {
                if (m_Session != null) Release();
                return;
            }
            session.Sync();
            if (session != m_Session || m_BoundNatural != NaturalCharacters) Bind(session);
            var world = session.World;
            var game = world.Resource(BwKeys.Game);
            bool interpolationDiscontinuity = m_Timeline.Update(session, session.TimelineRevision, world.LevelVersion);
            bool damageDiscontinuity = interpolationDiscontinuity || m_ResumePending;
            if (interpolationDiscontinuity) ClearTransientState();
            if (m_ResumePending)
            {
                // Hidden time is not a delayed presentation backlog. Keep the warmed resources,
                // but begin this visible interval at the authoritative cue head.
                if (world.HasResource(BwWeapons.Key))
                {
                    var equipped = world.Resource(BwWeapons.Key);
                    m_WeaponRuntime = equipped; m_WeaponRevision = equipped.Revision; m_WeaponTick = equipped.Tick;
                    m_WeaponCueSequence = equipped.Equipment.CueSequence; m_HasWeaponCue = m_WeaponCueSequence != 0;
                }
                m_ResumePending = false;
            }
            if(NaturalCharacters&&world.HasResource(BwWeapons.PoseKey))
            {
                var poseClock=world.Resource(BwWeapons.PoseKey);
                if(!ReferenceEquals(m_PoseClock,poseClock)||m_PoseRevision!=poseClock.Revision){m_Characters?.Clear();interpolationDiscontinuity=true;}
                m_PoseClock=poseClock;m_PoseRevision=poseClock.Revision;
            }
            if(NaturalCharacters&&world.HasResource(BwWeapons.Key))
            {
                var equipped=world.Resource(BwWeapons.Key);
                if(!ReferenceEquals(m_WeaponRuntime,equipped)||m_WeaponRevision!=equipped.Revision){m_Characters?.Clear();interpolationDiscontinuity=true;}
            }
            var rig = world.Resource(BwKeys.Rig);
            float requestedAlpha = session.State == SessionState.Running && (game.Flow == BwFlow.Fighting || game.Flow == BwFlow.WaveClear) ? session.InterpolationAlpha : 1f;
            float alpha = m_Interpolation.Resolve(session.Clock.NextTickIndex, session.TimelineRevision, requestedAlpha, interpolationDiscontinuity);
            float tickDt = 1f / 60f;
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));
            BindCamera();
            if (!m_ArenaBuilt) { BuildArena(); m_ArenaBuilt = true; }

            m_NaturalShadows?.Clear();
            m_Fighters.Clear();
            m_Effects.Clear();
            DrainFeedback(world);
            PartsDrawn = 0;
            int count = game.Flow == BwFlow.Menu ? 0 : math.min(world.Table(BwKeys.Fighter).Count, NaturalCharacters ? m_Characters.Capacity : MaxFighters);
            if (NaturalCharacters) DrawNatural(world, game, rig, alpha, count, presentationDeltaTime);
            else if (count > 0)
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
                    if (f.Team == 1 && f.State != FighterState.KO && (m_DamageNumbers == null || DamageNumberLayout.ShowHealthBar(f.Hp, f.MaxHp, f.Variant == 3)))
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
            if(world.HasResource(BwWeapons.Key))DrawWeaponProjectiles(world.Resource(BwWeapons.Key),alpha);
            m_Fx.UpdateAndDraw(presentationDeltaTime, m_Effects, m_Art.Sheet, null);
            UpdateDamageNumbers(session, damageDiscontinuity || game.Flow == BwFlow.Menu, session.State == SessionState.Running && (game.Flow == BwFlow.Fighting || game.Flow == BwFlow.WaveClear) ? presentationDeltaTime : 0f, Camera != null ? Camera.ViewRect : new float4(-12, -3, 12, 7), alpha);
            if(m_Sanctuary!=null&&m_Sanctuary.Ready)m_Sanctuary.Draw(Camera!=null?Camera.ViewRect:new float4(-12,-3,12,7),bounds);
            else m_Arena.Draw(bounds, dirty: false);
            m_NaturalShadows?.Draw(bounds);
            m_Fighters.Draw(bounds);
            m_Characters?.Draw(bounds);
            m_Effects.Draw(bounds); m_DamageGlyphs?.Draw(bounds);
            if(m_WeaponParticles!=null){UpdateWeaponParticles(world,game,alpha,presentationDeltaTime);m_WeaponParticles.EndFrame(bounds);}
            SpritesDrawn = (m_Sanctuary!=null&&m_Sanctuary.Ready?m_Sanctuary.SpritesDrawn:m_Arena.Count) + m_Fighters.Count + m_Effects.Count + (m_DamageGlyphs?.Count ?? 0) + (m_Characters?.PartsDrawn ?? 0) + (m_NaturalShadows?.Count ?? 0);
        }

        void UpdateDamageNumbers(SimSession session, bool reset, float dt, float4 view, float alpha)
        {
            if (m_DamageNumbers == null) return;
            var journal = session.World.Resource(AppliedDamageJournal.Key);
            bool rebound = m_DamageNumbers.Bind(session, session.TimelineRevision, session.World.LevelVersion);
            if (reset || rebound || !ReferenceEquals(m_DamageJournal, journal) || m_DamageRevision != journal.Revision)
            {
                m_DamageNumbers.Clear(); m_DamageJournal = journal; m_DamageRevision = journal.Revision;
                // Same-tick restores, level resets and hidden intervals start at the current head.
                m_DamageCursor = journal.CreateCursor();
            }
            m_DamageGlyphs.Clear();
            m_DamageNumbers.BeginFrame(dt, QualityLevel);
            while (journal.TryRead(ref m_DamageCursor, out var fact))
                m_DamageNumbers.Emit(fact.Target, fact.Position, fact.Amount, fact.Critical, fact.Sequence, fact.Tick, session.Clock.StepSeconds);
            view = DamageNumberLayout.RenderedView(Camera != null ? Camera.Camera : null, view);
            ReserveDamageLayout(session.World, view, alpha);
            m_DamageNumbers.Draw(m_DamageGlyphs, m_Art.Sheet, m_Art.Font, view, layout: DamageLayout);
        }

        void ReserveDamageLayout(SimWorld world, float4 view, float alpha)
        {
            DamageLayout.Begin(view);
            if (m_DamageNumbers.Active == 0) return;
            var info = world.Column(BwKeys.Info); var position = world.Column(BwKeys.Position); var previous = world.Column(BwKeys.Prev);
            int count = math.min(world.Table(BwKeys.Fighter).Count, MaxFighters);
            for (int i = 0; i < count; i++)
            {
                if (info[i].State == FighterState.KO) continue;
                float2 p = math.lerp(previous[i], position[i], alpha);
                // Original natural head/horns and bar occupy root + .8 .. 2.43 (including small pose motion); reserve the
                // same visual extent even when an undamaged ordinary bar is omitted.
                float top = NaturalCharacters ? 2.43f : 2.2f;
                DamageLayout.ReserveActor(new float4(p + new float2(-.6f, .8f), p + new float2(.6f, top)));
            }
        }

        void UpdateWeaponParticles(SPF.Runtime.World.SimWorld world,BwGameState game,float alpha,float presentationDeltaTime)
        {
            var weapons=world.Resource(BwWeapons.Key);
            if(!ReferenceEquals(m_WeaponRuntime,weapons)||m_WeaponRevision!=weapons.Revision||weapons.Tick<m_WeaponTick||game.Flow==BwFlow.Menu){m_WeaponParticles.Clear();m_HasWeaponCue=false;System.Array.Clear(m_WeaponTrailValid,0,m_WeaponTrailValid.Length);}
            m_WeaponParticles.BeginFrame(m_Session.State==SessionState.Running&&game.Flow==BwFlow.Fighting?presentationDeltaTime:0,Camera!=null?Camera.ViewRect:new float4(-12,-5,12,8));
            var view=weapons.View(alpha);view.AimDirection=new float2(view.AimDirection.x,view.AimDirection.y*BwBeltRules.DepthProjection);
            if(m_Characters.TryReadWeapon(weapons.Owner,out var socket))m_WeaponParticles.UpdateEmitter(weapons.Owner,view,socket.Tip,socket.Muzzle,socket.Direction,FxDepth,true);
            for(int i=0;i<weapons.CueCount;i++)
            {
                var cue=weapons.Cues[i];
                if(m_HasWeaponCue&&unchecked((int)(cue.Sequence-m_WeaponCueSequence))<=0)continue;
                if(cue.ActionPulse==view.ActionPulse&&((cue.Kind==WeaponCueKind.Release&&(view.Stage==WeaponStage.Windup||view.Stage==WeaponStage.Active||view.Stage==WeaponStage.Recovery)&&view.Phase<view.ReleasePhase)||(cue.Kind==WeaponCueKind.Impact&&view.Stage==WeaponStage.Windup)))break;
                cue.Position=BwBeltRules.Project(cue.Position,cue.Height);cue.Height=0;cue.Direction=new float2(cue.Direction.x,cue.Direction.y*BwBeltRules.DepthProjection);
                m_WeaponParticles.SubmitCue(cue,FxDepth,true,weapons.Profile(cue.ContentId).Family);m_WeaponCueSequence=cue.Sequence;m_HasWeaponCue=true;
            }
            for(int i=0;i<weapons.Projectiles.Length;i++)
            {
                var shot=weapons.Projectiles[i];if(!weapons.ProjectileVisible(i,alpha)||weapons.Profile(shot.ContentId).Family!=WeaponActionFamily.Draw){m_WeaponTrailValid[i]=false;continue;}
                float2 point=BwBeltRules.Project(math.lerp(shot.Previous,shot.Position,alpha),shot.Height);
                if(m_WeaponTrailValid[i]&&m_WeaponTrailBirths[i]==shot.SpawnTick)m_WeaponParticles.ProjectileTrail(m_WeaponTrailPoints[i],point,FxDepth,shot.Pulse^(uint)i,true);
                m_WeaponTrailPoints[i]=point;m_WeaponTrailBirths[i]=shot.SpawnTick;m_WeaponTrailValid[i]=true;
            }
            m_WeaponTick=weapons.Tick;m_WeaponRevision=weapons.Revision;m_WeaponRuntime=weapons;
        }

        void DrawWeaponProjectiles(WeaponRuntime weapons,float alpha)
        {
            for(int i=0;i<weapons.Projectiles.Length;i++)
            {
                var shot=weapons.Projectiles[i];if(!weapons.ProjectileVisible(i,alpha))continue;var profile=weapons.Profile(shot.ContentId);bool arrow=profile.Family==WeaponActionFamily.Draw;
                float2 ground=math.lerp(shot.Previous,shot.Position,alpha);
                float2 point=BwBeltRules.Project(ground,shot.Height);
                float2 direction=new float2(shot.Direction.x,shot.Direction.y*BwBeltRules.DepthProjection);
                if(arrow)point=WeaponProjectileArt.ArrowCentre(BwBeltRules.Project(ground+shot.Direction*(profile.Radius*shot.Scale),shot.Height),direction,.66f);
                m_Effects.Add(point,arrow?new float2(.66f,.22f):new float2(.28f),m_Art.Sheet[arrow?m_Art.WeaponProjectiles.Arrow:m_Art.WeaponProjectiles.Spell].Uv,FxDepth,new float4(1),math.atan2(direction.y,direction.x));
            }
        }

        void Frame(FollowCamera2D camera)
        {
            float aspect = math.max(camera.Camera != null ? camera.Camera.aspect : 16f / 9f, 0.3f);
            camera.Size = math.max(NaturalCharacters ? 4.3f : 3.2f, (BwRules.ArenaHalf + 0.6f) / aspect);
            camera.Target = new float2(0f, NaturalCharacters ? 1.6f : camera.Size - 0.8f);
        }

        void BuildArena()
        {
            m_Arena.Clear();
            if (NaturalCharacters) { BuildNaturalArena(); return; }
            for (int x = -14; x < 14; x++)
            {
                m_Arena.Add(new float2(x + 0.5f, -0.5f), new float2(1.001f), m_Art.Sheet[m_Art.Floor].Uv, ArenaDepth, new float4(1f));
                m_Arena.Add(new float2(x + 0.5f, -1.5f), new float2(1.001f), m_Art.Sheet[m_Art.Floor].Uv, ArenaDepth, new float4(0.8f, 0.8f, 0.8f, 1f));
                for (int y = 0; y < 10; y++)
                    m_Arena.Add(new float2(x + 0.5f, y + 0.5f), new float2(1.001f), m_Art.Sheet[m_Art.Wall].Uv, ArenaDepth + 0.5f, new float4(1f));
            }
            m_Arena.Draw(new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f)), dirty: true);
        }

        void DrawNatural(SPF.Runtime.World.SimWorld world, BwGameState game, BwRig rig, float alpha, int count, float presentationDeltaTime)
        {
            bool belt=world.HasResource(BwBeltKeys.State);
            var position=world.Column(BwKeys.Position);var previous=world.Column(BwKeys.Prev);var info=world.Column(BwKeys.Info);
            var handles=world.Table(BwKeys.Fighter).Handles;
            m_Characters.Begin(m_Session.State==SessionState.Running ? presentationDeltaTime : 0,QualityLevel);
            if(game.Flow==BwFlow.Menu)m_Characters.Clear();
            for(int i=0;i<count;i++)
            {
                var f=info[i];float2 p=math.lerp(previous[i],position[i],alpha),ground=p;
                float2 velocity=(position[i]-previous[i])*60f;
                if(belt)
                {
                    ground=BwBeltRules.Project(math.lerp(world.Column(BwBeltKeys.PreviousGround)[i],world.Column(BwBeltKeys.Ground)[i],alpha),0);
                    // Final ground includes arena clamping and crowd separation. Keep this fixed-tick
                    // pair while paused; Begin(0) freezes the existing gait without replanning its feet.
                    var v=(world.Column(BwBeltKeys.Ground)[i]-world.Column(BwBeltKeys.PreviousGround)[i])/(float)m_Session.Clock.StepSeconds;
                    velocity=new float2(v.x,v.y*BwBeltRules.DepthProjection);
                }
                bool armed=f.Team==0&&world.HasResource(BwWeapons.Key);
                var weapon=armed?world.Resource(BwWeapons.Key).View(alpha):default;
                if(armed){weapon.AimDirection=new float2(weapon.AimDirection.x,weapon.AimDirection.y*BwBeltRules.DepthProjection);if(f.State==FighterState.Hit||f.State==FighterState.KO){weapon.Stage=WeaponStage.Idle;weapon.Phase=0;}}
                float phase=f.State==FighterState.Attack && f.Attack!=AttackKind.None ? math.saturate(f.StateTime/math.max(.01f,rig.Attack(f.Attack).Duration)) : 0;
                var state=f.State==FighterState.KO?GameplayCharacterState.Death:f.State==FighterState.Hit?GameplayCharacterState.Hit:
                    f.State==FighterState.Attack?(phase>.58f?GameplayCharacterState.Recovery:GameplayCharacterState.Attack):
                    f.State==FighterState.Walk?GameplayCharacterState.Run:GameplayCharacterState.Idle;
                float2 actionTarget=default;
                if(f.State==FighterState.Attack&&f.Attack!=AttackKind.None)actionTarget=BrawlerFoundation.Systems.BwProbe.Tip(rig,f,world.Column(BwKeys.Anim)[i],p,m_Scratch,m_World);
                if(armed&&f.State!=FighterState.Hit&&f.State!=FighterState.KO&&weapon.Stage!=WeaponStage.Idle)
                {state=weapon.Stage==WeaponStage.Recovery?GameplayCharacterState.Recovery:GameplayCharacterState.Attack;phase=weapon.Phase;}
                var skill=f.Team==0&&world.HasResource(BwWeapons.PoseKey)?world.Resource(BwWeapons.PoseKey):null;
                int skillId=skill!=null&&skill.Running?skill.ContentId:0;
                float skillPhase=skillId!=0?skill.Phase(alpha):0;uint skillPulse=skillId!=0?skill.Timeline.PulseId:0;
                // Existing enemy kicks retain their own authoritative fighter action, independent of held equipment.
                if(f.Attack==AttackKind.Kick&&f.State==FighterState.Attack){skillId=BwWeapons.KickPose;skillPhase=phase;}
                m_Characters.Submit(new GameplayCharacterInput {MotionProfileId=f.Team==0?0:f.Variant==3?2:1,SkillPoseId=skillId,SkillPhase=skillPhase,SkillPulse=skillPulse,SkillWeight=skillId!=0?1:0,Weapon=weapon,Aim=!armed&&f.State==FighterState.Attack,AimTarget=actionTarget,Handle=handles[i],Root=p,Ground=ground,Velocity=velocity,Facing=f.Facing,
                    Scale=.9f,State=state,Phase=phase,Action=f.Attack==AttackKind.Kick?GameplayCharacterAction.Kick:GameplayCharacterAction.Punch,
                    Kind=f.Team==0?0:1,Flash=f.Flash,Tint=f.Team==0?new float4(1f):new float4(1f,1f-f.Variant*.035f,1f-f.Variant*.06f,1f),Depth=FighterDepth+ground.y*.01f});
                m_NaturalShadows.Add(ground+new float2(0,.01f),new float2(.95f,.24f),m_Art.Sheet[m_Art.Shadow].Uv,ShadowDepth,new float4(0,0,0,.34f));
                if(f.Team==1&&f.State!=FighterState.KO&&(m_DamageNumbers==null||DamageNumberLayout.ShowHealthBar(f.Hp,f.MaxHp,f.Variant==3)))
                {
                    float hp=math.saturate(f.Hp/math.max(1,f.MaxHp));
                    m_Effects.Add(p+new float2(0,2.3f),new float2(.84f,.12f),m_Art.Sheet[m_Art.Bar].Uv,FxDepth,new float4(.07f,.13f,.16f,1));
                    m_Effects.Add(p+new float2(-.4f+.4f*hp,2.3f),new float2(.8f*hp,.06f),m_Art.Sheet[m_Art.Bar].Uv,FxDepth-.01f,new float4(.98f,.42f,.32f,1));
                }
            }
            m_Characters.Evaluate();PartsDrawn=m_Characters.PartsDrawn;
            if(belt)
            {
                var drops=world.Resource(BwBeltKeys.State).Drops;
                for(int i=0;i<drops.Length;i++)
                {
                    var drop=drops[i];if(drop.RemainingTicks<=0)continue;
                    float2 p=BwBeltRules.Project(drop.Ground,0)+new float2(0,.14f);
                    var color=drop.Kind==BwBeltDropKind.Coin?new float4(1.3f,.91f,.3f,1):new float4(.4f,1.1f,.7f,1);
                    m_Fighters.Add(p,new float2(.26f,.3f),m_Art.Sheet[m_Art.Star].Uv,FighterDepth+.1f,color);
                    m_NaturalShadows.Add(p-new float2(0,.14f),new float2(.35f,.09f),m_Art.Sheet[m_Art.Shadow].Uv,ShadowDepth,new float4(0,0,0,.3f));
                }
            }
        }

        void BuildNaturalArena()
        {
            var white=m_Art.Sheet[m_Art.Bar].Uv;
            m_Arena.Add(new float2(0,4),new float2(40,14),white,ArenaDepth+2,new float4(.055f,.10f,.16f,1));
            for(int x=-7;x<7;x++)for(int y=-2;y<2;y++)
                m_Arena.Add(new float2(x*2+1,y+ .5f),new float2(2.005f,1.005f),m_Art.Sheet[m_Art.Floor].Uv,ArenaDepth,new float4(y<0?.86f:1f,.96f,1f,1));
            m_Arena.Add(new float2(0,2.1f),new float2(28,.18f),white,ArenaDepth-.05f,new float4(.25f,.36f,.42f,1));
            m_Arena.Add(new float2(0,2.25f),new float2(28,.11f),white,ArenaDepth-.1f,new float4(.56f,.66f,.64f,1));
            for(int x=-7;x<=7;x++)
            {
                m_Arena.Add(new float2(x*2,2.5f),new float2(.65f,.55f),m_Art.Sheet[m_Art.Wall].Uv,ArenaDepth+.1f,new float4(.65f,.8f,.85f,1));
                m_Arena.Add(new float2(x*2,4.9f),new float2(.04f,3.9f),white,ArenaDepth+.5f,new float4(.08f,.15f,.19f,1));
            }
            m_Arena.Draw(new Bounds(Vector3.zero,new Vector3(1e5f,1e5f,100)),dirty:true);
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
