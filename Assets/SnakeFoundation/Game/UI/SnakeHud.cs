using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SnakeFoundation.Game.UI
{
    /// <summary>
    /// Menu, in-game HUD (score, rank, region, leaderboard, touch controls) and game-over screen.
    /// Texts refresh at most a few times per second and only when the game state changed.
    /// Object names are stable so UI automation can find them.
    /// </summary>
    public sealed class SnakeHud : MonoBehaviour
    {
        const float RefreshInterval = 0.25f;

        SnakeGameBootstrap m_Game;
        readonly StringBuilder m_Builder = new StringBuilder(256);
        int m_ShownVersion = -1;
        float m_NextRefresh;
        GameFlow m_ShownFlow = (GameFlow)(-1);

        public Canvas Canvas { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform HudPanel { get; private set; }
        public RectTransform GameOverPanel { get; private set; }
        public Button StartButton { get; private set; }
        public Button SkinButton { get; private set; }
        public Button PlayAgainButton { get; private set; }
        public Button MenuButton { get; private set; }
        public Text ScoreText { get; private set; }
        public Text RankText { get; private set; }
        public Text RegionText { get; private set; }
        public Text LeaderboardText { get; private set; }
        public Text GameOverText { get; private set; }
        public VirtualJoystick Joystick { get; private set; }
        public HoldButton BoostButton { get; private set; }
        public TapButton SkillButton { get; private set; }

        public void Build(SnakeGameBootstrap game)
        {
            m_Game = game;
            Canvas = UIFactory.CreateCanvas(transform);
            var root = Canvas.transform;

            // HUD
            HudPanel = UIFactory.Panel(root, "HudPanel", Color.clear, Vector2.zero, Vector2.one);
            ScoreText = UIFactory.Label(HudPanel, "ScoreText", "", 44, TextAnchor.UpperLeft, new Vector2(0f, 0.8f), new Vector2(0.5f, 1f));
            RankText = UIFactory.Label(HudPanel, "RankText", "", 32, TextAnchor.UpperLeft, new Vector2(0f, 0.72f), new Vector2(0.5f, 0.86f));
            RegionText = UIFactory.Label(HudPanel, "RegionText", "", 32, TextAnchor.UpperCenter, new Vector2(0.3f, 0.9f), new Vector2(0.7f, 1f));
            LeaderboardText = UIFactory.Label(HudPanel, "LeaderboardText", "", 30, TextAnchor.UpperRight, new Vector2(0.6f, 0.6f), new Vector2(1f, 1f));

            // Touch controls: joystick on the left half, boost / skill buttons on the right.
            var joystickArea = UIFactory.Panel(HudPanel, "Joystick", new Color(1f, 1f, 1f, 0.02f), new Vector2(0f, 0f), new Vector2(0.5f, 0.6f));
            Joystick = joystickArea.gameObject.AddComponent<VirtualJoystick>();
            var boost = UIFactory.Button(HudPanel, "BoostButton", "BOOST", new Vector2(-170, 170), new Vector2(220, 220), new Color(1f, 0.5f, 0.2f, 0.45f), new Vector2(1f, 0f));
            BoostButton = boost.gameObject.AddComponent<HoldButton>();
            var skill = UIFactory.Button(HudPanel, "SkillButton", "FIRE", new Vector2(-420, 130), new Vector2(160, 160), new Color(0.4f, 0.6f, 1f, 0.45f), new Vector2(1f, 0f));
            SkillButton = skill.gameObject.AddComponent<TapButton>();
            Joystick.Boost = BoostButton;
            Joystick.Skill = SkillButton;

            // Menu
            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(0f, 0f, 0f, 0.45f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", "SNAKE", 140, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            StartButton = UIFactory.Button(MenuPanel, "StartButton", "PLAY", new Vector2(0, -20), new Vector2(420, 130), new Color(0.3f, 0.8f, 0.45f, 0.95f), new Vector2(0.5f, 0.5f));
            SkinButton = UIFactory.Button(MenuPanel, "SkinButton", "SKIN", new Vector2(0, -180), new Vector2(420, 110), new Color(0.35f, 0.4f, 0.6f, 0.95f), new Vector2(0.5f, 0.5f));
            StartButton.onClick.AddListener(() => m_Game.StartGame());
            SkinButton.onClick.AddListener(() => { m_Game.CycleSkin(); m_ShownVersion = -1; });

            // Game over
            GameOverPanel = UIFactory.Panel(root, "GameOverPanel", new Color(0.1f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one);
            GameOverText = UIFactory.Label(GameOverPanel, "GameOverText", "", 56, TextAnchor.MiddleCenter, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            PlayAgainButton = UIFactory.Button(GameOverPanel, "PlayAgainButton", "PLAY AGAIN", new Vector2(0, -40), new Vector2(460, 130), new Color(0.3f, 0.8f, 0.45f, 0.95f), new Vector2(0.5f, 0.5f));
            MenuButton = UIFactory.Button(GameOverPanel, "MenuButton", "MENU", new Vector2(0, -200), new Vector2(460, 110), new Color(0.35f, 0.4f, 0.6f, 0.95f), new Vector2(0.5f, 0.5f));
            PlayAgainButton.onClick.AddListener(() => m_Game.PlayAgain());
            MenuButton.onClick.AddListener(() => m_Game.BackToMenu());

            Refresh(force: true);
        }

        void LateUpdate()
        {
            if (m_Game == null) return;
            Refresh(force: false);
        }

        void Refresh(bool force)
        {
            var game = m_Game.State;
            if (game == null) return;

            if (force || game.Flow != m_ShownFlow)
            {
                m_ShownFlow = game.Flow;
                MenuPanel.gameObject.SetActive(game.Flow == GameFlow.Attract);
                HudPanel.gameObject.SetActive(game.Flow == GameFlow.Playing);
                GameOverPanel.gameObject.SetActive(game.Flow == GameFlow.GameOver);
                m_ShownVersion = -1;
            }

            if (!force && (game.Version == m_ShownVersion || Time.unscaledTime < m_NextRefresh))
                return;
            m_ShownVersion = game.Version;
            m_NextRefresh = Time.unscaledTime + RefreshInterval;

            switch (game.Flow)
            {
                case GameFlow.Attract:
                    UIFactory.SetText(SkinButton, "SKIN: " + m_Game.SkinName(game.PlayerSkin));
                    break;
                case GameFlow.Playing:
                    ScoreText.text = m_Builder.Clear().Append("Mass ").Append((int)game.PlayerMass)
                        .Append("   Length ").Append((int)game.PlayerLength).ToString();
                    RankText.text = m_Builder.Clear().Append("Rank ").Append(game.PlayerRank).Append(" / ").Append(game.AliveSnakes)
                        .Append("   Kills ").Append(game.PlayerKills).ToString();
                    RegionText.text = m_Game.RegionName(game.ActiveRegion);
                    m_Builder.Clear();
                    for (int i = 0; i < game.LeaderboardCount; i++)
                    {
                        var e = game.Leaderboard[i];
                        m_Builder.Append(i + 1).Append(". ").Append(e.IsPlayer ? game.PlayerName : m_Game.SnakeName(e.SnakeId))
                            .Append("  ").Append((int)e.Mass).Append('\n');
                    }
                    LeaderboardText.text = m_Builder.ToString();
                    break;
                case GameFlow.GameOver:
                    m_Builder.Clear().Append(game.LastDeathCause switch
                    {
                        DeathCause.Wall => "You hit the wall",
                        DeathCause.HeadOn => "Head-on crash",
                        _ => "Eaten",
                    });
                    if (game.LastKillerId != 0)
                        m_Builder.Append(" by ").Append(m_Game.SnakeName(game.LastKillerId));
                    m_Builder.Append("\nBest mass ").Append((int)game.BestMass)
                        .Append("   Kills ").Append(game.PlayerKills)
                        .Append("   Time ").Append((int)game.SurvivalSeconds).Append('s');
                    GameOverText.text = m_Builder.ToString();
                    break;
            }
        }
    }
}
