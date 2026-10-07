using SPF.Contracts;
using SPF.Runtime.Session;
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
        Text[] m_ChoiceLabels;
        /// <summary>Every choice label (upgrade × current level), built once: the level-up screen then allocates nothing.</summary>
        static readonly string[,] s_ChoiceText = BuildChoiceText(SvRules.Names);
        static readonly string[,] s_SwordChoiceText = BuildChoiceText(SvSwordRules.Names);

        static string[,] BuildChoiceText(string[] names)
        {
            var text = new string[SvGameState.UpgradeCount, SvGameState.MaxLevel + 1];
            for (int u = 0; u < SvGameState.UpgradeCount; u++)
                for (int level = 0; level <= SvGameState.MaxLevel; level++)
                    text[u, level] = level == 0 ? "NEW: " + names[u] : names[u] + "  " + level + " > " + (level + 1);
            return text;
        }
        SvFlow m_Flow = (SvFlow)255;

        public Canvas Canvas { get; private set; }
        public RectTransform HudPanel { get; private set; }
        public RectTransform MenuPanel { get; private set; }
        public RectTransform LevelUpPanel { get; private set; }
        public RectTransform DeadPanel { get; private set; }
        public Image HealthFill { get; private set; }
        public Image XpFill { get; private set; }
        public Image BeaconFill { get; private set; }
        public Button MenuButton { get; private set; }
        public Button HudMenuButton { get; private set; }
        public BufferText StatsText { get; private set; }
        public Text DeadText { get; private set; }
        public Button StartButton { get; private set; }
        public Button RestartButton { get; private set; }
        public Button[] ChoiceButtons { get; private set; }
        public VirtualJoystick Joystick { get; private set; }
        public TouchInputSource TouchInput { get; private set; }
        public MobileCombatHud MobileHud { get; private set; }

        public void Build(SvGameBootstrap game)
        {
            m_Game = game;
            Canvas = UIFactory.CreateCanvas(transform, "SurvivorUI");
            var root = Canvas.transform;
            bool mobile = game.Session.World.Resource(SvKeys.Config).MobileSkills;
            if (mobile)
            {
                MobileHud = gameObject.AddComponent<MobileCombatHud>();
                MobileHud.Build(root, new MobileSource(game), preferLandscape: false);
                MobileHud.Interrupted = () => { if (game.State != null) game.State.Input = default; };
                MobileHud.SkillCanceled = slot => { if (game.State != null) game.State.Input = SkillInput.CancelSlot(game.State.Input, slot, SvMobileSkills.Dash); };
                root = MobileHud.SafeRoot;
            }
            bool guard = game.Session.World.Resource(SvKeys.Config).Settings.Variant == SvVariant.GuardBeacon;
            bool swords = game.Session.World.Resource(SvKeys.Config).Settings.FlyingSwords.Enabled;

            HudPanel = UIFactory.Panel(root, "HudPanel", Color.clear, Vector2.zero, Vector2.one);
            // Keep telemetry legible against dense smooth-art crowds; passive background never captures input.
            if (guard && !mobile) UIFactory.Card(HudPanel, "StatsBackdrop", SanctuaryUiTheme.Ink,
                new Vector2(0f, 0.86f), Vector2.one, raycast: false);
            if (mobile)
            {
                var telemetry = UIFactory.Card(HudPanel, "MobileStatsBackdrop", SanctuaryUiTheme.Ink,
                    new Vector2(.015f, .972f), new Vector2(.75f, .972f), raycast: false);
                telemetry.offsetMin = new Vector2(0f, -204f);
                telemetry.offsetMax = Vector2.zero;
            }
            XpFill = UIFactory.Bar(HudPanel, "XpBar", SanctuaryUiTheme.Ink, SanctuaryUiTheme.Spirit, new Vector2(0f, mobile ? .992f : .975f), new Vector2(1f, 1f));
            HealthFill = UIFactory.Bar(HudPanel, "HealthBar", SanctuaryUiTheme.Surface, SanctuaryUiTheme.Coral, new Vector2(mobile ? .035f : .02f, mobile ? .943f : .93f), new Vector2(mobile ? .32f : .3f, mobile ? .955f : .96f));
            BeaconFill = UIFactory.Bar(HudPanel, "BeaconBar", SanctuaryUiTheme.Surface, SanctuaryUiTheme.Spirit, new Vector2(mobile ? .38f : .52f, mobile ? .943f : .93f), new Vector2(mobile ? .72f : .8f, mobile ? .955f : .96f));
            BeaconFill.transform.parent.gameObject.SetActive(guard);
            if (mobile)
            {
                var vitality = UIFactory.Label(HudPanel, "HealthLabel", "VITALITY", 13, TextAnchor.MiddleLeft, new Vector2(.035f, .955f), new Vector2(.32f, .976f));
                vitality.rectTransform.offsetMin = vitality.rectTransform.offsetMax = Vector2.zero; vitality.color = SanctuaryUiTheme.Muted;
                var sanctuary = UIFactory.Label(HudPanel, "BeaconLabel", "SANCTUARY", 13, TextAnchor.MiddleLeft, new Vector2(.38f, .955f), new Vector2(.72f, .976f));
                sanctuary.rectTransform.offsetMin = sanctuary.rectTransform.offsetMax = Vector2.zero; sanctuary.color = SanctuaryUiTheme.Muted;
                sanctuary.gameObject.SetActive(guard);
            }
            HudMenuButton = UIFactory.Button(HudPanel, "MenuButton", "MENU", new Vector2(-72, mobile ? -65 : -78), new Vector2(124, mobile ? 56 : 70), SanctuaryUiTheme.Surface, new Vector2(1f, 1f), mobile ? 19 : 24);
            HudMenuButton.onClick.AddListener(() => m_Game.BackToMenu());
            StatsText = BufferText.Create(HudPanel, "StatsText", mobile ? 22 : 30, TextAnchor.UpperLeft, new Vector2(0.015f, 0.75f), new Vector2(mobile ? .74f : .6f, 0.93f));
            if (mobile) Joystick = MobileHud.Joystick;
            else
            {
                var joystickArea = UIFactory.Panel(HudPanel, "Joystick", new Color(1f, 1f, 1f, 0.02f), Vector2.zero, new Vector2(1f, 0.7f));
                Joystick = joystickArea.gameObject.AddComponent<VirtualJoystick>();
                TouchInput = new TouchInputSource(Joystick);

            }

            LevelUpPanel = UIFactory.Panel(root, "LevelUpPanel", SanctuaryUiTheme.Ink, Vector2.zero, Vector2.one);
            UIFactory.Label(LevelUpPanel, "Title", "LEVEL UP", mobile ? 58 : 100, TextAnchor.MiddleCenter, new Vector2(0f, 0.7f), new Vector2(1f, 0.9f)).color = SanctuaryUiTheme.Ivory;
            UIFactory.Label(LevelUpPanel, "UpgradeHint", "CHOOSE ONE TO CONTINUE", mobile ? 18 : 24, TextAnchor.MiddleCenter, new Vector2(.1f, .68f), new Vector2(.9f, .74f)).color = SanctuaryUiTheme.Muted;
            ChoiceButtons = new Button[SvGameState.ChoiceCount];
            m_ChoiceLabels = new Text[SvGameState.ChoiceCount];
            for (int i = 0; i < ChoiceButtons.Length; i++)
            {
                int choice = i;
                ChoiceButtons[i] = UIFactory.Button(LevelUpPanel, "Choice" + i, "", new Vector2(0, 180 - i * 170), new Vector2(620, 140), SanctuaryUiTheme.Spirit, new Vector2(0.5f, 0.5f), mobile ? 30 : 36);
                ChoiceButtons[i].onClick.AddListener(() => m_Game.Choose(choice));
                m_ChoiceLabels[i] = ChoiceButtons[i].GetComponentInChildren<Text>();
            }

            DeadPanel = UIFactory.Panel(root, "DeadPanel", SanctuaryUiTheme.Ink, Vector2.zero, Vector2.one);
            DeadText = UIFactory.Label(DeadPanel, "DeadText", "", 60, TextAnchor.MiddleCenter, new Vector2(0f, 0.55f), new Vector2(1f, 0.85f));
            RestartButton = UIFactory.Button(DeadPanel, "RestartButton", "AGAIN", new Vector2(0, -60), new Vector2(460, 130), SanctuaryUiTheme.Spirit, new Vector2(0.5f, 0.5f));
            RestartButton.onClick.AddListener(() => m_Game.StartRun());

            MenuButton = UIFactory.Button(DeadPanel, "MenuButton", "MENU", new Vector2(0, -215), new Vector2(460, 100), SanctuaryUiTheme.Surface, new Vector2(0.5f, 0.5f), 32);
            MenuButton.onClick.AddListener(() => m_Game.BackToMenu());

            MenuPanel = UIFactory.Panel(root, "MenuPanel", new Color(.035f, .095f, .105f, .89f), Vector2.zero, Vector2.one);
            UIFactory.Label(MenuPanel, "Title", game.Session.World.HasResource(SvWeapons.Key) ? "WEAPON HORDE" : swords ? "FLYING SWORDS" : guard ? "BEACON GUARD" : "SURVIVE", mobile ? 64 : guard ? 85 : 140, TextAnchor.MiddleCenter, new Vector2(0f, 0.6f), new Vector2(1f, 0.85f));
            if (game.Session.World.HasResource(SvWeapons.Key)) UIFactory.Label(MenuPanel, "WeaponInstructions", "Auto-attack nearby enemies. Hold ATTACK to strike.\nSWITCH / Q cycles BLADE, SWORD, STAFF and BOW.\nPULSE / J and BLINK / K remain available.", 23, TextAnchor.MiddleCenter, new Vector2(.05f,.18f), new Vector2(.95f,.38f));
            if (guard && !game.Session.World.HasResource(SvWeapons.Key)) UIFactory.Label(MenuPanel, "Instructions", mobile ? "Move with the left stick. Tap PULSE to clear space.\nDrag BLINK to aim, release to jump.\nDrag far away to cancel. Protect the beacon." : "ORIGINAL EXAMPLE\nHold the beacon until waves end, then clear the horde.\nMove to intercept. Two electric bands damage on fixed ticks.", mobile ? 23 : 26, TextAnchor.MiddleCenter, new Vector2(0.07f, 0.2f), new Vector2(0.93f, 0.42f));
            if (swords) UIFactory.Label(MenuPanel, "Instructions", "Move to guide your orbiting sword swarm.\nSwords seek, pierce and return. Collect gems to grow.\nPULSE clears space. Drag BLINK to escape.\nSurvive the waves, then clear the horde.", 24, TextAnchor.MiddleCenter, new Vector2(.05f, .18f), new Vector2(.95f, .40f));
            StartButton = UIFactory.Button(MenuPanel, "StartButton", "START", new Vector2(0, -20), new Vector2(460, 140), SanctuaryUiTheme.Spirit, new Vector2(0.5f, 0.5f));
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
                DeadPanel.gameObject.SetActive(state.Flow == SvFlow.Dead || state.Flow == SvFlow.Won);
            }
            UIFactory.SetFill(HealthFill, state.MaxHp > 0f ? state.Hp / state.MaxHp : 0f);
            UIFactory.SetFill(BeaconFill, state.BeaconHp / Mathf.Max(1f, state.BeaconMaxHp));
            UIFactory.SetFill(XpFill, state.Xp / (float)Mathf.Max(SvRules.XpToNext(s, state.Level), 1));
            var world = m_Game.Session.World;
            int seconds = (int)state.Time;
            // Rebuilt every frame without allocating; a string is made only when a shown number changes.
            var stats = StatsText.Begin().Append(seconds / 60, 2).Append(':').Append(seconds % 60, 2).Append("   Lv ").Append(state.Level).Append("   Kills ").Append(state.Kills)
                .Append("\nEnemies ").Append(world.Table(SvKeys.Enemy).Count).Append("   Bullets ").Append(world.Table(SvKeys.Bullet).Count);
            if (s.Variant == SvVariant.GuardBeacon)
                stats.Append("\nBEACON ").Append((int)state.BeaconHp).Append(" / ").Append((int)state.BeaconMaxHp)
                    .Append(state.RunTicks < s.GuardDurationTicks ? "   HOLD " : "   CLEAR THE HORDE ")
                    .Append(Mathf.Max(0, (s.GuardDurationTicks - state.RunTicks + 29) / 30));
            if (s.FlyingSwords.Enabled)
                stats.Append("\nSWORDS ").Append(world.Resource(SvFlyingSwordState.Key).ActiveCount)
                    .Append(state.RunTicks < s.FlyingSwords.WaveTicks ? "   WAVES " : "   CLEAR THE HORDE ")
                    .Append(Mathf.Max(0, (s.FlyingSwords.WaveTicks - state.RunTicks + 29) / 30));
            if (world.HasResource(SvWeapons.Key))
            {
                var weapons = world.Resource(SvWeapons.Key); stats.Append("\nWEAPON ").Append(weapons.Current.Name);
                if (weapons.Equipment.PendingId != 0) stats.Append(" > ").Append(weapons.Profile(weapons.Equipment.PendingId).Name);
            }
            StatsText.Commit();
            // Choice and death texts are only visible on those screens: don't rebuild them for every gem picked up.
            if (state.Version == m_Version || state.Flow != SvFlow.LevelUp && state.Flow != SvFlow.Dead && state.Flow != SvFlow.Won) return;
            m_Version = state.Version;
            for (int i = 0; i < ChoiceButtons.Length; i++)
            {
                bool shown = i < state.ChoiceCountOffered;
                ChoiceButtons[i].gameObject.SetActive(shown);
                if (!shown) continue;
                int u = state.Choices[i];
                int level = Mathf.Clamp(state.Upgrades[u], 0, SvGameState.MaxLevel);
                m_ChoiceLabels[i].text = s.FlyingSwords.Enabled ? s_SwordChoiceText[u, level] : s_ChoiceText[u, level];   // prebuilt: offers can change every tick after a big pickup
            }
            if (state.Flow == SvFlow.Dead) DeadText.text = (state.LossReason == SvLossReason.BeaconLost ? "BEACON LOST" : "YOU FELL") + $"\nsurvived {seconds / 60:00}:{seconds % 60:00}, {state.Kills} kills";
            if (state.Flow == SvFlow.Won) DeadText.text = (s.FlyingSwords.Enabled ? "HORDE CLEARED" : "BEACON SAVED") + $"\n{state.Kills} enemies cleared";
        }
        sealed class MobileSource : IMobileCombatHudSource, IMobileCombatHudGlyphSource
        {
            readonly SvGameBootstrap m_Game;
            public MobileSource(SvGameBootstrap game) => m_Game = game;
            public int SlotCount => m_Game.Session.World.HasResource(SvWeapons.Key) ? 4 : 2;
            public int TickRate => 30;
            public bool Playing => m_Game.Session != null && m_Game.Session.State == SessionState.Running && m_Game.State.Flow == SvFlow.Playing && !m_Game.InputRouter.Scripted.Active && !m_Game.AutoPlay;
            public string SlotLabel(int slot) => slot == 0 ? "PULSE / J" : slot == 1 ? "BLINK / K" : slot == 2 ? "ATTACK / F" : "SWITCH / Q";
            public SkillSlotSnapshot ReadSlot(int slot)
            {
                m_Game.Session.Sync();
                var world = m_Game.Session.World;
                return world.Resource(SvMobileSkills.Key).GetSnapshot(slot, Playing && m_Game.State.Hp > 0);
            }
            public int ReadFallbackGlyph(int slot, in SkillSlotSnapshot snapshot)
            {
                var world = m_Game.Session.World;
                if (!world.HasResource(SvWeapons.Key)) return snapshot.Definition.IconId;
                if (slot == SvWeapons.AttackButton) return CombatControlGraphic.WeaponGlyph(world.Resource(SvWeapons.Key).Current.Family);
                if (slot == SvWeapons.SwitchButton) return CombatControlGraphic.SwitchGlyph;
                return snapshot.Definition.IconId;
            }
        }
    }
}
