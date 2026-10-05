using SPF.Shell.Input;
using SPF.Shell.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PlatformerFoundation.Game
{
    /// <summary>HUD: coins, lives and level, a left-hand stick and a jump button, and the flow screens.</summary>
    public sealed class PlHud : MonoBehaviour
    {
        PlGameBootstrap m_Game;
        PlFlow m_Flow = (PlFlow)255;
        int m_Version = -1;

        public RectTransform HudPanel { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform ClearPanel { get; private set; }
        public RectTransform OverPanel { get; private set; }
        public BufferText StatsText { get; private set; }
        public Text OverText { get; private set; }
        public Button StartButton { get; private set; }
        public Button NextButton { get; private set; }
        public Button RetryButton { get; private set; }
        public HoldButton JumpButton { get; private set; }
        public VirtualJoystick Joystick { get; private set; }
        public TouchInputSource TouchInput { get; private set; }

        public void Build(PlGameBootstrap game)
        {
            m_Game = game;
            var canvas = UIFactory.CreateCanvas(transform, "PlatformerUI");
            var root = canvas.transform;
            HudPanel = UIFactory.Panel(root, "HudPanel", Color.clear, Vector2.zero, Vector2.one);
            StatsText = BufferText.Create(HudPanel, "StatsText", 36, TextAnchor.UpperLeft, new Vector2(0.02f, 0.8f), new Vector2(0.7f, 0.98f));
            var stick = UIFactory.Panel(HudPanel, "Joystick", new Color(1f, 1f, 1f, 0.03f), Vector2.zero, new Vector2(0.45f, 0.6f));
            Joystick = stick.gameObject.AddComponent<VirtualJoystick>();
            var jump = UIFactory.Button(HudPanel, "JumpButton", "JUMP", new Vector2(-200, 200), new Vector2(260, 260), new Color(0.3f, 0.6f, 0.95f, 0.5f), new Vector2(1f, 0f));
            JumpButton = jump.gameObject.AddComponent<HoldButton>();
            TouchInput = new TouchInputSource(Joystick).Hold(JumpButton, PlButton.Jump);

            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(0f, 0f, 0.1f, 0.45f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", "LEAP", 150, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            StartButton = UIFactory.Button(MenuPanel, "StartButton", "PLAY", new Vector2(0, -20), new Vector2(460, 140), new Color(0.3f, 0.75f, 0.45f, 0.95f), new Vector2(0.5f, 0.5f));
            StartButton.onClick.AddListener(() => m_Game.StartGame());

            ClearPanel = UIFactory.Panel(root, "ClearPanel", new Color(0f, 0.1f, 0f, 0.5f), Vector2.zero, Vector2.one);
            UIFactory.Label(ClearPanel, "Title", "LEVEL CLEAR", 110, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            NextButton = UIFactory.Button(ClearPanel, "NextButton", "NEXT", new Vector2(0, -20), new Vector2(460, 140), new Color(0.3f, 0.65f, 0.9f, 0.95f), new Vector2(0.5f, 0.5f));
            NextButton.onClick.AddListener(() => m_Game.NextLevel());

            OverPanel = UIFactory.Panel(root, "OverPanel", new Color(0.1f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one);
            OverText = UIFactory.Label(OverPanel, "OverText", "", 100, TextAnchor.MiddleCenter, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            RetryButton = UIFactory.Button(OverPanel, "RetryButton", "AGAIN", new Vector2(0, -40), new Vector2(460, 140), new Color(0.75f, 0.35f, 0.3f, 0.95f), new Vector2(0.5f, 0.5f));
            RetryButton.onClick.AddListener(() => m_Game.Retry());
        }

        void Update()
        {
            var state = m_Game != null ? m_Game.State : null;
            if (state == null) return;
            if (state.Flow != m_Flow)
            {
                m_Flow = state.Flow;
                MenuPanel.gameObject.SetActive(state.Flow == PlFlow.Menu);
                HudPanel.gameObject.SetActive(state.Flow == PlFlow.Playing || state.Flow == PlFlow.Dying);
                ClearPanel.gameObject.SetActive(state.Flow == PlFlow.LevelComplete);
                OverPanel.gameObject.SetActive(state.Flow == PlFlow.GameOver || state.Flow == PlFlow.Won);
                OverText.text = state.Flow == PlFlow.Won ? "YOU WIN!" : "GAME OVER";
            }
            if (state.Version == m_Version) return;
            m_Version = state.Version;
            StatsText.Begin().Append("Level ").Append(state.Level + 1).Append("   Coins ").Append(state.Coins).Append("   Lives ").Append(state.Lives);
            StatsText.Commit();
        }
    }
}
