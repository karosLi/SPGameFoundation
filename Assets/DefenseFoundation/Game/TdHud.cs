using SPF.Shell.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace DefenseFoundation.Game
{
    /// <summary>HUD: gold / lives / wave, next-wave button, build panel (tower choice with costs), tower panel (upgrade, sell), menu and end screens.</summary>
    public sealed class TdHud : MonoBehaviour
    {
        static readonly string[] Names = { "ARROW", "CANNON", "FROST" };
        TdGameBootstrap m_Game;
        int m_Version = -1;
        int2 m_Selected = new int2(-2);
        TdFlow m_Flow = (TdFlow)255;

        public RectTransform HudPanel { get; private set; }
        public RectTransform BuildPanel { get; private set; }
        public RectTransform TowerPanel { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform EndPanel { get; private set; }
        public Text StatsText { get; private set; }
        public Text EndText { get; private set; }
        public Button NextWaveButton { get; private set; }
        public Button[] BuildButtons { get; private set; }
        public Button UpgradeButton { get; private set; }
        public Button SellButton { get; private set; }
        public Button StartButton { get; private set; }
        public Button AgainButton { get; private set; }

        public void Build(TdGameBootstrap game)
        {
            m_Game = game;
            var root = UIFactory.CreateCanvas(transform, "DefenseUI").transform;
            HudPanel = UIFactory.Panel(root, "HudPanel", Color.clear, Vector2.zero, Vector2.one, raycast: false);
            StatsText = UIFactory.Label(HudPanel, "StatsText", "", 34, TextAnchor.UpperLeft, new Vector2(0.01f, 0.88f), new Vector2(0.7f, 0.99f));
            NextWaveButton = UIFactory.Button(HudPanel, "NextWaveButton", "NEXT WAVE", new Vector2(-170, -70), new Vector2(300, 100), new Color(0.8f, 0.45f, 0.25f, 0.9f), new Vector2(1f, 1f), 32);
            NextWaveButton.onClick.AddListener(() => m_Game.NextWave());

            BuildPanel = UIFactory.Panel(root, "BuildPanel", new Color(0f, 0f, 0f, 0.6f), new Vector2(0.15f, 0f), new Vector2(0.85f, 0.14f));
            BuildButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                var kind = (TowerKind)i;
                BuildButtons[i] = UIFactory.Button(BuildPanel, "Build" + Names[i], Names[i], new Vector2(-330 + i * 330, 0), new Vector2(300, 110), new Color(0.3f, 0.45f, 0.7f, 0.95f), new Vector2(0.5f, 0.5f), 30);
                BuildButtons[i].onClick.AddListener(() => m_Game.Build(kind));
            }
            TowerPanel = UIFactory.Panel(root, "TowerPanel", new Color(0f, 0f, 0f, 0.6f), new Vector2(0.25f, 0f), new Vector2(0.75f, 0.14f));
            UpgradeButton = UIFactory.Button(TowerPanel, "UpgradeButton", "UPGRADE", new Vector2(-170, 0), new Vector2(300, 110), new Color(0.35f, 0.65f, 0.35f, 0.95f), new Vector2(0.5f, 0.5f), 30);
            UpgradeButton.onClick.AddListener(() => m_Game.Upgrade());
            SellButton = UIFactory.Button(TowerPanel, "SellButton", "SELL", new Vector2(170, 0), new Vector2(300, 110), new Color(0.7f, 0.35f, 0.3f, 0.95f), new Vector2(0.5f, 0.5f), 30);
            SellButton.onClick.AddListener(() => m_Game.Sell());

            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", "HOLD THE LINE", 110, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            StartButton = UIFactory.Button(MenuPanel, "StartButton", "START", new Vector2(0, -20), new Vector2(460, 140), new Color(0.3f, 0.75f, 0.45f, 0.95f), new Vector2(0.5f, 0.5f));
            StartButton.onClick.AddListener(() => m_Game.StartGame());
            EndPanel = UIFactory.Panel(root, "EndPanel", new Color(0f, 0f, 0f, 0.6f), Vector2.zero, Vector2.one);
            EndText = UIFactory.Label(EndPanel, "EndText", "", 100, TextAnchor.MiddleCenter, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            AgainButton = UIFactory.Button(EndPanel, "AgainButton", "AGAIN", new Vector2(0, -40), new Vector2(460, 140), new Color(0.3f, 0.6f, 0.85f, 0.95f), new Vector2(0.5f, 0.5f));
            AgainButton.onClick.AddListener(() => m_Game.StartGame());
        }

        public void Refresh() => m_Version = -1;

        void Update()
        {
            var state = m_Game != null ? m_Game.State : null;
            if (state == null) return;
            if (state.Flow != m_Flow)
            {
                m_Flow = state.Flow;
                MenuPanel.gameObject.SetActive(state.Flow == TdFlow.Menu);
                EndPanel.gameObject.SetActive(state.Flow == TdFlow.Won || state.Flow == TdFlow.Lost);
                EndText.text = state.Flow == TdFlow.Won ? "VICTORY" : "THE LINE FELL";
                m_Version = -1;
            }
            bool playing = state.Flow == TdFlow.Building || state.Flow == TdFlow.Wave;
            HudPanel.gameObject.SetActive(playing);
            var rules = m_Game.Session.World.Resource(TdKeys.Rules);
            string phase = state.Flow == TdFlow.Building ? $"next wave in {math.max(state.BuildTimer, 0f):0}s" : $"wave {state.Wave + 1}/{rules.Waves.Count}";
            StatsText.text = $"Gold {state.Gold}   Lives {state.Lives}   {phase}";
            NextWaveButton.interactable = state.Flow == TdFlow.Building;
            int2 sel = m_Game.Selected;
            if (state.Version == m_Version && math.all(sel == m_Selected)) return;
            m_Version = state.Version;
            m_Selected = sel;
            var session = m_Game.Session;
            session.Sync();
            var map = session.World.Resource(TdKeys.Map);
            bool inMap = playing && map.AsView().InBounds(sel);
            bool tower = inMap && map[sel] == TdTile.Tower;
            BuildPanel.gameObject.SetActive(inMap && !tower);
            TowerPanel.gameObject.SetActive(tower);
            if (inMap && !tower)
                for (int i = 0; i < 3; i++)
                {
                    bool ok = TdQueries.CanBuild(session, sel, (TowerKind)i, out _);
                    BuildButtons[i].interactable = ok;
                    UIFactory.SetText(BuildButtons[i], $"{Names[i]} {rules.Towers[i].Cost}");
                }
            if (tower)
            {
                var towers = session.World.Column(TdKeys.TowerInfo);
                for (int i = 0; i < session.World.Table(TdKeys.Tower).Count; i++)
                    if (math.all(towers[i].Cell == sel))
                    {
                        var t = towers[i];
                        int cost = rules.UpgradeCost(t.Kind, t.Level);
                        UpgradeButton.interactable = t.Level < 3 && state.Gold >= cost;
                        UIFactory.SetText(UpgradeButton, t.Level < 3 ? $"UPGRADE {cost}" : "MAX");
                        UIFactory.SetText(SellButton, $"SELL {t.Invested * 7 / 10}");
                    }
            }
        }
    }
}
