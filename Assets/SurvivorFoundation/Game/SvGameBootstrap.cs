using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using SPF.Shell.Input;
using SPF.Shell.Performance;
using SurvivorFoundation.Presentation;
using UnityEngine;

namespace SurvivorFoundation.Game
{
    /// <summary>Assembles the survivor game from foundation pieces (session host, camera, input, renderer, HUD, sound).</summary>
    [DisallowMultipleComponent]
    public sealed class SvGameBootstrap : MonoBehaviour
    {
        [SerializeField] SvConfig m_Config;
        [SerializeField] bool m_GuardExample;
        [SerializeField] SvArtStyle m_ArtStyle = SvArtStyle.Pixel;
        [SerializeField] uint m_Seed = 1;
        [SerializeField] bool m_CreateUI = true;
        [SerializeField] bool m_PerfHud = true;
        [SerializeField] int m_TargetFrameRate = 60;

        bool m_OwnsConfig;
        ModeDefinition m_Mode;
        GameplayModuleAsset m_Module;
        SvBot m_Bot;

        public SessionHost Host { get; private set; }
        public FrameGovernor Governor { get; private set; }
        public FollowCamera2D CameraRig { get; private set; }
        public SvRenderer Renderer { get; private set; }
        public SvHud Hud { get; private set; }
        public SvAudio Audio { get; private set; }
        public InputRouter InputRouter { get; private set; }
        public SimSession Session => Host != null ? Host.Session : null;
        public SvGameState State => Session?.World.Resource(SvKeys.Game);

        /// <summary>Lets the built-in bot play (attract mode, benchmarks, smoke tests).</summary>
        public bool AutoPlay { get; set; }

        public static SvGameBootstrap Create(SvConfig config = null, uint seed = 1, bool ui = true, bool perfHud = false, SvArtStyle artStyle = SvArtStyle.Pixel)
        {
            var go = new GameObject("SurvivorGame");
            go.SetActive(false);
            var game = go.AddComponent<SvGameBootstrap>();
            game.m_Config = config;
            game.m_ArtStyle = artStyle;
            game.m_Seed = seed;
            game.m_CreateUI = ui;
            game.m_PerfHud = perfHud;
            go.SetActive(true);
            return game;
        }

        /// <summary>Runnable original guard example, with the smooth style selected separately from simulation.</summary>
        public static SvGameBootstrap CreateGuardExample(SvConfig config = null, uint seed = 1, bool ui = true)
        {
            bool owns = config == null;
            if (owns) config = SvConfig.CreateGuardExample();
            var game = Create(config, seed, ui, artStyle: SvArtStyle.SmoothOutline);
            game.m_OwnsConfig = owns;
            return game;
        }

        /// <summary>Playable periodic crossed-path simulation with the existing Survivor renderer.
        /// Enemies, health and hit feedback render normally; this stage does not add blade-path art.</summary>
        public static SvGameBootstrap CreateCrossedBladeExample(SvConfig config = null, uint seed = 1, bool ui = true)
        {
            bool owns = config == null;
            if (owns) config = SvConfig.CreateCrossedBladeExample();
            var game = Create(config, seed, ui);
            game.m_OwnsConfig = owns;
            return game;
        }

        void Awake()
        {
            Governor = gameObject.AddComponent<FrameGovernor>();
            Governor.ThrottleWhenIdle = false;
            Governor.SetFrameRates(m_TargetFrameRate, 30);
            if (m_Config == null) { m_Config = m_GuardExample ? SvConfig.CreateGuardExample() : SvConfig.CreateDefault(); m_OwnsConfig = true; }
            if (m_GuardExample) m_ArtStyle = SvArtStyle.SmoothOutline;
            m_Mode = SvMode.Create(m_Config, out m_Module);

            var sim = new GameObject("Simulation");
            sim.transform.SetParent(transform, false);
            Host = sim.AddComponent<SessionHost>();
            Host.Initialize(m_Mode, m_Seed);

            var cameraObject = UnityEngine.Camera.main != null ? UnityEngine.Camera.main.gameObject : new GameObject("Main Camera", typeof(UnityEngine.Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.06f);
            CameraRig = cameraObject.GetComponent<FollowCamera2D>();
            if (CameraRig == null) CameraRig = cameraObject.AddComponent<FollowCamera2D>();
            CameraRig.Size = m_Config.Settings.Variant == SvVariant.GuardBeacon ? 10f : 9f;
            if (cameraObject.GetComponent<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();

            var view = new GameObject("SvRenderer");
            view.transform.SetParent(transform, false);
            Renderer = view.AddComponent<SvRenderer>();
            Renderer.Host = Host;
            Renderer.Camera = CameraRig;
            Renderer.ArtStyle = m_ArtStyle;
            Renderer.SetQualityLevel(Governor.Level);
            Governor.LevelChanged += Renderer.SetQualityLevel;
            Audio = SvAudio.Create(transform, Renderer);

            InputRouter = gameObject.AddComponent<InputRouter>();
            InputRouter.Sink = frame =>
            {
                var state = State;
                if (state == null) return;
                if (AutoPlay && Session != null)
                {
                    m_Bot ??= new SvBot();
                    Session.Sync();   // ticks overlap rendering: finish the in-flight one before reading
                    frame = m_Bot.Think(Session.World);
                }
                state.Input = InputFrame.Latch(state.Input, frame);
            };
            if (m_CreateUI)
            {
                Hud = gameObject.AddComponent<SvHud>();
                Hud.Build(this);
                InputRouter.AddSource(Hud.TouchInput);
            }
            InputRouter.AddSource(new KeyboardInputSource());
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (m_PerfHud) gameObject.AddComponent<SPF.Runtime.Diagnostics.PerfHud>().Host = Host;
#endif
        }

        void OnDestroy()
        {
            if (Governor != null && Renderer != null) Governor.LevelChanged -= Renderer.SetQualityLevel;
            if (m_Module != null) Destroy(m_Module);
            if (m_Mode != null) Destroy(m_Mode);
            if (m_OwnsConfig && m_Config != null) Destroy(m_Config);
        }

        public void StartRun() { State?.Send(SvCommandKind.Start); CameraRig.Snap(); }
        public void Choose(int index) => State?.Send(SvCommandKind.Choose, index);
        public void BackToMenu() => State?.Send(SvCommandKind.Menu);
    }
}
