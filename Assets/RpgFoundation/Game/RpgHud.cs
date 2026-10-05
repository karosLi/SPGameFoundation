using SPF.Shell.Input;
using SPF.Shell.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace RpgFoundation.Game
{
    /// <summary>
    /// The RPG's UI, built in code from shell widgets: HUD (health / XP bars, stats, joystick, attack /
    /// fireball / potion buttons, bag), inventory panel, menu, floor-clear, death and victory panels,
    /// floating damage numbers.
    /// </summary>
    public sealed class RpgHud : MonoBehaviour
    {
        RpgGameBootstrap m_Game;
        int m_Version = -1;
        RpgFlow m_Flow = (RpgFlow)255;
        float m_NextText;

        public Canvas Canvas { get; private set; }
        public RectTransform HudPanel { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform ClearPanel { get; private set; }
        public RectTransform DeadPanel { get; private set; }
        public RectTransform VictoryPanel { get; private set; }
        public RectTransform BagPanel { get; private set; }
        public Image HealthFill { get; private set; }
        public Image XpFill { get; private set; }
        public Text StatsText { get; private set; }
        public Text MessageText { get; private set; }
        public Text ClearText { get; private set; }
        public Text EquippedText { get; private set; }
        public VirtualJoystick Joystick { get; private set; }
        public HoldButton AttackButton { get; private set; }
        public TapButton SkillButton { get; private set; }
        public TapButton PotionButton { get; private set; }
        public Button BagButton { get; private set; }
        public Button NewGameButton { get; private set; }
        public Button ContinueButton { get; private set; }
        public Button DescendButton { get; private set; }
        public Button RetryButton { get; private set; }
        public Button DeadMenuButton { get; private set; }
        public Button VictoryMenuButton { get; private set; }
        public Button[] BagSlots { get; private set; }
        public TouchInputSource TouchInput { get; private set; }
        public FloatingTextPool FloatingText { get; private set; }

        public void Build(RpgGameBootstrap game, UnityEngine.Camera camera)
        {
            m_Game = game;
            Canvas = UIFactory.CreateCanvas(transform, "RpgUI");
            var root = Canvas.transform;

            FloatingText = gameObject.AddComponent<FloatingTextPool>();
            FloatingText.Camera = camera;
            FloatingText.Build(root);
            game.WorldRenderer.Feedback = OnFeedback;

            // HUD
            HudPanel = UIFactory.Panel(root, "HudPanel", Color.clear, Vector2.zero, Vector2.one);
            HealthFill = UIFactory.Bar(HudPanel, "HealthBar", new Color(0f, 0f, 0f, 0.55f), new Color(0.85f, 0.2f, 0.25f, 0.95f), new Vector2(0.02f, 0.93f), new Vector2(0.32f, 0.97f));
            XpFill = UIFactory.Bar(HudPanel, "XpBar", new Color(0f, 0f, 0f, 0.55f), new Color(0.95f, 0.8f, 0.25f, 0.95f), new Vector2(0.02f, 0.905f), new Vector2(0.32f, 0.92f));
            StatsText = UIFactory.Label(HudPanel, "StatsText", "", 30, TextAnchor.UpperLeft, new Vector2(0.01f, 0.7f), new Vector2(0.5f, 0.9f));
            MessageText = UIFactory.Label(HudPanel, "MessageText", "", 40, TextAnchor.UpperCenter, new Vector2(0.25f, 0.82f), new Vector2(0.75f, 0.97f));
            var joystickArea = UIFactory.Panel(HudPanel, "Joystick", new Color(1f, 1f, 1f, 0.02f), new Vector2(0f, 0f), new Vector2(0.45f, 0.65f));
            Joystick = joystickArea.gameObject.AddComponent<VirtualJoystick>();
            var attack = UIFactory.Button(HudPanel, "AttackButton", "ATK", new Vector2(-170, 170), new Vector2(220, 220), new Color(0.9f, 0.4f, 0.3f, 0.5f), new Vector2(1f, 0f));
            AttackButton = attack.gameObject.AddComponent<HoldButton>();
            var skill = UIFactory.Button(HudPanel, "SkillButton", "FIRE", new Vector2(-420, 120), new Vector2(160, 160), new Color(1f, 0.6f, 0.2f, 0.5f), new Vector2(1f, 0f), 34);
            SkillButton = skill.gameObject.AddComponent<TapButton>();
            var potion = UIFactory.Button(HudPanel, "PotionButton", "POT", new Vector2(-170, 420), new Vector2(150, 150), new Color(0.9f, 0.25f, 0.4f, 0.5f), new Vector2(1f, 0f), 34);
            PotionButton = potion.gameObject.AddComponent<TapButton>();
            BagButton = UIFactory.Button(HudPanel, "BagButton", "BAG", new Vector2(-110, -70), new Vector2(180, 100), new Color(0.4f, 0.4f, 0.55f, 0.85f), new Vector2(1f, 1f), 34);
            BagButton.onClick.AddListener(() => BagPanel.gameObject.SetActive(!BagPanel.gameObject.activeSelf));
            TouchInput = new TouchInputSource(Joystick).Hold(AttackButton, RpgButton.Attack).Tap(SkillButton, RpgButton.Skill).Tap(PotionButton, RpgButton.Potion);

            // Bag (inventory)
            BagPanel = UIFactory.Panel(root, "BagPanel", new Color(0.05f, 0.05f, 0.08f, 0.9f), new Vector2(0.55f, 0.15f), new Vector2(0.98f, 0.85f));
            UIFactory.Label(BagPanel, "Title", "BAG  (tap to equip)", 36, TextAnchor.UpperCenter, new Vector2(0f, 0.88f), new Vector2(1f, 1f));
            EquippedText = UIFactory.Label(BagPanel, "Equipped", "", 28, TextAnchor.UpperLeft, new Vector2(0f, 0.74f), new Vector2(1f, 0.88f));
            BagSlots = new Button[HeroProfile.MaxInventory];
            for (int i = 0; i < BagSlots.Length; i++)
            {
                int slot = i;
                int col = i % 2, row = i / 2;
                var b = UIFactory.Button(BagPanel, "Slot" + i, "", new Vector2(-195 + col * 390, -260 - row * 92), new Vector2(380, 84), new Color(0.25f, 0.22f, 0.35f, 0.95f), new Vector2(0.5f, 1f), 24);
                b.onClick.AddListener(() => m_Game.Equip(slot));
                BagSlots[i] = b;
            }
            BagPanel.gameObject.SetActive(false);

            // Menu
            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", "DUNGEON", 140, TextAnchor.MiddleCenter, new Vector2(0f, 0.62f), new Vector2(1f, 0.88f));
            NewGameButton = UIFactory.Button(MenuPanel, "NewGameButton", "NEW GAME", new Vector2(0, -10), new Vector2(460, 130), new Color(0.3f, 0.75f, 0.45f, 0.95f), new Vector2(0.5f, 0.5f));
            NewGameButton.onClick.AddListener(() => m_Game.NewGame());
            ContinueButton = UIFactory.Button(MenuPanel, "ContinueButton", "CONTINUE", new Vector2(0, -170), new Vector2(460, 110), new Color(0.35f, 0.45f, 0.7f, 0.95f), new Vector2(0.5f, 0.5f));
            ContinueButton.onClick.AddListener(() => m_Game.Continue());

            // Floor cleared
            ClearPanel = UIFactory.Panel(root, "ClearPanel", new Color(0f, 0.05f, 0.08f, 0.6f), Vector2.zero, Vector2.one);
            ClearText = UIFactory.Label(ClearPanel, "ClearText", "", 56, TextAnchor.MiddleCenter, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            DescendButton = UIFactory.Button(ClearPanel, "DescendButton", "DESCEND", new Vector2(0, -60), new Vector2(460, 130), new Color(0.3f, 0.65f, 0.85f, 0.95f), new Vector2(0.5f, 0.5f));
            DescendButton.onClick.AddListener(() => m_Game.Descend());

            // Dead
            DeadPanel = UIFactory.Panel(root, "DeadPanel", new Color(0.12f, 0f, 0f, 0.6f), Vector2.zero, Vector2.one);
            UIFactory.Label(DeadPanel, "DeadText", "YOU DIED", 90, TextAnchor.MiddleCenter, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            RetryButton = UIFactory.Button(DeadPanel, "RetryButton", "RETRY FLOOR", new Vector2(0, -40), new Vector2(480, 130), new Color(0.75f, 0.35f, 0.3f, 0.95f), new Vector2(0.5f, 0.5f));
            RetryButton.onClick.AddListener(() => m_Game.Retry());
            DeadMenuButton = UIFactory.Button(DeadPanel, "DeadMenuButton", "MENU", new Vector2(0, -200), new Vector2(480, 110), new Color(0.35f, 0.4f, 0.6f, 0.95f), new Vector2(0.5f, 0.5f));
            DeadMenuButton.onClick.AddListener(() => m_Game.BackToMenu());

            // Victory
            VictoryPanel = UIFactory.Panel(root, "VictoryPanel", new Color(0.05f, 0.08f, 0f, 0.6f), Vector2.zero, Vector2.one);
            UIFactory.Label(VictoryPanel, "VictoryText", "VICTORY", 120, TextAnchor.MiddleCenter, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            VictoryMenuButton = UIFactory.Button(VictoryPanel, "VictoryMenuButton", "MENU", new Vector2(0, -60), new Vector2(460, 130), new Color(0.35f, 0.6f, 0.4f, 0.95f), new Vector2(0.5f, 0.5f));
            VictoryMenuButton.onClick.AddListener(() => m_Game.BackToMenu());
        }

        void OnFeedback(FeedbackEvent e)
        {
            switch (e.Kind)
            {
                case FeedbackKind.Damage: FloatingText.Spawn(e.Position, e.Value.ToString("0"), new Color(1f, 0.95f, 0.85f)); break;
                case FeedbackKind.Crit: FloatingText.Spawn(e.Position, e.Value.ToString("0") + "!", new Color(1f, 0.75f, 0.2f)); break;
                case FeedbackKind.HeroHurt: FloatingText.Spawn(e.Position, "-" + e.Value.ToString("0"), new Color(1f, 0.3f, 0.3f)); break;
                case FeedbackKind.Heal: FloatingText.Spawn(e.Position, "+" + e.Value.ToString("0"), new Color(0.4f, 1f, 0.5f)); break;
                case FeedbackKind.Gold: FloatingText.Spawn(e.Position, "+" + e.Value.ToString("0") + "g", new Color(1f, 0.85f, 0.3f)); break;
                case FeedbackKind.Item: FloatingText.Spawn(e.Position, e.Value > 0 ? "GEAR" : "POTION", new Color(0.8f, 0.6f, 1f)); break;
                case FeedbackKind.LevelUp: FloatingText.Spawn(e.Position, "LEVEL " + e.Value.ToString("0") + "!", new Color(1f, 1f, 0.5f)); break;
            }
        }

        void Update()
        {
            var state = m_Game != null ? m_Game.State : null;
            if (state == null) return;
            var session = m_Game.Session;
            var world = session.World;

            if (state.Flow != m_Flow)
            {
                m_Flow = state.Flow;
                m_Version = -1;
                bool playing = state.Flow == RpgFlow.Playing;
                HudPanel.gameObject.SetActive(state.Flow != RpgFlow.Menu);
                MenuPanel.gameObject.SetActive(state.Flow == RpgFlow.Menu);
                ClearPanel.gameObject.SetActive(state.Flow == RpgFlow.FloorClear);
                DeadPanel.gameObject.SetActive(state.Flow == RpgFlow.Dead);
                VictoryPanel.gameObject.SetActive(state.Flow == RpgFlow.Victory);
                if (!playing) BagPanel.gameObject.SetActive(false);
                ContinueButton.interactable = m_Game.HasSave;
            }

            var profile = state.Profile;
            if (world.Registry.TryResolve(state.Hero, out _, out int row))
            {
                session.Sync();
                UIFactory.SetFill(HealthFill, world.Column(RpgKeys.Health)[row].Fraction);
            }
            var levels = m_Game.Runtime.Settings.Levels;
            UIFactory.SetFill(XpFill, profile.Level >= levels.MaxLevel ? 1f : profile.Xp / (float)math.max(levels.XpToNext(profile.Level), 1));

            if (state.Version != m_Version || Time.unscaledTime >= m_NextText)
            {
                m_Version = state.Version;
                m_NextText = Time.unscaledTime + 0.25f;
                RefreshTexts(state, world, row);
            }
        }

        void RefreshTexts(RpgGameState state, SPF.Runtime.World.SimWorld world, int heroRow)
        {
            var profile = state.Profile;
            var config = m_Game.Runtime;
            string hp = "";
            if (heroRow >= 0)
            {
                var h = world.Column(RpgKeys.Health)[heroRow];
                var s = world.Column(RpgKeys.Stats)[heroRow];
                hp = $"HP {h.Current:0}/{h.Max:0}   ATK {s[Stat.Attack]:0}   ARM {s[Stat.Armour]:0}";
            }
            StatsText.text = $"Floor {profile.Floor}   Lv {profile.Level}   {hp}\nGold {profile.Gold}   Potions {profile.Potions}   Monsters {state.MonstersAlive}/{state.FloorMonsters}";
            MessageText.text = state.Message;
            ClearText.text = state.Message + $"\nLevel {profile.Level}, gold {profile.Gold}";
            EquippedText.text = $"Weapon: {config.GearName(profile.Weapon)}\nArmour: {config.GearName(profile.Armour)}";
            for (int i = 0; i < BagSlots.Length; i++)
            {
                bool has = i < profile.Inventory.Count;
                BagSlots[i].gameObject.SetActive(has);
                if (has) UIFactory.SetText(BagSlots[i], config.GearName(profile.Inventory[i]));
            }
        }
    }
}
