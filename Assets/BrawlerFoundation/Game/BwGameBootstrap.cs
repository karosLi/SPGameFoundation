using BrawlerFoundation.Presentation;
using SPF.Contracts;
using SPF.Presentation.Audio;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using SPF.Shell.Input;
using SPF.Shell.Performance;
using SPF.Shell.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BrawlerFoundation.Game
{
    /// <summary>
    /// The brawler: a stick on the left, PUNCH and KICK on the right (J / K and the arrows on a keyboard), three waves,
    /// skeletal fighters, an allocation-free HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BwGameBootstrap : MonoBehaviour
    {
        [SerializeField] bool m_CreateUI = true;
        [SerializeField, Tooltip("Use bounded stable-handle combat histories (64 fighters). Classic saves use a different layout.")]
        bool m_SharedCombat;
        [SerializeField] bool m_MobileCombat;
        [SerializeField] bool m_BeltScroller;
        [SerializeField] bool m_WeaponCombat;
        [SerializeField] bool m_NaturalCharacters;

        ModeDefinition m_Mode;
        GameplayModuleAsset m_Module;
        SoundPlayer m_Sound;
        int m_Swing, m_Hit, m_Ko, m_Wave, m_Lose;
        bool m_ScriptedInputOwner;
        BwFlow m_Flow = (BwFlow)255;

        public SessionHost Host { get; private set; }
        public FrameGovernor Governor { get; private set; }
        public FollowCamera2D CameraRig { get; private set; }
        public BwRenderer Renderer { get; private set; }
        public BwCollisionDebugOverlay CollisionOverlay { get; private set; }
        public InputRouter InputRouter { get; private set; }
        public SimSession Session => Host != null ? Host.Session : null;
        public bool SharedCombatEnabled => m_SharedCombat || m_MobileCombat || m_BeltScroller;
        public bool BeltScrollerEnabled => m_BeltScroller;
        public BwGameState State => Session?.World.Resource(BwKeys.Game);

        public BufferText StatsText { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform EndPanel { get; private set; }
        public Text EndText { get; private set; }
        public Button StartButton { get; private set; }
        public Button AgainButton { get; private set; }
        public TapButton PunchButton { get; private set; }
        public TapButton KickButton { get; private set; }
        public VirtualJoystick Joystick { get; private set; }
        public MobileCombatHud MobileHud { get; private set; }
        public Button SwitchWeaponButton { get; private set; }

        /// <summary>Scripted input for tests and demos (replaces the stick; buttons add up).</summary>
        public System.Func<InputFrame> Script { get; set; }

        public static BwGameBootstrap Create(bool ui = true, bool sharedCombat = false, bool mobileCombat = false, bool beltScroller = false, bool naturalCharacters = false, bool weaponCombat = false)
        {
            var go = new GameObject("BrawlerGame");
            go.SetActive(false);
            var game = go.AddComponent<BwGameBootstrap>();
            game.m_CreateUI = ui;
            game.m_SharedCombat = sharedCombat || mobileCombat || beltScroller;
            game.m_MobileCombat = mobileCombat || beltScroller;
            game.m_WeaponCombat = weaponCombat; beltScroller |= weaponCombat;
            game.m_BeltScroller = beltScroller; game.m_NaturalCharacters = naturalCharacters || beltScroller;
            go.SetActive(true);
            return game;
        }

        /// <summary>Playable stable-hit example at the renderer's existing 64-fighter bound.</summary>
        public static BwGameBootstrap CreateSharedCombat(bool ui = true) => Create(ui, sharedCombat: true);

        public static BwGameBootstrap CreateMobileCombat(bool ui = true) => Create(ui, mobileCombat: true);

        public static BwGameBootstrap CreateNaturalCombat(bool ui = true) => Create(ui, mobileCombat: true, naturalCharacters: true);

        public static BwGameBootstrap CreateBeltScroller(bool ui = true) => Create(ui, beltScroller: true);

        public static BwGameBootstrap CreateWeaponBelt(bool ui = true) => Create(ui, beltScroller: true, weaponCombat: true);

        void Awake()
        {
            Governor = gameObject.AddComponent<FrameGovernor>();
            Governor.SetFrameRates(60, 30);
            if (m_WeaponCombat) m_BeltScroller = true;
            if (m_BeltScroller) { m_MobileCombat = true; m_NaturalCharacters = true; }
            m_Mode = m_WeaponCombat ? BwMode.CreateWeaponBelt(BwBeltConfig.Default, out m_Module) : m_BeltScroller ? BwMode.CreateBeltScroller(BwBeltConfig.Default, out m_Module) : m_MobileCombat ? BwMode.CreateMobileCombat(out m_Module) : m_SharedCombat
                ? BwMode.CreateSharedCombat(BwSharedCombatConfig.Default, out m_Module)
                : BwMode.Create(out m_Module);
            var sim = new GameObject("Simulation");
            sim.transform.SetParent(transform, false);
            Host = sim.AddComponent<SessionHost>();
            Host.Initialize(m_Mode, 1);

            var cameraObject = UnityEngine.Camera.main != null ? UnityEngine.Camera.main.gameObject : new GameObject("Main Camera", typeof(UnityEngine.Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.2f, 0.18f, 0.24f);
            CameraRig = cameraObject.GetComponent<FollowCamera2D>();
            if (CameraRig == null) CameraRig = cameraObject.AddComponent<FollowCamera2D>();
            CameraRig.Follow = 20f;
            if (cameraObject.GetComponent<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();

            var view = new GameObject("BwRenderer");
            view.transform.SetParent(transform, false);
            Renderer = view.AddComponent<BwRenderer>();
            if (m_WeaponCombat) { CollisionOverlay = view.AddComponent<BwCollisionDebugOverlay>(); CollisionOverlay.Host = Host; }
            Renderer.Host = Host;
            Renderer.NaturalCharacters = m_NaturalCharacters;
            Renderer.SetQualityLevel(Governor.Level);
            Governor.LevelChanged += Renderer.SetQualityLevel;
            Renderer.Camera = CameraRig;
            Renderer.Feedback += OnFeedback;

            m_Sound = SoundPlayer.Create(transform, 8);
            m_Swing = m_Sound.Register("swing", SfxDef.Create(SfxWave.Noise, 1500f, 600f, 0.08f, 0.25f).WithNoise(1f, 0.2f), maxVoices: 2);
            m_Hit = m_Sound.Register("hit", SfxDef.Create(SfxWave.Noise, 700f, 120f, 0.12f, 0.55f).WithNoise(1f, 0.7f), maxVoices: 3, minInterval: 0.04f);
            m_Ko = m_Sound.Register("ko", SfxDef.Create(SfxWave.Triangle, 500f, 70f, 0.5f, 0.5f), maxVoices: 2);
            m_Wave = m_Sound.Register("wave", SfxDef.Create(SfxWave.Square, 392f, 784f, 0.4f, 0.35f).WithDuty(0.25f), maxVoices: 1, priority: 2);
            m_Lose = m_Sound.Register("lose", SfxDef.Create(SfxWave.Triangle, 300f, 60f, 0.9f, 0.45f), maxVoices: 1, priority: 3);

            InputRouter = gameObject.AddComponent<InputRouter>();
            InputRouter.Sink = frame =>
            {
                var state = State;
                if (state == null) return;
                if (Script != null)
                {
                    var scripted = Script();
                    scripted.Held |= frame.Held;
                    scripted.Pressed |= frame.Pressed;
                    frame = scripted;
                }
                if (m_MobileCombat)
                {
                    bool scriptedOwner = InputRouter.Scripted.Active;
                    if (scriptedOwner != m_ScriptedInputOwner)
                    { MobileHud?.CancelInput(); state.Input = default; m_ScriptedInputOwner = scriptedOwner; }
                    if (Session.State != SessionState.Running || !MovementPhase(state.Flow)) { state.Input = default; return; }
                    // The belt collection interval keeps locomotion, but never latches an attack,
                    // jump or heal for the next wave. Skill cooldowns remain simulation-paused.
                    state.Input = state.Flow == BwFlow.WaveClear ? new InputFrame { Move = frame.Move } : InputFrame.Latch(state.Input, frame);
                }
                else state.Input = InputFrame.Latch(state.Input, frame);
            };
            if (m_CreateUI) BuildUi();
            var keyboard = new KeyboardInputSource().Map(KeyCode.J, BwButton.Punch).Map(KeyCode.K, BwButton.Kick);
            if (m_BeltScroller) keyboard.Map(KeyCode.Space, BwBeltRules.JumpButton).Map(KeyCode.L, BwBeltRules.HealButton);
            if (m_WeaponCombat) keyboard.Map(KeyCode.Q, BwWeapons.SwitchButton);
            InputRouter.AddSource(keyboard);
        }

        bool MovementPhase(BwFlow flow) => flow == BwFlow.Fighting || (m_BeltScroller && flow == BwFlow.WaveClear);

        void BuildUi()
        {
            var canvas = UIFactory.CreateCanvas(transform, "BrawlerUI");
            var root = canvas.transform;
            if (m_MobileCombat)
            {
                MobileHud = gameObject.AddComponent<MobileCombatHud>();
                MobileHud.Build(root, new MobileSource(this), preferLandscape: true);
                MobileHud.Interrupted = () => { if (State != null) State.Input = default; };
                MobileHud.SkillCanceled = slot => { if (State != null) State.Input = SkillInput.CancelSlot(State.Input, slot); };
                Joystick = MobileHud.Joystick;
                InputRouter.AddSource(MobileHud.Input);
                root = MobileHud.SafeRoot;
            }
            if (m_WeaponCombat)
            {
                SwitchWeaponButton = UIFactory.Button(root, "SwitchWeapon", "SWITCH / Q", new Vector2(-112, -55), new Vector2(188, 60), SanctuaryUiTheme.Surface, new Vector2(1,1), 20);
                SwitchWeaponButton.onClick.AddListener(() => { if (Session.State == SessionState.Running && State.Flow == BwFlow.Fighting && !InputRouter.Scripted.Active) State.Input.Pressed |= 1u << BwWeapons.SwitchButton; });
            }
            if(m_MobileCombat)
            {
                var card=UIFactory.Card(root,"CombatStatsBackdrop",SanctuaryUiTheme.Ink,new Vector2(.015f,.985f),new Vector2(.50f,.985f),false);
                card.offsetMin=new Vector2(0,-115);card.offsetMax=Vector2.zero;
            }
            StatsText = BufferText.Create(root, "Stats", m_MobileCombat?25:40, TextAnchor.UpperLeft, new Vector2(0.025f, 0.79f), new Vector2(m_MobileCombat?.50f:.7f, 0.973f));
            if (!m_MobileCombat)
            {
                var stick = UIFactory.Panel(root, "Joystick", new Color(1f, 1f, 1f, 0.03f), Vector2.zero, new Vector2(0.45f, 0.6f));
                Joystick = stick.gameObject.AddComponent<VirtualJoystick>();
                var punch = UIFactory.Button(root, "PunchButton", "PUNCH", new Vector2(-420, 200), new Vector2(240, 240), new Color(0.9f, 0.45f, 0.3f, 0.55f), new Vector2(1f, 0f));
                PunchButton = punch.gameObject.AddComponent<TapButton>();
                var kick = UIFactory.Button(root, "KickButton", "KICK", new Vector2(-160, 300), new Vector2(220, 220), new Color(0.3f, 0.55f, 0.9f, 0.55f), new Vector2(1f, 0f));
                KickButton = kick.gameObject.AddComponent<TapButton>();
                InputRouter.AddSource(new TouchInputSource(Joystick).Tap(PunchButton, BwButton.Punch).Tap(KickButton, BwButton.Kick));

            }

            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(.025f, .07f, .075f, .84f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", m_WeaponCombat ? "WEAPONS / BRAWL" : m_BeltScroller ? "BELT / BRAWL" : m_MobileCombat ? "BRAWL / MOBILE" : "BRAWL", m_MobileCombat ? 60 : 150, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            StartButton = UIFactory.Button(MenuPanel, "StartButton", "FIGHT", new Vector2(0, -20), new Vector2(460, 140), SanctuaryUiTheme.Coral, new Vector2(0.5f, 0.5f));
            StartButton.onClick.AddListener(() => State?.Send(BwCommandKind.Start));

            EndPanel = UIFactory.Panel(root, "EndPanel", new Color(.025f, .07f, .075f, .84f), Vector2.zero, Vector2.one);
            EndText = UIFactory.Label(EndPanel, "EndText", "", 110, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            AgainButton = UIFactory.Button(EndPanel, "AgainButton", "AGAIN", new Vector2(0, -20), new Vector2(460, 140), SanctuaryUiTheme.Coral, new Vector2(0.5f, 0.5f));
            AgainButton.onClick.AddListener(() => State?.Send(BwCommandKind.Start));
        }

        void Update()
        {
            var state = State;
            if (state == null || StatsText == null) return;
            int hp = 0;
            Session.Sync();
            var world = Session.World;
            var info = world.Column(BwKeys.Info);
            for (int i = 0; i < world.Table(BwKeys.Fighter).Count; i++) if (info[i].Team == 0) { hp = (int)info[i].Hp; break; }
            var stats = StatsText.Begin().Append("Wave ").Append(state.Wave).Append('/').Append(BwRules.Waves).Append("   KO ").Append(state.Kos)
                .Append("\nHP ").Append(hp).Append("   Score ").Append(state.Score);
            if (m_BeltScroller) stats.Append("   Coins ").Append(world.Resource(BwBeltKeys.State).Coins);
            if (m_WeaponCombat)
            {
                var weapons = world.Resource(BwWeapons.Key); stats.Append("\nWEAPON ").Append(weapons.Current.Name);
                if (weapons.Equipment.PendingId != 0) stats.Append(" > ").Append(weapons.Profile(weapons.Equipment.PendingId).Name);
                SwitchWeaponButton.interactable = Session.State == SessionState.Running && state.Flow == BwFlow.Fighting && hp > 0;
            }
            StatsText.Commit();
            if (state.Flow == m_Flow) return;
            m_Flow = state.Flow;
            if (m_BeltScroller && state.Flow == BwFlow.WaveClear && MobileHud != null)
                foreach (var button in MobileHud.Buttons) button.CancelInput(); // keep the independent movement pointer
            MenuPanel.gameObject.SetActive(state.Flow == BwFlow.Menu);
            bool end = state.Flow == BwFlow.Won || state.Flow == BwFlow.Lost;
            EndPanel.gameObject.SetActive(end);
            if (end) EndText.text = state.Flow == BwFlow.Won ? "VICTORY" : "DOWN AND OUT";
        }

        sealed class MobileSource : IMobileCombatHudSource, IMobileCombatHudGlyphSource
        {
            readonly BwGameBootstrap m_Game;
            public MobileSource(BwGameBootstrap game) => m_Game = game;
            public int SlotCount => m_Game.m_BeltScroller ? 4 : 2;
            public int TickRate => 60;
            public bool Playing => m_Game.Session != null && m_Game.Session.State == SessionState.Running && m_Game.MovementPhase(m_Game.State.Flow) && !m_Game.InputRouter.Scripted.Active;
            public string SlotLabel(int slot) => slot == 0 ? (m_Game.m_WeaponCombat ? "ATTACK / J" : m_Game.m_BeltScroller ? "COMBO / J" : "PUNCH / J") : slot == 1 ? "KICK / K" : slot == 2 ? "JUMP / SPACE" : "HEAL / L";
            public SkillSlotSnapshot ReadSlot(int slot)
            {
                m_Game.Session.Sync();
                var world = m_Game.Session.World;
                return world.Resource(BwMobileSkills.Key).GetSnapshot(slot, Playing && (m_Game.m_BeltScroller ? BwBeltRules.CanUseSlot(world, slot) : BwMobileSkills.CanAct(world)));
            }
            public int ReadFallbackGlyph(int slot, in SkillSlotSnapshot snapshot)
            {
                if (slot == 0 && m_Game.m_WeaponCombat) return CombatControlGraphic.WeaponGlyph(m_Game.Session.World.Resource(BwWeapons.Key).Current.Family);
                if (slot == 2 && m_Game.m_BeltScroller) return CombatControlGraphic.JumpGlyph;
                if (slot == 3 && m_Game.m_BeltScroller) return CombatControlGraphic.HealGlyph;
                return snapshot.Definition.IconId;
            }
        }

        void OnFeedback(BwFeedback e)
        {
            switch (e.Kind)
            {
                case BwFeedbackKind.Swing: m_Sound.Play(m_Swing); break;
                case BwFeedbackKind.Hit: m_Sound.Play(m_Hit); break;
                case BwFeedbackKind.KO: m_Sound.Play(m_Ko); break;
                case BwFeedbackKind.Wave: m_Sound.Play(m_Wave); break;
                case BwFeedbackKind.Lose: m_Sound.Play(m_Lose); break;
            }
        }

        void OnDestroy()
        {
            if (Governor != null && Renderer != null) Governor.LevelChanged -= Renderer.SetQualityLevel;
            if (m_Module != null) Destroy(m_Module);
            if (m_Mode != null) Destroy(m_Mode);
        }
    }
}
