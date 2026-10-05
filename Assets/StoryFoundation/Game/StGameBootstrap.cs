using System.IO;
using SPF.L2.Narrative;
using SPF.Runtime.Composition;
using SPF.Runtime.Persistence;
using SPF.Runtime.Session;
using SPF.Shell.Performance;
using SPF.Shell.UI;
using StoryFoundation.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace StoryFoundation.Game
{
    /// <summary>
    /// A visual novel on the foundation: the story runs in a session with a manual clock (one tick per tap or
    /// choice), so BACK is a snapshot undo and SAVE / LOAD are session snapshots; the dialogue box is the shared
    /// typewriter UI; EN / 中文 switches the string table live. Idle frames drop to 30 FPS.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StGameBootstrap : MonoBehaviour
    {
        ModeDefinition m_Mode;
        GameplayModuleAsset m_Module;
        int m_RecordedSerial = -1;
        bool m_InStory;

        public SessionHost Host { get; private set; }
        public FrameGovernor Governor { get; private set; }
        public StRenderer Renderer { get; private set; }
        public DialogueBox Dialogue { get; private set; }
        public LocalizationTable Strings { get; private set; }
        public SnapshotHistory History { get; private set; }
        public ProfileStore Saves { get; private set; }
        public SimSession Session => Host != null ? Host.Session : null;
        public StState State => Session?.World.Resource(StKeys.State);

        public RectTransform MenuPanel { get; private set; }
        public Button StartButton { get; private set; }
        public Button LanguageButton { get; private set; }
        public Button BackButton { get; private set; }
        public Button SaveButton { get; private set; }
        public Button LoadButton { get; private set; }

        public static StGameBootstrap Create(string saveDirectory = null)
        {
            var go = new GameObject("StoryGame");
            go.SetActive(false);
            var game = go.AddComponent<StGameBootstrap>();
            game.Saves = new ProfileStore(saveDirectory ?? Path.Combine(Application.persistentDataPath, "story"));
            go.SetActive(true);
            return game;
        }

        void Awake()
        {
            Governor = gameObject.AddComponent<FrameGovernor>();
            Governor.ThrottleWhenIdle = true;
            Governor.SetFrameRates(60, 30);
            m_Mode = StMode.Create(out m_Module);
            var sim = new GameObject("Simulation");
            sim.transform.SetParent(transform, false);
            Host = sim.AddComponent<SessionHost>();
            Host.Initialize(m_Mode, 1);
            Session.ManualClock = true;
            History = new SnapshotHistory(Session, 128);
            Saves ??= new ProfileStore(Path.Combine(Application.persistentDataPath, "story"));
            Strings = LocalizationTable.FromCsv(StContent.Strings);

            var cameraObject = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -20f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.04f, 0.12f);

            var view = new GameObject("StRenderer");
            view.transform.SetParent(transform, false);
            Renderer = view.AddComponent<StRenderer>();
            Renderer.Host = Host;

            var canvas = UIFactory.CreateCanvas(transform, "StoryUI");
            var root = canvas.transform;
            Dialogue = DialogueBox.Create(root);
            Dialogue.Strings = Strings;
            Dialogue.Runner = State.Runner;
            Dialogue.Advanced += () => Step(StCommandKind.Advance);
            Dialogue.Chosen += i => Step(StCommandKind.Choose, i);

            var bar = UIFactory.Panel(root, "TopBar", Color.clear, new Vector2(0f, 0.9f), Vector2.one, raycast: false);
            LanguageButton = UIFactory.Button(bar, "LanguageButton", "中文", new Vector2(-120, -50), new Vector2(200, 80), new Color(0.2f, 0.25f, 0.4f, 0.9f), new Vector2(1f, 1f), 34);
            LanguageButton.onClick.AddListener(ToggleLanguage);
            BackButton = UIFactory.Button(bar, "BackButton", "BACK", new Vector2(120, -50), new Vector2(200, 80), new Color(0.2f, 0.25f, 0.4f, 0.9f), new Vector2(0f, 1f), 34);
            BackButton.onClick.AddListener(Back);
            SaveButton = UIFactory.Button(bar, "SaveButton", "SAVE", new Vector2(340, -50), new Vector2(200, 80), new Color(0.2f, 0.25f, 0.4f, 0.9f), new Vector2(0f, 1f), 34);
            SaveButton.onClick.AddListener(() => Saves.Save("quick", new SessionSnapshotSave(Session)));
            LoadButton = UIFactory.Button(bar, "LoadButton", "LOAD", new Vector2(560, -50), new Vector2(200, 80), new Color(0.2f, 0.25f, 0.4f, 0.9f), new Vector2(0f, 1f), 34);
            LoadButton.onClick.AddListener(() => LoadQuick());

            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(0f, 0f, 0.05f, 0.55f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", "THE LANTERN KEEPER", 110, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            StartButton = UIFactory.Button(MenuPanel, "StartButton", "BEGIN", new Vector2(0, -20), new Vector2(460, 140), new Color(0.75f, 0.45f, 0.25f, 0.95f), new Vector2(0.5f, 0.5f));
            StartButton.onClick.AddListener(() => { History.Clear(); m_RecordedSerial = -1; Step(StCommandKind.Start); });
        }

        void Step(StCommandKind kind, int choice = 0)
        {
            State.Send(kind, choice);
            Session.RequestTicks(1);
            Governor.KeepAwake();
        }

        public void Back()
        {
            Session.Sync();
            if (History.Undo()) m_RecordedSerial = State.Runner.Serial;
        }

        public void ToggleLanguage()
        {
            bool zh = Strings.Language == "en";
            Strings.SetLanguage(zh ? "zh" : "en");
            UIFactory.SetText(LanguageButton, zh ? "EN" : "中文");
            Dialogue.Refresh();
        }

        public bool LoadQuick()
        {
            if (!Saves.Exists("quick")) return false;
            Session.Sync();
            bool ok = Saves.Load("quick", new SessionSnapshotSave(Session));
            if (ok) { History.Clear(); m_RecordedSerial = -1; }
            return ok;
        }

        void Update()
        {
            var s = State;
            if (s == null) return;
            if (Session.PendingTicks == 0 && s.InStory && s.Runner.Serial != m_RecordedSerial)
            {
                History.Record();   // one undo step per shown line
                m_RecordedSerial = s.Runner.Serial;
            }
            if (Dialogue.Typing) Governor.KeepAwake();
            bool inStory = s.InStory && s.Runner.State != DialogueRunner.Mode.Ended;
            if (inStory != m_InStory || MenuPanel.gameObject.activeSelf == inStory)
            {
                m_InStory = inStory;
                MenuPanel.gameObject.SetActive(!inStory);
            }
            BackButton.interactable = History.CanUndo;
        }

        void OnDestroy()
        {
            if (m_Module != null) Destroy(m_Module);
            if (m_Mode != null) Destroy(m_Mode);
        }
    }
}
