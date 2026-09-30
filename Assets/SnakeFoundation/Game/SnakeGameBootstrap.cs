using SnakeFoundation.Game.UI;
using SnakeFoundation.Presentation;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using UnityEngine;

namespace SnakeFoundation.Game
{
    /// <summary>
    /// One component that assembles the whole game at runtime: simulation host, camera rig, world
    /// renderer, input, UI, adaptive quality and (development builds) the perf HUD. A scene only needs
    /// a GameObject with this component; tests create it with <see cref="Create"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnakeGameBootstrap : MonoBehaviour
    {
        [SerializeField] SnakeConfig m_Config;
        [SerializeField] bool m_RandomSeed = true;
        [SerializeField] uint m_Seed = 1;
        [SerializeField] bool m_CreateUI = true;
        [SerializeField] bool m_PerfHud = true;
        [SerializeField] int m_TargetFrameRate = 60;

        bool m_OwnsConfig;
        ModeDefinition m_Mode;
        SnakeGameModule m_Module;

        public SessionHost Host { get; private set; }
        public SnakeCameraRig CameraRig { get; private set; }
        public SnakeWorldRenderer WorldRenderer { get; private set; }
        public InputRouter InputRouter { get; private set; }
        public SnakeHud Hud { get; private set; }
        public AdaptiveQualityController Quality { get; private set; }
        public SnakeConfig Config => m_Config;
        public SimSession Session => Host != null ? Host.Session : null;
        public SnakeGameState State => Session?.World.Resource(SnakeKeys.Game);

        /// <summary>Creates a fully wired game object (configuration applied before Awake).</summary>
        public static SnakeGameBootstrap Create(SnakeConfig config = null, uint? seed = null, bool ui = true, bool perfHud = false)
        {
            var go = new GameObject("SnakeGame");
            go.SetActive(false);
            var game = go.AddComponent<SnakeGameBootstrap>();
            game.m_Config = config;
            game.m_CreateUI = ui;
            game.m_PerfHud = perfHud;
            if (seed.HasValue)
            {
                game.m_RandomSeed = false;
                game.m_Seed = seed.Value;
            }
            go.SetActive(true);
            return game;
        }

        void Awake()
        {
            Application.targetFrameRate = m_TargetFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            if (m_Config == null)
            {
                m_Config = SnakeConfig.CreateDefault();
                m_OwnsConfig = true;
            }
            m_Module = SnakeGameModule.Create(m_Config);
            m_Mode = ModeDefinition.Create(new GameplayModuleAsset[] { m_Module }, SessionSettings.Default);
            uint seed = m_RandomSeed ? (uint)System.Environment.TickCount | 1u : m_Seed;

            var sim = new GameObject("Simulation");
            sim.transform.SetParent(transform, false);
            Host = sim.AddComponent<SessionHost>();
            Host.Initialize(m_Mode, seed);

            var cameraObject = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            camera.allowHDR = false;
            CameraRig = cameraObject.GetComponent<SnakeCameraRig>();
            if (CameraRig == null)   // not ?? : Unity's fake-null objects defeat null-coalescing
                CameraRig = cameraObject.AddComponent<SnakeCameraRig>();
            CameraRig.Host = Host;

            var view = new GameObject("WorldRenderer");
            view.transform.SetParent(transform, false);
            WorldRenderer = view.AddComponent<SnakeWorldRenderer>();
            WorldRenderer.Host = Host;
            WorldRenderer.CameraRig = CameraRig;

            InputRouter = gameObject.AddComponent<InputRouter>();
            InputRouter.Host = Host;

            if (m_CreateUI)
            {
                Hud = gameObject.AddComponent<SnakeHud>();
                Hud.Build(this);
                InputRouter.AddSource(Hud.Joystick);
            }
            InputRouter.AddSource(new KeyboardMouseInput());

            Quality = gameObject.AddComponent<AdaptiveQualityController>();
            Quality.Host = Host;
            Quality.SetLevel(Host.Session, 0);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (m_PerfHud)
            {
                var hud = gameObject.AddComponent<SPF.Runtime.Diagnostics.PerfHud>();
                hud.Host = Host;
            }
#endif
        }

        void OnDestroy()
        {
            if (m_Module != null) Destroy(m_Module);
            if (m_Mode != null) Destroy(m_Mode);
            if (m_OwnsConfig && m_Config != null) Destroy(m_Config);
        }

        // ---- Game flow used by the UI (and by UI automation). ----

        public void StartGame()
        {
            State?.RequestStart();
            CameraRig.Snap();
        }

        /// <summary>Respawn in the same world after dying.</summary>
        public void PlayAgain() => StartGame();

        /// <summary>Fresh world in attract mode (memory is reused).</summary>
        public void BackToMenu()
        {
            if (Session == null) return;
            Session.Restart();
            WorldRenderer.ResetCaches();
            CameraRig.Snap();
        }

        public void CycleSkin()
        {
            if (State == null || m_Config.Skins.Count == 0) return;
            State.PlayerSkin = (State.PlayerSkin + 1) % m_Config.Skins.Count;
            State.Version++;
        }

        public string SkinName(int skin) => m_Config.Skins.Count > 0 ? m_Config.Skins[skin % m_Config.Skins.Count].Name : "-";
        public string RegionName(int region) => region < m_Config.Regions.Count ? m_Config.Regions[region].Name : "?";
        public string SnakeName(int id) => m_Config.AINames.Count > 0 ? m_Config.AINames[id % m_Config.AINames.Count] : "Snake";
    }
}
