using DefenseFoundation.Presentation;
using SPF.Presentation.Audio;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using SPF.Shell.Input;
using SPF.Shell.Performance;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DefenseFoundation.Game
{
    /// <summary>
    /// Assembles the tower-defense game: session host, gesture camera (drag to pan, pinch / wheel to zoom,
    /// tap to select a tile), renderer, HUD with build / upgrade / sell, synthesized sounds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TdGameBootstrap : MonoBehaviour
    {
        ModeDefinition m_Mode;
        GameplayModuleAsset m_Module;
        SoundPlayer m_Sound;
        int m_Shot, m_Boom, m_Death, m_Leak, m_Build, m_Rejected;
        float2 m_View;

        public SessionHost Host { get; private set; }
        public FrameGovernor Governor { get; private set; }
        public FollowCamera2D CameraRig { get; private set; }
        public TdRenderer Renderer { get; private set; }
        public TdHud Hud { get; private set; }
        public GestureInput Gestures { get; private set; }
        public SimSession Session => Host != null ? Host.Session : null;
        public TdGameState State => Session?.World.Resource(TdKeys.Game);
        public int2 Selected => Renderer.Selected;

        public static TdGameBootstrap Create(bool ui = true)
        {
            var go = new GameObject("DefenseGame");
            go.SetActive(false);
            var game = go.AddComponent<TdGameBootstrap>();
            go.SetActive(true);
            return game;
        }

        void Awake()
        {
            Governor = gameObject.AddComponent<FrameGovernor>();
            Governor.ThrottleWhenIdle = true;
            Governor.SetFrameRates(60, 30);
            m_Mode = TdMode.Create(out m_Module);
            var sim = new GameObject("Simulation");
            sim.transform.SetParent(transform, false);
            Host = sim.AddComponent<SessionHost>();
            Host.Initialize(m_Mode, 1);

            var cameraObject = UnityEngine.Camera.main != null ? UnityEngine.Camera.main.gameObject : new GameObject("Main Camera", typeof(UnityEngine.Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.16f, 0.12f);
            CameraRig = cameraObject.GetComponent<FollowCamera2D>();
            if (CameraRig == null) CameraRig = cameraObject.AddComponent<FollowCamera2D>();
            var size = TdRules.MapSize;
            m_View = (float2)size * 0.5f;
            CameraRig.Size = size.y * 0.5f + 1f;
            CameraRig.Follow = 20f;
            CameraRig.Bounds = new float4(-2f, -2f, size.x + 2f, size.y + 2f);
            CameraRig.UpdateTarget = c => c.Target = m_View;
            if (cameraObject.GetComponent<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();

            var view = new GameObject("TdRenderer");
            view.transform.SetParent(transform, false);
            Renderer = view.AddComponent<TdRenderer>();
            Renderer.Host = Host;
            Renderer.Camera = CameraRig;

            m_Sound = SoundPlayer.Create(transform, 10);
            m_Shot = m_Sound.Register("shot", SfxDef.Create(SfxWave.Noise, 3000f, 1500f, 0.06f, 0.15f).WithNoise(1f, 0.5f), maxVoices: 2, minInterval: 0.05f);
            m_Boom = m_Sound.Register("boom", SfxDef.Create(SfxWave.Noise, 500f, 80f, 0.3f, 0.4f).WithNoise(1f, 0.8f), maxVoices: 1, minInterval: 0.1f);
            m_Death = m_Sound.Register("death", SfxDef.Create(SfxWave.Square, 400f, 120f, 0.1f, 0.25f).WithNoise(0.4f, 0.3f), maxVoices: 2, minInterval: 0.04f);
            m_Leak = m_Sound.Register("leak", SfxDef.Create(SfxWave.Saw, 220f, 110f, 0.3f, 0.4f), maxVoices: 1, priority: 2);
            m_Build = m_Sound.Register("build", SfxDef.Create(SfxWave.Square, 330f, 660f, 0.15f, 0.3f).WithDuty(0.25f), maxVoices: 1, priority: 2);
            m_Rejected = m_Sound.Register("nope", SfxDef.Create(SfxWave.Square, 160f, 140f, 0.15f, 0.3f), maxVoices: 1, priority: 2);
            Renderer.Feedback += OnFeedback;

            Gestures = gameObject.AddComponent<GestureInput>();
            Gestures.Blocked = OverUi;
            Hud = gameObject.AddComponent<TdHud>();
            Hud.Build(this);
        }

        static bool OverUi(float2 screen)
        {
            var events = EventSystem.current;
            if (events == null) return false;
            var data = new PointerEventData(events) { position = screen };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            events.RaycastAll(data, hits);
            return hits.Count > 0;
        }

        void Update()
        {
            // Waves run at full rate; the build phase drops to the idle rate until the player touches the screen.
            if (Session != null && Session.World.Resource(TdKeys.Game).Flow == TdFlow.Wave) Governor.KeepAwake();
            var tracker = Gestures.Tracker;
            var cam = CameraRig.Camera;
            float unitsPerPixel = 2f * CameraRig.Size / math.max(Screen.height, 1);
            if (math.any(tracker.Drag != 0f)) m_View -= tracker.Drag * unitsPerPixel;
            float zoom = tracker.Pinch;
#if ENABLE_LEGACY_INPUT_MANAGER
            zoom *= 1f + UnityEngine.Input.mouseScrollDelta.y * 0.1f;
#endif
            if (zoom != 1f) CameraRig.Size = math.clamp(CameraRig.Size / zoom, 3f, TdRules.MapSize.y * 0.5f + 2f);
            var size = TdRules.MapSize;
            m_View = math.clamp(m_View, float2.zero, (float2)size);
            foreach (var tap in tracker.Taps)
            {
                var world = cam.ScreenToWorldPoint(new Vector3(tap.x, tap.y, 50f));
                Select((int2)math.floor(new float2(world.x, world.y)));
            }
        }

        void OnFeedback(TdFeedback e)
        {
            switch (e.Kind)
            {
                case TdFeedbackKind.Shot: m_Sound.Play(e.Tower == TowerKind.Cannon ? m_Boom : m_Shot, 0.6f); break;
                case TdFeedbackKind.Death: m_Sound.Play(m_Death, 0.6f); break;
                case TdFeedbackKind.Leak: m_Sound.Play(m_Leak); break;
                case TdFeedbackKind.Built: m_Sound.Play(m_Build); break;
                case TdFeedbackKind.Rejected: m_Sound.Play(m_Rejected); break;
            }
        }

        void OnDestroy()
        {
            if (m_Module != null) Destroy(m_Module);
            if (m_Mode != null) Destroy(m_Mode);
        }

        // ---- Actions (UI, gestures, tests) ----

        public void StartGame() { State?.Send(TdCommandKind.Start); Renderer.Selected = new int2(-1); }
        public void NextWave() => State?.Send(TdCommandKind.NextWave);

        /// <summary>Selects a tile (tap): shows the build panel on empty ground, the tower panel on a tower.</summary>
        public void Select(int2 cell)
        {
            var session = Session;
            if (session == null) return;
            session.Sync();
            var map = session.World.Resource(TdKeys.Map);
            if (!map.AsView().InBounds(cell)) { Renderer.Selected = new int2(-1); return; }
            Renderer.Selected = cell;
            Renderer.SelectedBuildable = TdQueries.CanBuild(session, cell, TowerKind.Arrow, out _) || map[cell] == TdTile.Tower;
            Hud.Refresh();
        }

        public void Build(TowerKind kind) { State?.Send(TdCommandKind.Build, Selected, kind); }
        public void Upgrade() => State?.Send(TdCommandKind.Upgrade, Selected);
        public void Sell() { State?.Send(TdCommandKind.Sell, Selected); Renderer.Selected = new int2(-1); }
    }
}
