using PuzzleFoundation.Presentation;
using SPF.Presentation.Audio;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Shell.Input;
using SPF.Shell.Performance;
using SPF.Shell.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace PuzzleFoundation.Game
{
    /// <summary>
    /// Match-3 on the foundation in turn-based mode: the session's clock is manual (one tick per move),
    /// swipes or tap-tap pick the swap, input waits for the move's animation, every completed move is
    /// recorded for undo, a hint shows a valid move after a pause.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class M3GameBootstrap : MonoBehaviour
    {
        ModeDefinition m_Mode;
        GameplayModuleAsset m_Module;
        SoundPlayer m_Sound;
        int m_Swap, m_Pop, m_Bomb, m_Nope, m_Win;
        float m_Idle;
        int m_RecordedSerial = -1;

        public SessionHost Host { get; private set; }
        public FrameGovernor Governor { get; private set; }
        public M3Renderer Renderer { get; private set; }
        public GestureInput Gestures { get; private set; }
        public SnapshotHistory History { get; private set; }
        public Camera Camera { get; private set; }
        public SimSession Session => Host != null ? Host.Session : null;
        public M3Board Board => Session?.World.Resource(M3Keys.Board);
        public bool InputLocked => Renderer.Busy || Session.PendingTicks > 0 || Board.Moves.Count > 0;

        // HUD
        public Text StatsText { get; private set; }
        public Image ScoreFill { get; private set; }
        public Button StartButton { get; private set; }
        public Button UndoButton { get; private set; }
        public Button HintButton { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform EndPanel { get; private set; }
        public Text EndText { get; private set; }

        public static M3GameBootstrap Create()
        {
            var go = new GameObject("PuzzleGame");
            go.SetActive(false);
            var game = go.AddComponent<M3GameBootstrap>();
            go.SetActive(true);
            return game;
        }

        void Awake()
        {
            Governor = gameObject.AddComponent<FrameGovernor>();
            Governor.ThrottleWhenIdle = true;
            Governor.SetFrameRates(60, 30);
            m_Mode = M3Mode.Create(out m_Module);
            var sim = new GameObject("Simulation");
            sim.transform.SetParent(transform, false);
            Host = sim.AddComponent<SessionHost>();
            Host.Initialize(m_Mode, 17);
            Session.ManualClock = true;
            History = new SnapshotHistory(Session, 50);

            var cameraObject = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            Camera = cameraObject.GetComponent<Camera>();
            Camera.orthographic = true;
            Camera.orthographicSize = 6.5f;
            Camera.transform.position = new Vector3(0f, -0.5f, -20f);
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.16f, 0.13f, 0.24f);
            if (cameraObject.GetComponent<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();

            var view = new GameObject("M3Renderer");
            view.transform.SetParent(transform, false);
            Renderer = view.AddComponent<M3Renderer>();
            Renderer.Host = Host;
            m_Sound = SoundPlayer.Create(transform, 8);
            m_Swap = m_Sound.Register("swap", SfxDef.Create(SfxWave.Sine, 500f, 700f, 0.08f, 0.3f), maxVoices: 1);
            m_Pop = m_Sound.Register("pop", SfxDef.Create(SfxWave.Square, 700f, 1400f, 0.08f, 0.25f).WithDuty(0.25f), maxVoices: 3, minInterval: 0.03f);
            m_Bomb = m_Sound.Register("bomb", SfxDef.Create(SfxWave.Noise, 500f, 90f, 0.35f, 0.5f).WithNoise(1f, 0.8f), maxVoices: 1);
            m_Nope = m_Sound.Register("nope", SfxDef.Create(SfxWave.Square, 180f, 150f, 0.12f, 0.3f), maxVoices: 1);
            m_Win = m_Sound.Register("win", SfxDef.Create(SfxWave.Square, 523f, 2093f, 0.8f, 0.4f).WithDuty(0.25f).WithVibrato(0.4f, 10f), maxVoices: 1);
            Renderer.EventPlayed += e =>
            {
                switch (e.Kind)
                {
                    case M3EventKind.Swap: m_Sound.Play(m_Swap); break;
                    case M3EventKind.SwapBack: m_Sound.Play(m_Nope); break;
                    case M3EventKind.Clear: m_Sound.Play(m_Pop, 0.7f, 1f + e.Step * 0.08f); break;
                    case M3EventKind.MakeBomb: m_Sound.Play(m_Bomb); break;
                }
            };
            Gestures = gameObject.AddComponent<GestureInput>();
            BuildHud();
        }

        void BuildHud()
        {
            var root = UIFactory.CreateCanvas(transform, "PuzzleUI").transform;
            var hud = UIFactory.Panel(root, "Hud", Color.clear, Vector2.zero, Vector2.one, raycast: false);
            StatsText = UIFactory.Label(hud, "Stats", "", 38, TextAnchor.UpperCenter, new Vector2(0f, 0.9f), new Vector2(1f, 0.99f));
            ScoreFill = UIFactory.Bar(hud, "ScoreBar", new Color(0f, 0f, 0f, 0.5f), new Color(0.95f, 0.8f, 0.3f, 0.95f), new Vector2(0.2f, 0.87f), new Vector2(0.8f, 0.89f));
            UndoButton = UIFactory.Button(hud, "UndoButton", "UNDO", new Vector2(-200, 90), new Vector2(260, 110), new Color(0.35f, 0.4f, 0.65f, 0.95f), new Vector2(0.5f, 0f), 34);
            UndoButton.onClick.AddListener(Undo);
            HintButton = UIFactory.Button(hud, "HintButton", "HINT", new Vector2(200, 90), new Vector2(260, 110), new Color(0.35f, 0.6f, 0.4f, 0.95f), new Vector2(0.5f, 0f), 34);
            HintButton.onClick.AddListener(ShowHint);
            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", "GEM SWAP", 130, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            StartButton = UIFactory.Button(MenuPanel, "StartButton", "PLAY", new Vector2(0, -20), new Vector2(460, 140), new Color(0.3f, 0.75f, 0.45f, 0.95f), new Vector2(0.5f, 0.5f));
            StartButton.onClick.AddListener(StartGame);
            EndPanel = UIFactory.Panel(root, "EndPanel", new Color(0f, 0f, 0f, 0.6f), Vector2.zero, Vector2.one);
            EndText = UIFactory.Label(EndPanel, "EndText", "", 100, TextAnchor.MiddleCenter, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            var again = UIFactory.Button(EndPanel, "AgainButton", "AGAIN", new Vector2(0, -40), new Vector2(460, 140), new Color(0.3f, 0.6f, 0.85f, 0.95f), new Vector2(0.5f, 0.5f));
            again.onClick.AddListener(StartGame);
        }

        void OnDestroy()
        {
            if (m_Module != null) Destroy(m_Module);
            if (m_Mode != null) Destroy(m_Mode);
        }

        public void StartGame()
        {
            Board.StartRequested = true;
            Session.RequestTicks(1);
            History.Clear();
            m_RecordedSerial = -1;
        }

        /// <summary>Queues a swap (one turn = one requested tick). Ignored while a move is still animating.</summary>
        public bool TrySwap(int2 cell, int2 direction)
        {
            var board = Board;
            if (board == null || board.Flow != M3Flow.Playing || InputLocked) return false;
            board.Moves.Enqueue(new M3Move { Cell = cell, Direction = direction });
            Session.RequestTicks(1);
            Renderer.Selected = new int2(-1);
            Renderer.Hint = Renderer.HintTo = new int2(-1);
            m_Idle = 0f;
            return true;
        }

        public void Undo()
        {
            if (InputLocked) return;
            Session.Sync();
            if (History.Undo()) m_RecordedSerial = Board.LogSerial;
        }

        public void ShowHint()
        {
            Session.Sync();
            if (M3Rules.FindMove(Board, out var move)) { Renderer.Hint = move.Cell; Renderer.HintTo = move.Cell + move.Direction; }
        }

        void Update()
        {
            var board = Board;
            if (board == null) return;
            if (InputLocked) Governor.KeepAwake();   // tweens play at full rate; a board at rest drops to the idle rate
            var tracker = Gestures.Tracker;
            foreach (var (from, to) in tracker.Swipes)
            {
                int2 cell = M3Renderer.CellAt(ScreenToWorld(from));
                if (M3Board.InBounds(cell)) TrySwap(cell, GestureTracker.SwipeDirection(from, to));
            }
            foreach (var tap in tracker.Taps)
            {
                int2 cell = M3Renderer.CellAt(ScreenToWorld(tap));
                if (!M3Board.InBounds(cell)) continue;
                int2 sel = Renderer.Selected;
                if (M3Board.InBounds(sel) && math.csum(math.abs(cell - sel)) == 1) TrySwap(sel, cell - sel);
                else Renderer.Selected = cell;
            }
            // Record each completed turn once its tick ran (undo restores exactly).
            if (!InputLocked && board.Flow != M3Flow.Menu)
            {
                Session.Sync();
                int serial = board.LogSerial * 1000 + board.BoardSerial;
                if (serial != m_RecordedSerial) { History.Record(); m_RecordedSerial = serial; }
                m_Idle += Time.deltaTime;
                if (m_Idle > 6f && !M3Board.InBounds(Renderer.Hint)) ShowHint();
            }
            MenuPanel.gameObject.SetActive(board.Flow == M3Flow.Menu);
            bool over = (board.Flow == M3Flow.Won || board.Flow == M3Flow.Lost) && !Renderer.Busy;
            if (over && !EndPanel.gameObject.activeSelf && board.Flow == M3Flow.Won) m_Sound.Play(m_Win);
            EndPanel.gameObject.SetActive(over);
            EndText.text = board.Flow == M3Flow.Won ? "CLEARED!" : "OUT OF MOVES";
            StatsText.text = $"Score {board.Score} / {board.Target}     Moves {board.MovesLeft}";
            UIFactory.SetFill(ScoreFill, board.Target > 0 ? math.saturate(board.Score / (float)board.Target) : 0f);
            UndoButton.interactable = History.CanUndo && !InputLocked;
        }

        /// <summary>Renders into a target (tests, thumbnails).</summary>
        public void CameraLetterbox(RenderTexture target) => Camera.targetTexture = target;

        float2 ScreenToWorld(float2 screen)
        {
            var w = Camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 20f));
            return new float2(w.x, w.y);
        }
    }
}
