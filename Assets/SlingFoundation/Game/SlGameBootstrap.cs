using SlingFoundation.Presentation;
using SPF.Presentation.Audio;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using SPF.Shell.Input;
using SPF.Shell.Performance;
using SPF.Shell.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace SlingFoundation.Game
{
    /// <summary>
    /// The slingshot game: drag back from anywhere and release to fire (the drag is the pull), physics at 60 Hz in
    /// a Burst job, an idle frame-rate drop while aiming, HUD, sounds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlGameBootstrap : MonoBehaviour
    {
        [SerializeField] bool m_CreateUI = true;

        ModeDefinition m_Mode;
        GameplayModuleAsset m_Module;
        SoundPlayer m_Sound;
        int m_Launch, m_Hit, m_Break, m_Pop, m_Clear, m_Fail;
        SlFlow m_Flow = (SlFlow)255;
        int m_Version = -1;

        public SessionHost Host { get; private set; }
        public FrameGovernor Governor { get; private set; }
        public FollowCamera2D CameraRig { get; private set; }
        public SlRenderer Renderer { get; private set; }
        public GestureInput Gestures { get; private set; }
        public SimSession Session => Host != null ? Host.Session : null;
        public SlGameState State => Session?.World.Resource(SlKeys.Game);

        public BufferText StatsText { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform EndPanel { get; private set; }
        public Text EndText { get; private set; }
        public Button StartButton { get; private set; }
        public Button NextButton { get; private set; }
        public Button RetryButton { get; private set; }

        public static SlGameBootstrap Create(bool ui = true)
        {
            var go = new GameObject("SlingGame");
            go.SetActive(false);
            var game = go.AddComponent<SlGameBootstrap>();
            game.m_CreateUI = ui;
            go.SetActive(true);
            return game;
        }

        void Awake()
        {
            Governor = gameObject.AddComponent<FrameGovernor>();
            Governor.ThrottleWhenIdle = true;   // aiming at a settled level needs no 60 FPS
            Governor.SetFrameRates(60, 30);
            m_Mode = SlMode.Create(out m_Module);
            var sim = new GameObject("Simulation");
            sim.transform.SetParent(transform, false);
            Host = sim.AddComponent<SessionHost>();
            Host.Initialize(m_Mode, 1);

            var cameraObject = UnityEngine.Camera.main != null ? UnityEngine.Camera.main.gameObject : new GameObject("Main Camera", typeof(UnityEngine.Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.78f, 0.95f);
            CameraRig = cameraObject.GetComponent<FollowCamera2D>();
            if (CameraRig == null) CameraRig = cameraObject.AddComponent<FollowCamera2D>();
            CameraRig.Follow = 20f;
            if (cameraObject.GetComponent<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();

            var view = new GameObject("SlRenderer");
            view.transform.SetParent(transform, false);
            Renderer = view.AddComponent<SlRenderer>();
            Renderer.Host = Host;
            Renderer.Camera = CameraRig;
            Renderer.Feedback += OnFeedback;
            Gestures = gameObject.AddComponent<GestureInput>();
            Gestures.Tracker.TapSlop = 8f;

            m_Sound = SoundPlayer.Create(transform, 8);
            m_Launch = m_Sound.Register("launch", SfxDef.Create(SfxWave.Triangle, 220f, 660f, 0.25f, 0.45f), maxVoices: 1);
            m_Hit = m_Sound.Register("hit", SfxDef.Create(SfxWave.Noise, 900f, 150f, 0.12f, 0.5f).WithNoise(1f, 0.6f), maxVoices: 3, minInterval: 0.05f);
            m_Break = m_Sound.Register("break", SfxDef.Create(SfxWave.Noise, 2000f, 300f, 0.25f, 0.5f).WithNoise(1f, 0.3f), maxVoices: 3, minInterval: 0.04f);
            m_Pop = m_Sound.Register("pop", SfxDef.Create(SfxWave.Square, 880f, 220f, 0.2f, 0.4f).WithDuty(0.25f), maxVoices: 2);
            m_Clear = m_Sound.Register("clear", SfxDef.Create(SfxWave.Square, 523f, 2093f, 0.8f, 0.4f).WithDuty(0.25f).WithVibrato(0.4f, 10f), maxVoices: 1, priority: 3);
            m_Fail = m_Sound.Register("fail", SfxDef.Create(SfxWave.Triangle, 400f, 90f, 0.7f, 0.45f), maxVoices: 1, priority: 3);

            if (m_CreateUI) BuildUi();
        }

        void BuildUi()
        {
            var canvas = UIFactory.CreateCanvas(transform, "SlingUI");
            var root = canvas.transform;
            StatsText = BufferText.Create(root, "Stats", 40, TextAnchor.UpperLeft, new Vector2(0.01f, 0.86f), new Vector2(0.7f, 0.99f));

            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(0f, 0f, 0.1f, 0.45f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", "TOPPLE", 150, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            StartButton = UIFactory.Button(MenuPanel, "StartButton", "PLAY", new Vector2(0, -20), new Vector2(460, 140), new Color(0.3f, 0.75f, 0.45f, 0.95f), new Vector2(0.5f, 0.5f));
            StartButton.onClick.AddListener(() => State?.Send(SlCommandKind.Start));

            EndPanel = UIFactory.Panel(root, "EndPanel", new Color(0f, 0f, 0f, 0.45f), Vector2.zero, Vector2.one);
            EndText = UIFactory.Label(EndPanel, "EndText", "", 100, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            NextButton = UIFactory.Button(EndPanel, "NextButton", "NEXT", new Vector2(-260, -20), new Vector2(420, 130), new Color(0.3f, 0.65f, 0.9f, 0.95f), new Vector2(0.5f, 0.5f));
            NextButton.onClick.AddListener(() => State?.Send(SlCommandKind.NextLevel));
            RetryButton = UIFactory.Button(EndPanel, "RetryButton", "RETRY", new Vector2(260, -20), new Vector2(420, 130), new Color(0.75f, 0.45f, 0.3f, 0.95f), new Vector2(0.5f, 0.5f));
            RetryButton.onClick.AddListener(() => State?.Send(SlCommandKind.Retry));
            // Drags that start on the UI don't aim.
            Gestures.Blocked = p => MenuPanel.gameObject.activeInHierarchy || EndPanel.gameObject.activeInHierarchy;
        }

        /// <summary>World-space drag vector for a screen-space drag (the pull: release fires the opposite way).</summary>
        float2 PullFor(float2 screenFrom, float2 screenTo)
        {
            var cam = CameraRig.Camera;
            var a = cam.ScreenToWorldPoint(new Vector3(screenFrom.x, screenFrom.y, 10f));
            var b = cam.ScreenToWorldPoint(new Vector3(screenTo.x, screenTo.y, 10f));
            return new float2(b.x - a.x, b.y - a.y);
        }

        /// <summary>Fires a bird with this pull (UI and tests).</summary>
        public void Launch(float2 pull) => State?.Send(SlCommandKind.Launch, pull);

        void Update()
        {
            var state = State;
            if (state == null) return;
            var tracker = Gestures.Tracker;
            if (state.Flow == SlFlow.Aiming)
            {
                Renderer.Aim = tracker.TryGetPrimary(out var start, out var now) ? PullFor(start, now) : float2.zero;
                for (int i = 0; i < tracker.Releases.Count; i++)
                {
                    var (from, to) = tracker.Releases[i];
                    float2 pull = PullFor(from, to);
                    if (math.length(pull) > 0.4f) Launch(pull);
                }
            }
            else Renderer.Aim = float2.zero;
            if (state.Flow == SlFlow.Flying || tracker.Active > 0) Governor.KeepAwake();

            if (StatsText != null)
            {
                StatsText.Begin().Append("Level ").Append(state.Level + 1).Append("   Score ").Append(state.Score)
                    .Append("\nBirds ").Append(state.BirdsLeft).Append("   Targets ").Append(state.TargetsLeft);
                StatsText.Commit();
            }
            if (MenuPanel == null || state.Flow == m_Flow && state.Version == m_Version) return;
            m_Flow = state.Flow;
            m_Version = state.Version;
            MenuPanel.gameObject.SetActive(state.Flow == SlFlow.Menu);
            bool end = state.Flow == SlFlow.LevelClear || state.Flow == SlFlow.Failed || state.Flow == SlFlow.Won;
            EndPanel.gameObject.SetActive(end);
            StatsText.gameObject.SetActive(state.Flow != SlFlow.Menu);
            if (end)
            {
                EndText.text = state.Flow == SlFlow.LevelClear ? "TOPPLED!" : state.Flow == SlFlow.Won ? "ALL CLEAR!" : "OUT OF BIRDS";
                NextButton.gameObject.SetActive(state.Flow == SlFlow.LevelClear);
                RetryButton.gameObject.SetActive(state.Flow != SlFlow.LevelClear);
            }
        }

        void OnFeedback(SlFeedback e)
        {
            switch (e.Kind)
            {
                case SlFeedbackKind.Launch: m_Sound.Play(m_Launch); break;
                case SlFeedbackKind.Hit: if (e.Strength > 3f) m_Sound.Play(m_Hit); break;
                case SlFeedbackKind.Break: m_Sound.Play(m_Break); break;
                case SlFeedbackKind.TargetDown: m_Sound.Play(m_Pop); break;
                case SlFeedbackKind.Clear: m_Sound.Play(m_Clear); break;
                case SlFeedbackKind.Fail: m_Sound.Play(m_Fail); break;
            }
        }

        void OnDestroy()
        {
            if (m_Module != null) Destroy(m_Module);
            if (m_Mode != null) Destroy(m_Mode);
        }
    }
}
