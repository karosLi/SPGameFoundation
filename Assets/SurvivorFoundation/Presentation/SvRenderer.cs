using SPF.Presentation.Particles;
using SPF.Contracts.Weapons;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Presentation.Combat;
using SPF.Presentation.Animation;
using SPF.Contracts;
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
    /// Bullets use an additive batch. Classic art keeps opaque cutouts; smooth art uses stable-sorted
    /// translucent actors, with explicit batched shadow, health-bar and effect layers.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class SvRenderer : MonoBehaviour
    {
        const float GroundDepth = 9f, GemDepth = 4f, ActorDepth = 2f, DepthPerY = 0.0005f, BulletDepth = 0.5f;
        const float GroundTile = 2f;

        public SessionHost Host;
        public FollowCamera2D Camera;
        public System.Action<SvFeedback> Feedback;
        public SvArtStyle ArtStyle = SvArtStyle.Pixel;
        public bool NaturalCharacters;
        public const int NaturalEnemyCapacity = 192, NaturalDeathCapacity = 16;
        public int NaturalEnemyBudget => QualityLevel >= 3 ? 48 : QualityLevel >= 2 ? 96 : NaturalEnemyCapacity;
        public GameplayCharacterPresenter Characters => m_Characters;
        public int ArticulatedEnemies { get; private set; }
        GameplayCharacterPresenter m_Characters;
        GameplayCharacterSelection m_CharacterSelection;
        NativeArray<byte> m_NaturalMask;
        struct DeathVisual { public float2 Position; public float Scale, Life; public int Generation; }
        NativeArray<DeathVisual> m_Deaths;
        int m_DeathSequence, m_HeroCastTick = -100;
        bool m_BoundNatural;
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
        public int QualityLevel { get; private set; }
        public int ShadowBudget => QualityLevel >= 3 ? 32 : QualityLevel >= 2 ? 128 : QualityLevel >= 1 ? 256 : 768;
        public int EffectBudget => VfxBudget.ForQuality(QualityLevel).Active;
        public VfxDiagnostics VfxStats => m_CombatFx?.Stats ?? default;
        public int ActiveCombatEffects => m_CombatFx?.Active ?? 0;
        public VfxProfile ImpactProfile = VfxProfile.Electric, DeathProfile = VfxProfile.Destruction, PulseProfile = VfxProfile.Pulse;
        public int HealthBarsDrawn { get; private set; }
        public int ShadowsDrawn { get; private set; }
        public int RingsDrawn { get; private set; }
        public bool StableTranslucentActors => ArtStyle == SvArtStyle.SmoothOutline || NaturalCharacters;
        public void SetQualityLevel(int level) => QualityLevel = math.clamp(level, 0, 3);

        RenderAssets m_Assets;
        SvArt m_Art;
        SpriteBatch m_Ground, m_Opaque, m_Additive, m_Effects, m_Shadows, m_Health;
        NativeArray<PackedSprite> m_SortScratch;
        SpriteEffects m_Fx;
        CombatVfxPool m_CombatFx;
        struct HitVisual { public float2 Position; public uint Identity; }
        NativeArray<HitVisual> m_HitVisuals;
        int m_LastHitTick = -1;
        uint m_EventSequence;
        SimSession m_Session;
        SvArtStyle m_BoundStyle;
        NativeArray<float4> m_EnemyColors;
        NativeArray<int> m_Counts;
        int m_PreviousRunTicks;

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
            m_WeaponParticles?.Dispose();m_WeaponParticles=null;m_WeaponTick=-1;
            m_Characters?.Dispose(); m_Characters = null; ArticulatedEnemies = 0;
            if (m_NaturalMask.IsCreated) m_NaturalMask.Dispose();
            if (m_Deaths.IsCreated) m_Deaths.Dispose();
            m_Ground?.Dispose(); m_Opaque?.Dispose(); m_Additive?.Dispose(); m_Effects?.Dispose();
            m_Shadows?.Dispose(); m_Health?.Dispose();
            m_Shadows = m_Health = null;
            if (m_SortScratch.IsCreated) m_SortScratch.Dispose();
            m_Ground = m_Opaque = m_Additive = m_Effects = null;
            m_Art?.Dispose();
            m_Art = null;
            if (m_EnemyColors.IsCreated) m_EnemyColors.Dispose();
            if (m_Counts.IsCreated) m_Counts.Dispose();
            if (m_HitVisuals.IsCreated) m_HitVisuals.Dispose();
            m_Assets?.Dispose();
            m_Assets = null;
        }

        void Bind(SimSession session)
        {
            Release();
            m_Session = session; m_BoundStyle = ArtStyle; m_BoundNatural = NaturalCharacters;
            var world = session.World;
            var config = world.Resource(SvKeys.Config);
            m_Assets = new RenderAssets(RenderCapabilities.Detect());
            m_Art = SvArt.Build(config.EnemyKinds, k => { var c = config.Enemies[k].Color; return new Color(c.x, c.y, c.z, 1f); }, NaturalCharacters ? SvArtStyle.SmoothOutline : ArtStyle);
            var tier = m_Assets.Tier;
            if(NaturalCharacters&&world.HasResource(SvWeapons.Key))m_WeaponParticles=new WeaponParticlePresenter(tier, lowQuality: tier!=RenderTier.GpuDriven);
            if(m_WeaponParticles!=null)
            {
                var equipped=world.Resource(SvWeapons.Key);int capacity=equipped.Projectiles.Length;
                m_WeaponTrailPoints=new float2[capacity];m_WeaponTrailBirths=new long[capacity];m_WeaponTrailValid=new bool[capacity];
                // A newly bound view starts at the current cue head; time spent hidden is not replayed.
                m_WeaponRuntime=equipped;m_WeaponRevision=equipped.Revision;m_WeaponTick=equipped.Tick;
                m_WeaponCueSequence=equipped.Equipment.CueSequence;m_HasWeaponCue=m_WeaponCueSequence!=0;
            }
            var atlas = m_Art.Sheet.Texture;
            // Jobs validate containers before Execute, including the classic path. Keep its zero mask valid.
            m_NaturalMask = new NativeArray<byte>(world.Table(SvKeys.Enemy).Capacity, Allocator.Persistent);
            if (NaturalCharacters)
            {
                m_Characters = new GameplayCharacterPresenter(tier, NaturalEnemyCapacity + 1 + NaturalDeathCapacity, includeWeapons: world.HasResource(SvWeapons.Key));
                m_CharacterSelection = new GameplayCharacterSelection(NaturalEnemyCapacity);
                m_Deaths = new NativeArray<DeathVisual>(NaturalDeathCapacity, Allocator.Persistent);
                m_DeathSequence = 0; m_HeroCastTick = -100;
            }
            m_Ground = new SpriteBatch(tier, atlas, BlendKind.Opaque, 1024, queueOffset: -10);
            m_Opaque = new SpriteBatch(tier, atlas, StableTranslucentActors ? BlendKind.Translucent : BlendKind.Opaque, world.Table(SvKeys.Enemy).Capacity + world.Table(SvKeys.Gem).Capacity + 64, queueOffset: StableTranslucentActors ? -30 : 0);
            if(NaturalCharacters)
            {
                m_Opaque.Material?.SetFloat(RenderAssets.Ids.ZWrite,1);
                m_Opaque.Material?.SetFloat(Shader.PropertyToID("_Cutoff"),.02f);
            }
            m_Shadows = new SpriteBatch(tier, atlas, BlendKind.Translucent, world.Table(SvKeys.Enemy).Capacity + 8, queueOffset: -60);
            m_Health = new SpriteBatch(tier, atlas, BlendKind.Translucent, world.Table(SvKeys.Enemy).Capacity * 3 + 12, queueOffset: 150);
            if (StableTranslucentActors) m_SortScratch = new NativeArray<PackedSprite>(m_Opaque.Capacity, Allocator.Persistent);
            m_Additive = new SpriteBatch(tier, atlas, StableTranslucentActors ? BlendKind.Translucent : BlendKind.Additive, world.Table(SvKeys.Bullet).Capacity + 1024, queueOffset: 30);
            m_Effects = new SpriteBatch(tier, atlas, BlendKind.Translucent, 768, queueOffset: 60);
            // Preallocate dynamic pages and prefix textures at session bind, before counts grow in play.
            m_Ground.Warmup(m_Ground.Capacity);
            m_Opaque.Warmup(m_Opaque.Capacity);
            m_Additive.Warmup(m_Additive.Capacity);
            m_Effects.Warmup(m_Effects.Capacity);
            m_Shadows.Warmup(m_Shadows.Capacity); m_Health.Warmup(m_Health.Capacity);
            m_Fx = new SpriteEffects(32); m_CombatFx = new CombatVfxPool(96);
            m_LastHitTick = -1; m_EventSequence = 0;
            m_HitVisuals = new NativeArray<HitVisual>(48, Allocator.Persistent);
            m_EnemyColors = new NativeArray<float4>(math.max(config.EnemyKinds, 1), Allocator.Persistent);
            for (int k = 0; k < config.EnemyKinds; k++) m_EnemyColors[k] = new float4(1f);
            m_Counts = new NativeArray<int>(4, Allocator.Persistent);
        }

        void LateUpdate() => RenderFrame();

        /// <summary>Same live presentation path; exposed for calibrated synchronous probes.</summary>
        public void RenderFrame()
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            session.Sync();
            if (session != m_Session || ArtStyle != m_BoundStyle || NaturalCharacters != m_BoundNatural) Bind(session);
            var world = session.World;
            var game = world.Resource(SvKeys.Game);
            if(NaturalCharacters&&world.HasResource(SvWeapons.PoseKey))
            {
                var poseClock=world.Resource(SvWeapons.PoseKey);
                if(!ReferenceEquals(m_PoseClock,poseClock)||m_PoseRevision!=poseClock.Revision)m_Characters?.Clear();
                m_PoseClock=poseClock;m_PoseRevision=poseClock.Revision;
            }
            if(NaturalCharacters&&world.HasResource(SvWeapons.Key))
            {
                var equipped=world.Resource(SvWeapons.Key);
                if(!ReferenceEquals(m_WeaponRuntime,equipped)||m_WeaponRevision!=equipped.Revision)m_Characters?.Clear();
            }
            float alpha = session.State == SessionState.Running && game.Flow == SvFlow.Playing ? session.InterpolationAlpha : 1f;
            float dt = Time.deltaTime;
            float time = Time.time;
            float2 hero = math.lerp(game.HeroPrev, game.Hero, alpha);
            if (Camera != null && Camera.UpdateTarget == null)
                Camera.UpdateTarget = AimCamera;
            float4 view = Camera != null ? Camera.ViewRect : new float4(hero - 20f, hero + 20f);
            float4 effectView = view; // fill budget uses the actual viewport, not the padded culling rectangle
            view += new float4(-2f, -2f, 2f, 2f);
            var bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 100f));

            m_Ground.Clear(); m_Opaque.Clear(); m_Additive.Clear(); m_Effects.Clear(); m_Shadows.Clear(); m_Health.Clear();
            RingsDrawn = 0;
            if (game.Flow == SvFlow.Menu || game.RunTicks < m_PreviousRunTicks)
            { m_Fx.Clear(); m_CombatFx.Clear(); m_LastHitTick = -1; m_EventSequence = 0;
                m_Characters?.Clear(); m_HeroCastTick = -100;
                if(m_Deaths.IsCreated)for(int i=0;i<m_Deaths.Length;i++)m_Deaths[i]=default;
            }
            m_CombatFx.BeginFrame(game.Flow == SvFlow.Playing ? dt : 0f, QualityLevel);
            m_PreviousRunTicks = game.RunTicks;
            DrainFeedback(world);
            if(NaturalCharacters) PrepareCharacters(world,game,hero,alpha,view,session.State==SessionState.Running && game.Flow!=SvFlow.LevelUp && game.Flow!=SvFlow.Menu?dt:0);
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
                float flash = game.Invulnerable > 0f && ((int)(time * 20f) & 1) == 0 ? 0.22f : 0f;
                if (!NaturalCharacters) m_Opaque.Add(hero + new float2(0f, 0.45f), StableTranslucentActors ? new float2(1.3f * side, 1.5f) : new float2(14f * side, 16f) / SvArt.PixelsPerUnit, m_Art.Sheet[frame].Uv, ActorDepth + hero.y * DepthPerY, new float4(1f), 0f, flash);
                int blades = world.Resource(SvKeys.Config).Settings.FlyingSwords.Enabled ? 0 : SvRules.OrbitBlades(game);
                var s = world.Resource(SvKeys.Config).Settings;
                for (int k = 0; k < blades; k++)
                {
                    float a = game.OrbitAngle + k * math.PI * 2f / blades + alpha * tickDt * 3.2f;
                    float2 p = hero + new float2(math.cos(a), math.sin(a)) * s.OrbitRadius;
                    m_Opaque.Add(p, new float2(1f), m_Art.Sheet[m_Art.Blade].Uv, BulletDepth, new float4(1f), time * 12f);
                }
                if (s.Variant == SvVariant.GuardBeacon || StableTranslucentActors)
                {
                    var profile = BlobShadowProfile.Default;
                    BlobShadow.Add(m_Shadows, m_Art.Sheet[m_Art.Shadow].Uv, hero, GroundDepth - 1f, profile);
                    DrawHealth(hero + new float2(0f, 1.55f), 1.1f, game.Hp / math.max(1f, game.MaxHp), new float4(0.43f, 0.86f, 0.45f, 1f));
                }
                if (s.Variant == SvVariant.GuardBeacon)
                {
                    float2 beacon = s.BeaconPosition;
                    var profile = BlobShadowProfile.Default; profile.Size = new float2(2.4f, 0.75f);
                    BlobShadow.Add(m_Shadows, m_Art.Sheet[m_Art.Shadow].Uv, beacon, GroundDepth - 1f, profile);
                    m_Opaque.Add(beacon + new float2(0f, 1.1f), new float2(2.5f, 3f), m_Art.Sheet[m_Art.Beacon].Uv,
                        ActorDepth + beacon.y * DepthPerY, new float4(1f), flash: game.BeaconInvulnerableTicks > s.BeaconHurtCooldownTicks * 0.66f ? 0.22f : 0f);
                    DrawHealth(beacon + new float2(0f, 2.8f), 2f, game.BeaconHp / math.max(1f, game.BeaconMaxHp), new float4(0.62f, 0.92f, 0.36f, 1f));
                }
                if (s.AnnularSkill.Enabled) DrawElectricRings(hero, s.AnnularSkill, time, game.AnnularTicks);
                else
                {
                    float magnet = SvRules.Magnet(s, game);
                    m_Effects.Add(hero, new float2(magnet * 2f), m_Art.Sheet[m_Art.Ring].Uv, GroundDepth - 0.5f, new float4(0.5f, 0.8f, 1f, 0.12f));
                }
            }
            m_Characters?.Evaluate();
            if(world.HasResource(SvWeapons.Key))DrawWeaponProjectiles(world.Resource(SvWeapons.Key),alpha);
            m_Fx.UpdateAndDraw(dt, m_Effects, m_Art.Sheet, m_Art.Font);
            m_CombatFx.Draw(m_Effects, m_Art.CombatFx.Resolve(m_Art.Sheet), effectView, BulletDepth - 0.2f);

            if (StableTranslucentActors)
                new SvSpriteOrder { Sprites = m_Opaque.Instances, Scratch = m_SortScratch, Count = m_Opaque.Count }.Run();
            HealthBarsDrawn = m_Health.Count / 3; ShadowsDrawn = m_Shadows.Count;
            m_Ground.Draw(bounds);
            m_Shadows.Draw(bounds);
            m_Opaque.Draw(bounds);
            m_Characters?.Draw(bounds);
            m_Additive.Draw(bounds);
            m_Effects.Draw(bounds); m_Health.Draw(bounds);
            if(m_WeaponParticles!=null){UpdateWeaponParticles(world,game,alpha,effectView);m_WeaponParticles.EndFrame(bounds);}
            SpritesDrawn = m_Ground.Count + m_Opaque.Count + m_Additive.Count + m_Effects.Count + m_Shadows.Count + m_Health.Count + (m_Characters?.PartsDrawn ?? 0);
            // API payload includes full data textures / indirect arguments, not just live packed sprites.
            BytesUploaded = m_Ground.BytesUploaded + m_Opaque.BytesUploaded + m_Additive.BytesUploaded + m_Effects.BytesUploaded + m_Shadows.BytesUploaded + m_Health.BytesUploaded + (m_Characters?.BytesUploaded ?? 0);
        }

        void UpdateWeaponParticles(SimWorld world,SvGameState game,float alpha,float4 viewRect)
        {
            var weapons=world.Resource(SvWeapons.Key);
            if(!ReferenceEquals(m_WeaponRuntime,weapons)||m_WeaponRevision!=weapons.Revision||weapons.Tick<m_WeaponTick||game.Flow==SvFlow.Menu){m_WeaponParticles.Clear();m_HasWeaponCue=false;System.Array.Clear(m_WeaponTrailValid,0,m_WeaponTrailValid.Length);}
            m_WeaponParticles.BeginFrame(m_Session.State==SessionState.Running&&game.Flow==SvFlow.Playing?Time.deltaTime:0,viewRect);
            var view=weapons.View(alpha);
            if(game.Hp>0&&m_Characters.TryReadWeapon(weapons.Owner,out var socket))m_WeaponParticles.UpdateEmitter(weapons.Owner,view,socket.Tip,socket.Muzzle,socket.Direction,BulletDepth,true);
            for(int i=0;i<weapons.CueCount;i++)
            {
                var cue=weapons.Cues[i];
                if(m_HasWeaponCue&&unchecked((int)(cue.Sequence-m_WeaponCueSequence))<=0)continue;
                if(cue.ActionPulse==view.ActionPulse&&((cue.Kind==WeaponCueKind.Release&&view.Phase<view.ReleasePhase)||(cue.Kind==WeaponCueKind.Impact&&view.Stage==WeaponStage.Windup)))break;
                cue.Position+=new float2(0,cue.Height);cue.Height=0;m_WeaponParticles.SubmitCue(cue,BulletDepth,true);m_WeaponCueSequence=cue.Sequence;m_HasWeaponCue=true;
            }
            for(int i=0;i<weapons.Projectiles.Length;i++)
            {
                var shot=weapons.Projectiles[i];if(!weapons.ProjectileVisible(i,alpha)||weapons.Profile(shot.ContentId).Family!=WeaponActionFamily.Draw){m_WeaponTrailValid[i]=false;continue;}
                float2 point=math.lerp(shot.Previous,shot.Position,alpha)+new float2(0,shot.Height);
                if(m_WeaponTrailValid[i]&&m_WeaponTrailBirths[i]==shot.SpawnTick)m_WeaponParticles.ProjectileTrail(m_WeaponTrailPoints[i],point,BulletDepth,shot.Pulse^(uint)i,true);
                m_WeaponTrailPoints[i]=point;m_WeaponTrailBirths[i]=shot.SpawnTick;m_WeaponTrailValid[i]=true;
            }
            m_WeaponTick=weapons.Tick;m_WeaponRevision=weapons.Revision;m_WeaponRuntime=weapons;
        }

        void DrawWeaponProjectiles(WeaponRuntime weapons,float alpha)
        {
            for(int i=0;i<weapons.Projectiles.Length;i++)
            {
                var shot=weapons.Projectiles[i];if(!weapons.ProjectileVisible(i,alpha))continue;bool arrow=weapons.Profile(shot.ContentId).Family==WeaponActionFamily.Draw;
                float2 point=math.lerp(shot.Previous,shot.Position,alpha)+new float2(0,shot.Height);
                m_Effects.Add(point,arrow?new float2(.5f,.045f):new float2(.23f),m_Art.Sheet[m_Art.White].Uv,BulletDepth,arrow?new float4(1,.85f,.42f,1):new float4(.34f,.80f,1,1),math.atan2(shot.Direction.y,shot.Direction.x));
            }
        }

        void PrepareCharacters(SimWorld world,SvGameState game,float2 hero,float alpha,float4 view,float dt)
        {
            m_Characters.Begin(dt,QualityLevel);ArticulatedEnemies=0;
            int count=world.Table(SvKeys.Enemy).Count;var pos=world.Column(SvKeys.Position);var prev=world.Column(SvKeys.PrevPosition);
            var info=world.Column(SvKeys.Info);var handles=world.Table(SvKeys.Enemy).Handles;
            m_CharacterSelection.Begin(hero,NaturalEnemyBudget);
            for(int i=0;i<count;i++)
            {
                m_NaturalMask[i]=0;float2 p=math.lerp(prev[i],pos[i],alpha);
                if(info[i].Has(EnemyFlags.Dead)||p.x<view.x||p.y<view.y||p.x>view.z||p.y>view.w)continue;
                m_CharacterSelection.Consider(handles[i],p,i);
            }
            for(int n=0;n<m_CharacterSelection.Count;n++)
            {
                int i=m_CharacterSelection.Row(n);var e=info[i];float2 p=math.lerp(prev[i],pos[i],alpha),v=(pos[i]-prev[i])*30f;
                float face=math.abs(v.x)>.02f?(v.x<0?-1:1):(hero.x<p.x?-1:1);
                var state=e.Flash>.01f?GameplayCharacterState.Hit:math.lengthsq(v)>.001f?GameplayCharacterState.Run:GameplayCharacterState.Idle;
                bool accepted=m_Characters.Submit(new GameplayCharacterInput {MotionProfileId=e.Radius>=.65f?2:1,Handle=handles[i],Root=p,Ground=p,Velocity=v,Facing=face,Scale=math.clamp(e.Radius*1.22f,.28f,.85f),
                    State=state,Kind=1,Flash=math.saturate(e.Flash*8),Depth=ActorDepth+p.y*DepthPerY,
                    Tint=e.Has(EnemyFlags.Elite)?new float4(1.2f,.86f,.68f,1):new float4(1f,.93f+(e.Kind%3)*.035f,.85f+(e.Kind%2)*.12f,1)});
                if(accepted){m_NaturalMask[i]=1;ArticulatedEnemies++;}
            }
            if(game.Flow!=SvFlow.Menu)
            {
                float2 velocity=(game.Hero-game.HeroPrev)*30f;int castAge=game.RunTicks-m_HeroCastTick;
                var state=game.Hp<=0?GameplayCharacterState.Death:game.Invulnerable>.01f?GameplayCharacterState.Hit:
                    castAge>=0&&castAge<12?(castAge>6?GameplayCharacterState.Recovery:GameplayCharacterState.Attack):
                    math.lengthsq(velocity)>.001f?GameplayCharacterState.Run:GameplayCharacterState.Idle;
                var weapon=world.HasResource(SvWeapons.Key)?world.Resource(SvWeapons.Key).View(alpha):default;
                if(weapon.Equipped&&game.Hp>0&&weapon.Stage!=WeaponStage.Idle)state=weapon.Stage==WeaponStage.Recovery?GameplayCharacterState.Recovery:GameplayCharacterState.Attack;
                var skill=world.HasResource(SvWeapons.PoseKey)?world.Resource(SvWeapons.PoseKey):null;
                int skillId=skill!=null&&skill.Running?skill.ContentId:0;
                m_Characters.Submit(new GameplayCharacterInput {MotionProfileId=0,SkillPoseId=skillId,SkillPhase=skillId!=0?skill.Phase(alpha):0,SkillPulse=skillId!=0?skill.Timeline.PulseId:0,SkillWeight=skillId!=0?1:0,Weapon=weapon,Handle=new EntityHandle(-1,1),Root=hero,Ground=hero,Velocity=velocity,Facing=game.Facing.x<0?-1:1,
                    Scale=.66f,State=state,Phase=weapon.Equipped?weapon.Phase:math.saturate(castAge/12f),Action=GameplayCharacterAction.Cast,Kind=0,
                    Flash=game.Invulnerable>.01f?.5f:0,Depth=ActorDepth+hero.y*DepthPerY,Tint=new float4(1f)});
            }
            for(int i=0;i<m_Deaths.Length;i++)
            {
                var d=m_Deaths[i];if(d.Life<=0)continue;d.Life=math.max(0,d.Life-dt);m_Deaths[i]=d;
                m_Characters.Submit(new GameplayCharacterInput {Handle=new EntityHandle(-2-i,d.Generation),Root=d.Position,Ground=d.Position,
                    Facing=(i&1)==0?1:-1,Scale=d.Scale,State=GameplayCharacterState.Death,Kind=1,Depth=ActorDepth+d.Position.y*DepthPerY,Tint=new float4(1,1,1,math.saturate(d.Life*4))});
            }
        }
        void AddNaturalDeath(SimWorld world,SvFeedback e)
        {
            // Death events supply position/kind, not an entity handle. These 16 short-lived presentation-only
            // identities are explicitly separate from live actors; no culling disappearance is treated as death.
            for(int i=0;i<m_Deaths.Length;i++)if(m_Deaths[i].Life<=0)
            {
                var config=world.Resource(SvKeys.Config);int kind=math.clamp(e.Enemy-1,0,config.EnemyKinds-1);
                m_Deaths[i]=new DeathVisual {Position=e.Position,Scale=math.clamp(config.Enemies[kind].Radius*1.22f,.28f,.85f),Life=.55f,Generation=++m_DeathSequence};return;
            }
        }

        /// <summary>Camera callback (runs before this renderer): follow the interpolated hero.</summary>
        void AimCamera(FollowCamera2D camera)
        {
            var session = Host != null ? Host.Session : null;
            if (session == null) return;
            var game = session.World.Resource(SvKeys.Game);
            float2 hero = math.lerp(game.HeroPrev, game.Hero, session.InterpolationAlpha);
            var s = session.World.Resource(SvKeys.Config).Settings;
            camera.Target = s.Variant == SvVariant.GuardBeacon ? math.lerp(hero, s.BeaconPosition, 0.2f) : hero;
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
            bool decorate = StableTranslucentActors || world.Resource(SvKeys.Config).Settings.Variant == SvVariant.GuardBeacon;
            var shadows = m_Shadows.Reserve(decorate ? math.min(count, ShadowBudget) : 0);
            var bars = m_Health.Reserve(decorate ? count * 3 : 0);
            int hitTick = world.Resource(SvKeys.Game).RunTicks / 3;
            bool emitHits = hitTick != m_LastHitTick; m_LastHitTick = hitTick;
            new EnemyDrawJob
            {
                Position = world.Column(SvKeys.Position), Prev = world.Column(SvKeys.PrevPosition), Info = world.Column(SvKeys.Info),
                Uv = m_Art.EnemyUv, Out = slots, Written = m_Counts, Alpha = alpha, View = view, Time = time,
                Count = math.min(count, slots.Length), Shadows = shadows, Bars = bars,
                Hits = m_HitVisuals, Handles = world.Table(SvKeys.Enemy).Handles, EmitHits = emitHits,
                NaturalMask = m_NaturalMask,
                ShadowUv = m_Art.Sheet[m_Art.Shadow].Uv, WhiteUv = m_Art.Sheet[m_Art.White].Uv, Smooth = StableTranslucentActors,
            }.Run();
            m_Opaque.Trim(start + m_Counts[0]); m_Shadows.Trim(m_Counts[1]); m_Health.Trim(m_Counts[2]);
            for (int i = 0; i < m_Counts[3]; i++)
            {
                var hit = m_HitVisuals[i];
                m_CombatFx.Emit(ImpactProfile, hit.Position, ((ulong)(uint)(hitTick + 1) << 32) | hit.Identity);
            }
            return m_Counts[0] + ArticulatedEnemies;
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
                Uv = m_Art.BulletUv, Out = slots, Written = m_Counts, Back = (1f - alpha) * tickDt, View = view, Count = math.min(count, slots.Length), Smooth = StableTranslucentActors,
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
            public NativeArray<PackedSprite> Shadows, Bars;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            [ReadOnly] public NativeArray<byte> NaturalMask;
            public NativeArray<HitVisual> Hits;
            public bool EmitHits;
            public float4 ShadowUv, WhiteUv;
            public bool Smooth;
            public float Alpha, Time;
            public float4 View;
            public int Count;

            public void Execute()
            {
                int n = 0, shadows = 0, bars = 0, hits = 0;
                for (int i = 0; i < Count; i++)
                {
                    float2 p = math.lerp(Prev[i], Position[i], Alpha);
                    if (p.x < View.x || p.y < View.y || p.x > View.z || p.y > View.w) continue;
                    var info = Info[i];
                    if (info.Has(EnemyFlags.Dead)) continue;
                    bool moving = math.distancesq(Position[i], Prev[i]) > 0.00001f;
                    var handle = Handles[i];
                    int frame = SvVisualMotion.Frame(Time, handle, moving, Smooth);
                    float size = info.Radius * 2.6f;
                    float side = Position[i].x < Prev[i].x ? -1f : 1f;
                    float4 tint = info.Has(EnemyFlags.Elite) ? new float4(1.3f, 0.9f, 0.6f, 1f) : new float4(1f);
                    float hit = math.saturate(info.Flash * 8f);
                    float squash = Smooth ? hit * 0.04f : 0f;
                    if (EmitHits && hit > 0f && hits < Hits.Length)
                    {
                        Hits[hits++] = new HitVisual { Position = p + new float2(0f,size * 0.45f), Identity = (uint)handle.Index * 397u ^ (uint)handle.Generation };
                    }
                    if (shadows < Shadows.Length)
                        Shadows[shadows++] = PackedSprite.Pack(p, new float2(size, size * 0.35f), ShadowUv, GroundDepth - 1f, new float4(0f, 0f, 0f, 0.28f));
                    if (bars + 3 <= Bars.Length)
                    {
                        float2 bar = p + new float2(0f, size * 0.96f);
                        float width = math.max(0.55f, size * 0.8f), fill = math.saturate(info.Hp / math.max(1f, info.MaxHp));
                        Bars[bars++] = PackedSprite.Pack(bar, new float2(width + 0.07f, 0.14f), WhiteUv, 0.1f, new float4(0.16f, 0.17f, 0.16f, 1f));
                        Bars[bars++] = PackedSprite.Pack(bar, new float2(width, 0.075f), WhiteUv, 0.09f, new float4(0.32f, 0.3f, 0.25f, 1f));
                        Bars[bars++] = PackedSprite.Pack(bar + new float2(-width * (1f - fill) * 0.5f, 0f), new float2(width * fill, 0.075f), WhiteUv, 0.08f, new float4(0.91f, 0.42f, 0.4f, 1f));
                    }
                    if (!NaturalMask.IsCreated || NaturalMask[i] == 0) Out[n++] = PackedSprite.Pack(p + new float2(0f, size * 0.4f), new float2(size * side * (1f + squash), size * (1f - squash)), Uv[(info.Kind - 1) * 2 + frame],
                        ActorDepth + p.y * DepthPerY, tint, Smooth ? SvVisualMotion.Wobble(Time, handle, moving) : 0f, hit * (Smooth ? 0.22f : 1f));
                }
                Written[0] = n; Written[1] = shadows; Written[2] = bars; Written[3] = hits;
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
            public bool Smooth;
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
                    float size = b.Radius * (b.Team == BulletTeam.Enemy ? (Smooth ? 2.4f : 2.8f) : (Smooth ? 2.2f : 3.2f));
                    float rotation = math.atan2(b.Velocity.y, b.Velocity.x);
                    Out[n++] = PackedSprite.Pack(p, new float2(Smooth && b.Visual == BulletVisual.Bolt ? size * 2.2f : size, size), Uv[(int)b.Visual], BulletDepth, new float4(1f), rotation);
                }
                Written[0] = n;
            }
        }

        void DrawHealth(float2 position, float width, float fraction, float4 color)
        {
            fraction = math.saturate(fraction); var uv = m_Art.Sheet[m_Art.White].Uv;
            m_Health.Add(position, new float2(width + 0.1f, 0.2f), uv, 0.1f, new float4(0.12f, 0.15f, 0.15f, 1f));
            m_Health.Add(position, new float2(width, 0.12f), uv, 0.09f, new float4(0.35f, 0.3f, 0.24f, 1f));
            m_Health.Add(position + new float2(-width * (1f - fraction) * 0.5f, 0f), new float2(width * fraction, 0.12f), uv, 0.08f, color);
        }

        void DrawElectricRings(float2 hero, SvAnnularSkill skill, float time, int pulseTick)
        {
            int segments = QualityLevel >= 2 ? 32 : 64;
            var uv = m_Art.Sheet[m_Art.White].Uv;
            for (int band = 0; band < 2; band++)
            {
                float radius = band == 0 ? skill.RadiusA : skill.RadiusB;
                if (radius <= 0f) continue;
                RingsDrawn++;
                for (int k = 0; k < segments; k++)
                {
                    float a = k * math.PI * 2f / segments, b = (k + 1) * math.PI * 2f / segments;
                    // Tiny visual jitter stays inside the configured damage band.
                    float ja = math.sin(a * 9f + time * 8f) * math.min(0.07f, skill.HalfWidth * 0.3f);
                    float jb = math.sin(b * 9f + time * 8f) * math.min(0.07f, skill.HalfWidth * 0.3f);
                    float2 from = hero + new float2(math.cos(a), math.sin(a)) * (radius + ja);
                    float2 to = hero + new float2(math.cos(b), math.sin(b)) * (radius + jb);
                    float2 d = to - from; float length = math.length(d), angle = math.atan2(d.y, d.x);
                    float pulse = pulseTick == 0 ? 1.2f : 1f;
                    m_Effects.Add((from + to) * 0.5f, new float2(length + 0.03f, 0.09f * pulse), uv, 0.4f, new float4(0.08f, 0.68f, 0.98f, 0.52f), angle);
                    m_Additive.Add((from + to) * 0.5f, new float2(length + 0.02f, 0.022f * pulse), uv, 0.3f, new float4(0.58f, 0.95f, 1f, 1f), angle);
                }
            }
        }

        void DrainFeedback(SimWorld world)
        {
            var feedback = world.Resource(SvKeys.Feedback);

            for (int i = 0; i < feedback.Count; i++)
            {
                var e = feedback[i];
                Feedback?.Invoke(e);
                ulong key = 0xf000000000000000ul | ++m_EventSequence;
                switch (e.Kind)
                {
                    case SvFeedbackKind.Death:
                        if (NaturalCharacters) AddNaturalDeath(world, e);
                        m_CombatFx.Emit(DeathProfile, e.Position, key, math.clamp(e.Value, 0.5f, 1.5f));
                        break;
                    case SvFeedbackKind.Hit:
                        m_CombatFx.Emit(ImpactProfile, e.Position, key);
                        break;
                    case SvFeedbackKind.Shoot:
                        if (NaturalCharacters) m_HeroCastTick = world.Resource(SvKeys.Game).RunTicks;
                        break;
                    case SvFeedbackKind.Nova:
                        if (NaturalCharacters) m_HeroCastTick = world.Resource(SvKeys.Game).RunTicks;
                        m_CombatFx.Emit(PulseProfile, e.Position, key);
                        break;
                    case SvFeedbackKind.LevelUp:
                        m_Fx.Spawn(new SpriteEffects.Effect { Clip = new SpriteClip(m_Art.Ring, 1, 1f, false), Position = e.Position, Size = new float2(6f), Life = 0.6f, ScaleFrom = 0.2f, ScaleTo = 1.2f, Fade = true, Color = new float4(1f, 0.9f, 0.4f, 0.9f), Depth = ActorDepth - 0.2f });
                        break;
                    case SvFeedbackKind.HeroHurt:
                        Camera?.Shake(0.06f, 0.10f);
                        m_CombatFx.Emit(VfxProfile.HeroHurt, e.Position, key);
                        m_Fx.Spawn(new SpriteEffects.Effect { NumberMode = true, Number = (int)math.round(e.Value), Prefix = '-', Position = e.Position + new float2(0f, 1f), Velocity = new float2(0f, 1.2f), Size = new float2(0.28f), Life = 0.7f, Fade = true, Color = new float4(1f, 0.35f, 0.3f, 1f), Depth = ActorDepth - 0.3f });
                        break;
                }
            }
            feedback.Clear();
        }
    }
}
