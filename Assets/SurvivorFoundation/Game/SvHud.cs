using SPF.Shell.Input;
using SPF.Shell.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SurvivorFoundation.Game
{
    /// <summary>HUD: health and XP bars, run stats, joystick, level-up choices, game over and menu.</summary>
    public sealed class SvHud : MonoBehaviour
    {
        SvGameBootstrap m_Game;
        int m_Version = -1;
        SvFlow m_Flow = (SvFlow)255;

        public Canvas Canvas { get; private set; }
        public RectTransform HudPanel { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform LevelUpPanel { get; private set; }
        public RectTransform DeadPanel { get; private set; }
        public Image HealthFill { get; private set; }
        public Image XpFill { get; private set; }
        public Text StatsText { get; private set; }
        public Text DeadText { get; private set; }
        public Button StartButton { get; private set; }
        public Button RestartButton { get; private set; }
        public Button[] ChoiceButtons { get; private set; }
        public VirtualJoystick Joystick { get; private set; }
        public TouchInputSource TouchInput { get; private set; }

        public void Build(SvGameBootstrap game)
        {
            m_Game = game;
            Canvas = UIFactory.CreateCanvas(transform, "SurvivorUI");
            var root = Canvas.transform;

            HudPanel = UIFactory.Panel(root, "HudPanel", Color.clear, Vector2.zero, Vector2.one);
            XpFill = UIFactory.Bar(HudPanel, "XpBar", new Color(0f, 0f, 0f, 0.6f), new Color(0.35f, 0.75f, 1f, 0.95f), new Vector2(0f, 0.975f), new Vector2(1f, 1f));
            HealthFill = UIFactory.Bar(HudPanel, "HealthBar", new Color(0f, 0f, 0f, 0.55f), new Color(0.9f, 0.25f, 0.25f, 0.95f), new Vector2(0.02f, 0.93f), new Vector2(0.3f, 0.96f));
            StatsText = UIFactory.Label(HudPanel, "StatsText", "", 30, TextAnchor.UpperLeft, new Vector2(0.01f, 0.75f), new Vector2(0.6f, 0.925f));
            var joystickArea = UIFactory.Panel(HudPanel, "Joystick", new Color(1f, 1f, 1f, 0.02f), Vector2.zero, new Vector2(1f, 0.7f));
            Joystick = joystickArea.gameObject.AddComponent<VirtualJoystick>();
            TouchInput = new TouchInputSource(Joystick);

            LevelUpPanel = UIFactory.Panel(root, "LevelUpPanel", new Color(0f, 0f, 0.05f, 0.75f), Vector2.zero, Vector2.one);
            UIFactory.Label(LevelUpPanel, "Title", "LEVEL UP", 100, TextAnchor.MiddleCenter, new Vector2(0f, 0.7f), new Vector2(1f, 0.9f));
            ChoiceButtons = new Button[SvGameState.ChoiceCount];
            for (int i = 0; i < ChoiceButtons.Length; i++)
            {
                int choice = i;
                ChoiceButtons[i] = UIFactory.Button(LevelUpPanel, "Choice" + i, "", new Vector2(0, 180 - i * 170), new Vector2(620, 140), new Color(0.3f, 0.45f, 0.75f, 0.95f), new Vector2(0.5f, 0.5f), 36);
                ChoiceButtons[i].onClick.AddListener(() => m_Game.Choose(choice));
            }

            DeadPanel = UIFactory.Panel(root, "DeadPanel", new Color(0.15f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
            DeadText = UIFactory.Label(DeadPanel, "DeadText", "", 60, TextAnchor.MiddleCenter, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            RestartButton = UIFactory.Button(DeadPanel, "RestartButton", "AGAIN", new Vector2(0, -60), new Vector2(460, 130), new Color(0.75f, 0.35f, 0.3f, 0.95f), new Vector2(0.5f, 0.5f));
            RestartButton.onClick.AddListener(() => m_Game.StartRun());

            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", "SURVIVE", 140, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            StartButton = UIFactory.Button(MenuPanel, "StartButton", "START", new Vector2(0, -20), new Vector2(460, 140), new Color(0.3f, 0.75f, 0.45f, 0.95f), new Vector2(0.5f, 0.5f));
            StartButton.onClick.AddListener(() => m_Game.StartRun());

            foreach (var button in root.GetComponentsInChildren<Button>(true))
                button.onClick.AddListener(() => m_Game.Audio?.Player.Play(m_Game.Audio.Click));
        }

        void Update()
        {
            var state = m_Game != null ? m_Game.State : null;
            if (state == null) return;
            var s = m_Game.Session.World.Resource(SvKeys.Config).Settings;
            if (state.Flow != m_Flow)
            {
                m_Flow = state.Flow;
                m_Version = -1;
                MenuPanel.gameObject.SetActive(state.Flow == SvFlow.Menu);
                HudPanel.gameObject.SetActive(state.Flow != SvFlow.Menu);
                LevelUpPanel.gameObject.SetActive(state.Flow == SvFlow.LevelUp);
                DeadPanel.gameObject.SetActive(state.Flow == SvFlow.Dead);
            }
            UIFactory.SetFill(HealthFill, state.MaxHp > 0f ? state.Hp / state.MaxHp : 0f);
            UIFactory.SetFill(XpFill, state.Xp / (float)Mathf.Max(SvRules.XpToNext(s, state.Level), 1));
            var world = m_Game.Session.World;
            int seconds = (int)state.Time;
            StatsText.text = $"{seconds / 60:00}:{seconds % 60:00}   Lv {state.Level}   Kills {state.Kills}\nEnemies {world.Table(SvKeys.Enemy).Count}   Bullets {world.Table(SvKeys.Bullet).Count}";
            if (state.Version == m_Version) return;
            m_Version = state.Version;
            for (int i = 0; i < ChoiceButtons.Length; i++)
            {
                bool shown = i < state.ChoiceCountOffered;
                ChoiceButtons[i].gameObject.SetActive(shown);
                if (!shown) continue;
                int u = state.Choices[i];
                int level = state.Upgrades[u];
                UIFactory.SetText(ChoiceButtons[i], level == 0 ? $"NEW: {SvRules.Names[u]}" : $"{SvRules.Names[u]}  {level} > {level + 1}");
            }
            DeadText.text = $"YOU FELL\nsurvived {seconds / 60:00}:{seconds % 60:00}, {state.Kills} kills";
        }
    }
}
