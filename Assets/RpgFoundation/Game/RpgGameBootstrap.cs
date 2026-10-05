using RpgFoundation.Presentation;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Persistence;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using SPF.Shell.Input;
using UnityEngine;

namespace RpgFoundation.Game
{
    /// <summary>
    /// Assembles the dungeon RPG at runtime from foundation pieces: session host (four modules), shell
    /// follow camera and input router, world renderer, HUD, save slot. A scene only needs this component;
    /// tests create it with <see cref="Create"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RpgGameBootstrap : MonoBehaviour
    {
        public const string SaveSlot = "hero";
        /// <summary>Mid-floor snapshot (pause / quit / app suspend); newer than the floor-start save while it exists.</summary>
        public const string RunSlot = "run";

        [SerializeField] RpgConfig m_Config;
        [SerializeField] uint m_Seed = 1;
        [SerializeField] bool m_CreateUI = true;
        [SerializeField] bool m_PerfHud = true;
        [SerializeField] int m_TargetFrameRate = 60;

        bool m_OwnsConfig;
        ModeDefinition m_Mode;
        GameplayModuleAsset[] m_Modules;
        int m_SavedBuild;
        bool m_RunSaved;
        RpgBot m_Bot;

        public SessionHost Host { get; private set; }
        public FollowCamera2D CameraRig { get; private set; }
        public RpgWorldRenderer WorldRenderer { get; private set; }
        public InputRouter InputRouter { get; private set; }
        public RpgHud Hud { get; private set; }
        public RpgAudio Audio { get; private set; }
        public RpgOptions Options { get; } = new RpgOptions();
        public ProfileStore Saves { get; private set; }
        public SimSession Session => Host != null ? Host.Session : null;
        public RpgGameState State => Session?.World.Resource(RpgKeys.Game);
        public RpgRuntimeConfig Runtime => Session?.World.Resource(RpgKeys.Config);

        /// <summary>Lets the built-in bot play (attract mode, demos, smoke tests).</summary>
        public bool AutoPlay { get; set; }

        /// <summary>Save directory override (tests); defaults to persistentDataPath/Rpg.</summary>
        public static string SaveDirectoryOverride;

        public static RpgGameBootstrap Create(RpgConfig config = null, uint seed = 1, bool ui = true, bool perfHud = false)
        {
            var go = new GameObject("RpgGame");
            go.SetActive(false);
            var game = go.AddComponent<RpgGameBootstrap>();
            game.m_Config = config;
            game.m_Seed = seed;
            game.m_CreateUI = ui;
            game.m_PerfHud = perfHud;
            go.SetActive(true);
            return game;
        }

        void Awake()
        {
            Application.targetFrameRate = m_TargetFrameRate;
            if (m_Config == null)
            {
                m_Config = RpgConfig.CreateDefault();
                m_OwnsConfig = true;
            }
            Saves = new ProfileStore(SaveDirectoryOverride ?? System.IO.Path.Combine(Application.persistentDataPath, "Rpg"));
            m_Mode = RpgMode.Create(m_Config, out m_Modules);

            var sim = new GameObject("Simulation");
            sim.transform.SetParent(transform, false);
            Host = sim.AddComponent<SessionHost>();
            Host.Initialize(m_Mode, m_Seed);

            var cameraObject = UnityEngine.Camera.main != null ? UnityEngine.Camera.main.gameObject : new GameObject("Main Camera", typeof(UnityEngine.Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.03f, 0.04f);
            CameraRig = cameraObject.GetComponent<FollowCamera2D>();
            if (CameraRig == null) CameraRig = cameraObject.AddComponent<FollowCamera2D>();

            var view = new GameObject("WorldRenderer");
            view.transform.SetParent(transform, false);
            WorldRenderer = view.AddComponent<RpgWorldRenderer>();
            WorldRenderer.Host = Host;
            WorldRenderer.Camera = CameraRig;

            if (cameraObject.GetComponent<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();
            Audio = RpgAudio.Create(transform, WorldRenderer, CameraRig);
            Audio.Config = Runtime;
            Saves.Load(RpgOptions.Slot, Options);
            ApplyOptions();

            InputRouter = gameObject.AddComponent<InputRouter>();
            InputRouter.Sink = frame =>
            {
                var state = State;
                if (state == null) return;
                if (AutoPlay && Session != null)
                {
                    m_Bot ??= new RpgBot();
                    // Ticks overlap rendering: complete the in-flight one before the bot reads the world.
                    Session.Sync();
                    frame = m_Bot.Think(Session.World);
                }
                state.Input = InputFrame.Latch(state.Input, frame);
            };
            if (m_CreateUI)
            {
                Hud = gameObject.AddComponent<RpgHud>();
                Hud.Build(this, camera);
                InputRouter.AddSource(Hud.TouchInput);
            }
            InputRouter.AddSource(new KeyboardInputSource()
                .Map(KeyCode.Space, RpgButton.Attack).Map(KeyCode.J, RpgButton.Attack)
                .Map(KeyCode.Alpha1, RpgButton.Skill1).Map(KeyCode.Alpha2, RpgButton.Skill2)
                .Map(KeyCode.Alpha3, RpgButton.Skill3).Map(KeyCode.Alpha4, RpgButton.Skill4)
                .Map(KeyCode.E, RpgButton.Skill1).Map(KeyCode.LeftShift, RpgButton.Skill2)
                .Map(KeyCode.Q, RpgButton.Potion));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (m_PerfHud)
                gameObject.AddComponent<SPF.Runtime.Diagnostics.PerfHud>().Host = Host;
#endif
        }

        void LateUpdate()
        {
            // Autosave at every floor start (the profile snapshot a retry / continue restores); a mid-floor
            // snapshot from an earlier floor is stale from then on.
            var state = State;
            if (state == null) return;
            if (state.FloorBuilds != m_SavedBuild)
            {
                m_SavedBuild = state.FloorBuilds;
                if (state.Flow == RpgFlow.Playing)
                {
                    Saves.Save(SaveSlot, state.FloorStart);
                    DeleteRun();
                }
            }
            if (state.Flow == RpgFlow.Dead) DeleteRun();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveRun();
        }

        void OnApplicationQuit() => SaveRun();

        /// <summary>Saves the whole session mid-floor (only while playing). Returns true when saved.</summary>
        public bool SaveRun()
        {
            var state = State;
            if (state == null || state.Flow != RpgFlow.Playing) return false;
            Saves.Save(RunSlot, new SessionSnapshotSave(Session));
            m_RunSaved = true;
            return true;
        }

        void DeleteRun()
        {
            if (!m_RunSaved && !Saves.Exists(RunSlot)) return;
            Saves.Delete(RunSlot);
            m_RunSaved = false;
        }

        // ---- Options ----

        public void ApplyOptions()
        {
            Audio.Player.SetVolume(SPF.Presentation.Audio.SoundBus.Sfx, Options.SfxVolume);
            Audio.Player.SetVolume(SPF.Presentation.Audio.SoundBus.Ui, Options.SfxVolume);
            Audio.Player.SetVolume(SPF.Presentation.Audio.SoundBus.Music, Options.MusicVolume);
            Audio.Player.Muted = Options.Muted;
        }

        /// <summary>Changes the sound settings, applies and saves them.</summary>
        public void SetSound(float sfxVolume, bool muted)
        {
            Options.SfxVolume = Mathf.Clamp01(sfxVolume);
            Options.Muted = muted;
            ApplyOptions();
            Saves.Save(RpgOptions.Slot, Options);
        }

        // ---- Pause ----

        public bool Paused => Session != null && Session.State == SessionState.Paused;

        public void Pause()
        {
            if (State?.Flow == RpgFlow.Playing) Session.Pause();
        }

        public void Resume() => Session?.Resume();

        /// <summary>Pause menu "save &amp; quit": snapshot the floor, back to the title (Continue resumes here).</summary>
        public void SaveAndQuit()
        {
            SaveRun();
            Session.Resume();
            BackToMenu();
        }

        void OnDestroy()
        {
            m_Bot?.Dispose();
            if (m_Modules != null) foreach (var m in m_Modules) if (m != null) Destroy(m);
            if (m_Mode != null) Destroy(m_Mode);
            if (m_OwnsConfig && m_Config != null) Destroy(m_Config);
        }

        // ---- Game flow (UI buttons, UI automation) ----

        public bool HasSave => Saves.Exists(SaveSlot) || Saves.Exists(RunSlot);

        public void NewGame()
        {
            var state = State;
            if (state == null) return;
            DeleteRun();
            state.Profile.Reset(m_Seed * 7919u + (uint)System.Environment.TickCount | 1u, Runtime.StartPotions);
            state.Send(RpgCommandKind.NewGame);
            CameraRig.Snap();
        }

        /// <summary>New game with a fixed run seed (tests, daily runs).</summary>
        public void NewGame(uint runSeed)
        {
            var state = State;
            if (state == null) return;
            DeleteRun();
            state.Profile.Reset(runSeed, Runtime.StartPotions);
            state.Send(RpgCommandKind.NewGame);
            CameraRig.Snap();
        }

        /// <summary>
        /// Resumes the mid-floor snapshot when there is one (exactly where the player left), otherwise the
        /// last floor start. A snapshot that no longer fits (game updated) falls back to the floor start.
        /// </summary>
        public bool Continue()
        {
            var state = State;
            if (state == null) return false;
            if (Saves.Exists(RunSlot))
            {
                Session.Resume();
                if (Saves.Load(RunSlot, new SessionSnapshotSave(Session)) && State.Flow == RpgFlow.Playing)
                {
                    m_SavedBuild = State.FloorBuilds;   // a restore is not a new floor: keep the run save
                    m_RunSaved = true;
                    CameraRig.Snap();
                    return true;
                }
                DeleteRun();
                state = State;
            }
            if (!Saves.Load(SaveSlot, state.Profile)) return false;
            state.Send(RpgCommandKind.Continue);
            CameraRig.Snap();
            return true;
        }

        public void Descend() { State?.Send(RpgCommandKind.Descend); CameraRig.Snap(); }
        public void Retry() { State?.Send(RpgCommandKind.Retry); CameraRig.Snap(); }
        public void BackToMenu() => State?.Send(RpgCommandKind.Menu);
        public void Equip(int inventoryIndex) => State?.Send(RpgCommandKind.Equip, inventoryIndex);
        public void Buy(int offer) => State?.Send(RpgCommandKind.Buy, offer);
    }
}
