using PlatformerFoundation.Presentation;
using SPF.Contracts;
using SPF.Presentation.Audio;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using SPF.Shell.Input;
using SPF.Shell.Performance;
using UnityEngine;

namespace PlatformerFoundation.Game
{
    /// <summary>Assembles the platformer from foundation pieces (session host at 60 Hz, camera, input, renderer, HUD, sound).</summary>
    [DisallowMultipleComponent]
    public sealed class PlGameBootstrap : MonoBehaviour
    {
        [SerializeField] bool m_CreateUI = true;
        [SerializeField] int m_TargetFrameRate = 60;

        ModeDefinition m_Mode;
        GameplayModuleAsset m_Module;
        SoundPlayer m_Sound;
        int m_Jump, m_Coin, m_Stomp, m_Die, m_Goal;

        public SessionHost Host { get; private set; }
        public FrameGovernor Governor { get; private set; }
        public FollowCamera2D CameraRig { get; private set; }
        public PlRenderer Renderer { get; private set; }
        public PlHud Hud { get; private set; }
        public InputRouter InputRouter { get; private set; }
        public SoundPlayer Sound => m_Sound;
        public SimSession Session => Host != null ? Host.Session : null;
        public PlGameState State => Session?.World.Resource(PlKeys.Game);

        /// <summary>Scripted input (tests, demos): replaces the stick while set; buttons are combined.</summary>
        public System.Func<InputFrame> Script { get; set; }

        public static PlGameBootstrap Create(bool ui = true)
        {
            var go = new GameObject("PlatformerGame");
            go.SetActive(false);
            var game = go.AddComponent<PlGameBootstrap>();
            game.m_CreateUI = ui;
            go.SetActive(true);
            return game;
        }

        void Awake()
        {
            Governor = gameObject.AddComponent<FrameGovernor>();
            Governor.ThrottleWhenIdle = false;
            Governor.SetFrameRates(m_TargetFrameRate, 30);
            m_Mode = PlMode.Create(out m_Module);
            var sim = new GameObject("Simulation");
            sim.transform.SetParent(transform, false);
            Host = sim.AddComponent<SessionHost>();
            Host.Initialize(m_Mode, 1);

            var cameraObject = UnityEngine.Camera.main != null ? UnityEngine.Camera.main.gameObject : new GameObject("Main Camera", typeof(UnityEngine.Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.45f, 0.7f, 0.95f);
            CameraRig = cameraObject.GetComponent<FollowCamera2D>();
            if (CameraRig == null) CameraRig = cameraObject.AddComponent<FollowCamera2D>();
            CameraRig.Size = 7f;
            CameraRig.Follow = 6f;
            if (cameraObject.GetComponent<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();

            var view = new GameObject("PlRenderer");
            view.transform.SetParent(transform, false);
            Renderer = view.AddComponent<PlRenderer>();
            Renderer.Host = Host;
            Renderer.Camera = CameraRig;

            m_Sound = SoundPlayer.Create(transform, 8);
            m_Jump = m_Sound.Register("jump", SfxDef.Create(SfxWave.Square, 300f, 650f, 0.14f, 0.3f).WithDuty(0.25f), maxVoices: 1);
            m_Coin = m_Sound.Register("coin", SfxDef.Create(SfxWave.Square, 988f, 1319f, 0.12f, 0.3f).WithDuty(0.25f).WithEnvelope(0.002f, 0.03f, 0.7f, 0.05f), maxVoices: 2, minInterval: 0.03f);
            m_Stomp = m_Sound.Register("stomp", SfxDef.Create(SfxWave.Noise, 600f, 120f, 0.15f, 0.5f).WithNoise(1f, 0.6f), maxVoices: 1);
            m_Die = m_Sound.Register("die", SfxDef.Create(SfxWave.Triangle, 600f, 80f, 0.7f, 0.5f).WithVibrato(1.5f, 8f), maxVoices: 1, priority: 3);
            m_Goal = m_Sound.Register("goal", SfxDef.Create(SfxWave.Square, 523f, 2093f, 0.8f, 0.4f).WithDuty(0.25f).WithVibrato(0.4f, 10f), maxVoices: 1, priority: 3);
            Renderer.Feedback += OnFeedback;

            InputRouter = gameObject.AddComponent<InputRouter>();
            InputRouter.Sink = frame =>
            {
                var state = State;
                if (state == null) return;
                if (Script != null)
                {
                    // The script steers; buttons pressed on the touch UI or keyboard still count.
                    var scripted = Script();
                    scripted.Held |= frame.Held;
                    scripted.Pressed |= frame.Pressed;
                    frame = scripted;
                }
                state.Input = InputFrame.Latch(state.Input, frame);
            };
            if (m_CreateUI)
            {
                Hud = gameObject.AddComponent<PlHud>();
                Hud.Build(this);
                InputRouter.AddSource(Hud.TouchInput);
            }
            InputRouter.AddSource(new KeyboardInputSource().Map(KeyCode.Space, PlButton.Jump).Map(KeyCode.K, PlButton.Jump).Map(KeyCode.UpArrow, PlButton.Jump));
        }

        void OnFeedback(PlFeedback e)
        {
            switch (e.Kind)
            {
                case PlFeedbackKind.Jump: m_Sound.Play(m_Jump); break;
                case PlFeedbackKind.Coin: m_Sound.Play(m_Coin); break;
                case PlFeedbackKind.Stomp: m_Sound.Play(m_Stomp); break;
                case PlFeedbackKind.Die: m_Sound.Play(m_Die); break;
                case PlFeedbackKind.Goal: m_Sound.Play(m_Goal); break;
            }
        }

        void OnDestroy()
        {
            if (m_Module != null) Destroy(m_Module);
            if (m_Mode != null) Destroy(m_Mode);
        }

        public void StartGame() => State?.Send(PlCommandKind.Start);
        public void NextLevel() => State?.Send(PlCommandKind.NextLevel);
        public void Retry() => State?.Send(PlCommandKind.Retry);
        public void BackToMenu() => State?.Send(PlCommandKind.Menu);
    }
}
