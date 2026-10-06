using System;
using SPF.Contracts;
using SPF.Shell.Input;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace SPF.Shell.UI
{
    public interface IMobileCombatHudSource
    {
        int SlotCount { get; }
        int TickRate { get; }
        bool Playing { get; }
        SkillSlotSnapshot ReadSlot(int slot);
        string SlotLabel(int slot);
    }

    /// <summary>Reusable, safe-area-aware view and input adapter. Skills, their rules, enabled state,
    /// charges and cooldown all come from the simulation snapshot supplied by the game adapter.</summary>
    [DefaultExecutionOrder(-1150)]
    public sealed class MobileCombatHud : MonoBehaviour
    {
        IMobileCombatHudSource m_Source;
        CanvasScaler m_Scaler;
        RectTransform m_Controls, m_StickRing, m_StickKnob;
        CombatControlGraphic[] m_Rings, m_Icons;
        BufferText[] m_Status;
        Text[] m_Labels;
        Image[] m_AssetIcons;
        int[] m_BoundIcons, m_BoundSkills;
        /// <summary>Optional project asset catalog, set before Build. Missing icons use vector fallback art.</summary>
        public Func<int, Sprite> IconResolver;
        bool m_Playing, m_Paused, m_FocusLost;
        int m_Width, m_Height;
        Rect m_LastSafe;
        public bool PreferredLandscape { get; private set; }
        public RectTransform SafeRoot { get; private set; }
        public VirtualJoystick Joystick { get; private set; }
        public SkillControl[] Buttons { get; private set; }
        public IInputSource Input { get; private set; }
        /// <summary>Game adapter clears its latched InputFrame here; canceled UI cannot leak a pre-pause press.</summary>
        public Action Interrupted;
        /// <summary>A single canceled slot; adapters preserve independently latched commands and movement.</summary>
        public Action<int> SkillCanceled;

        public void Build(Transform canvasRoot, IMobileCombatHudSource source, bool preferLandscape)
        {
            if (m_Source != null) throw new InvalidOperationException("HUD is already built.");
            if (source == null || source.SlotCount < 1 || source.SlotCount > 4 || source.TickRate < 1) throw new ArgumentException("HUD requires 1–4 slots.");
            m_Source = source; PreferredLandscape = preferLandscape;
            m_Scaler = canvasRoot.GetComponent<CanvasScaler>();
            SafeRoot = UIFactory.Panel(canvasRoot, "MobileSafeArea", Color.clear, Vector2.zero, Vector2.one, false);
            m_Controls = UIFactory.Panel(SafeRoot, "CombatControls", Color.clear, Vector2.zero, Vector2.one, false);
            var stickArea = UIFactory.Panel(m_Controls, "MoveTouchArea", new Color(0, 0, 0, .001f), Vector2.zero, new Vector2(.48f, .5f));
            Joystick = stickArea.gameObject.AddComponent<VirtualJoystick>();
            var ring = Graphic(m_Controls, "JoystickRing", new Vector2(150, 165), new Vector2(180, 180), Vector2.zero, new Color(.72f, .86f, .88f, .7f));
            m_StickRing = ring.rectTransform;
            var disc = Graphic(m_StickRing, "JoystickBase", Vector2.zero, new Vector2(170, 170), new Vector2(.5f, .5f), new Color(.035f, .075f, .10f, .42f));
            disc.Disc = true;
            var knob = Graphic(m_StickRing, "JoystickKnob", Vector2.zero, new Vector2(65, 65), new Vector2(.5f, .5f), new Color(.6f, .88f, .91f, .9f));
            knob.Disc = true; m_StickKnob = knob.rectTransform;
            Buttons = new SkillControl[source.SlotCount]; m_Rings = new CombatControlGraphic[Buttons.Length];
            m_Icons = new CombatControlGraphic[Buttons.Length];
            m_Status = new BufferText[Buttons.Length]; m_Labels = new Text[Buttons.Length];
            m_AssetIcons = new Image[Buttons.Length]; m_BoundIcons = new int[Buttons.Length]; m_BoundSkills = new int[Buttons.Length];
            for (int i = 0; i < Buttons.Length; i++)
            {
                var back = Graphic(m_Controls, "Skill" + i, Vector2.zero, new Vector2(142, 142), new Vector2(1, 0), new Color(.06f, .11f, .16f, .9f));
                back.Disc = true; back.raycastTarget = true;
                Buttons[i] = back.gameObject.AddComponent<SkillControl>();
                int slot = i;
                Buttons[i].Canceled = () => SkillCanceled?.Invoke(slot);
                m_Rings[i] = Graphic(back.transform, "Recharge", Vector2.zero, new Vector2(142, 142), new Vector2(.5f, .5f), Color.white);
                m_Icons[i] = Graphic(back.transform, "Icon", new Vector2(0, 8), new Vector2(84, 84), new Vector2(.5f, .5f), Color.white);
                var asset = UIFactory.Panel(back.transform, "AssetIcon", Color.clear, new Vector2(.2f, .27f), new Vector2(.8f, .87f), false);
                m_AssetIcons[i] = asset.gameObject.AddComponent<Image>(); m_AssetIcons[i].raycastTarget = false; m_AssetIcons[i].enabled = false; m_BoundIcons[i] = -1;
                m_Labels[i] = UIFactory.Label(back.transform, "SkillName", source.SlotLabel(i), 20, TextAnchor.MiddleCenter, new Vector2(-.15f, -.20f), new Vector2(1.15f, .10f));
                m_Status[i] = BufferText.Create(back.transform, "SkillState", 20, TextAnchor.MiddleCenter, new Vector2(0, .04f), new Vector2(1, .35f));
            }
            m_Controls.gameObject.SetActive(false);
            Input = new HudInputSource(this);
            RefreshLayout(true);
            Refresh();
        }

        static CombatControlGraphic Graphic(Transform parent, string name, Vector2 position, Vector2 size, Vector2 anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size; rect.anchoredPosition = position;
            var graphic = go.AddComponent<CombatControlGraphic>(); graphic.color = color; graphic.raycastTarget = false;
            return graphic;
        }

        void Update() { if (m_Source != null) { RefreshLayout(false); Refresh(); } }

        public void Refresh()
        {
            if (m_Source == null) return;
            bool playing = m_Source.Playing && !m_Paused && !m_FocusLost;
            if (playing != m_Playing)
            {
                CancelInput(); m_Playing = playing; m_Controls.gameObject.SetActive(playing);
            }
            for (int i = 0; i < Buttons.Length; i++)
            {
                var snapshot = m_Source.ReadSlot(i);
                Buttons[i].SetSnapshot(snapshot);
                if (m_BoundSkills[i] != snapshot.Definition.Id)
                { m_BoundSkills[i] = snapshot.Definition.Id; m_Labels[i].text = m_Source.SlotLabel(i); }
                Color tint = !snapshot.Enabled ? new Color(.42f, .47f, .50f, .6f) : snapshot.Charges > 0 ? new Color(.45f, .89f, .96f, .95f) : new Color(.57f, .67f, .73f, .72f);
                if (Buttons[i].AimingCanceled) tint = new Color(1f, .3f, .25f, 1);
                if (m_Icons[i].Glyph != snapshot.Definition.IconId) { m_Icons[i].Glyph = snapshot.Definition.IconId; m_Icons[i].SetVerticesDirty(); }
                if (m_BoundIcons[i] != snapshot.Definition.IconId)
                {
                    m_BoundIcons[i] = snapshot.Definition.IconId;
                    m_AssetIcons[i].sprite = IconResolver?.Invoke(snapshot.Definition.IconId);
                    m_AssetIcons[i].enabled = m_AssetIcons[i].sprite != null;
                    m_Icons[i].enabled = m_AssetIcons[i].sprite == null;
                }
                m_Icons[i].color = tint; m_Rings[i].color = tint; m_AssetIcons[i].color = tint;
                var aim = Buttons[i].Aim;
                Quaternion aimRotation = Buttons[i].Pressed && snapshot.Definition.Activation == SkillActivation.AimRelease && math.lengthsq(aim) > 0f
                    ? Quaternion.Euler(0, 0, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg - 45) : Quaternion.identity;
                m_Icons[i].transform.localRotation = aimRotation; m_AssetIcons[i].transform.localRotation = aimRotation;
                float fraction = snapshot.RechargeTicks > 0 ? 1f - snapshot.RechargeTicks / (float)snapshot.Definition.CooldownTicks : 1f;
                if (m_Rings[i].Fraction != fraction) { m_Rings[i].Fraction = fraction; m_Rings[i].SetVerticesDirty(); }
                var text = m_Status[i].Begin();
                if (Buttons[i].AimingCanceled) text.Append("CANCEL");
                else if (Buttons[i].Pressed && snapshot.Definition.Activation == SkillActivation.AimRelease) text.Append("AIM");
                else if (snapshot.RechargeTicks > 0)
                {
                    long tenths = ((long)snapshot.RechargeTicks * 10 + m_Source.TickRate - 1) / m_Source.TickRate;
                    text.Append(tenths / 10).Append('.').Append(tenths % 10).Append('s');
                }
                else text.Append(snapshot.Definition.Activation == SkillActivation.Hold ? "HOLD" : snapshot.Definition.Activation == SkillActivation.AimRelease ? "DRAG" : "READY");
                if (snapshot.Definition.MaxCharges > 1) text.Append("  ").Append(snapshot.Charges).Append('/').Append(snapshot.Definition.MaxCharges);
                m_Status[i].Commit();
            }
            m_StickKnob.anchoredPosition = new Vector2(Joystick.Direction.x, Joystick.Direction.y) * (Joystick.Magnitude * 52);
        }

        void RefreshLayout(bool force)
        {
            var safe = Screen.safeArea;
            if (!force && m_Width == Screen.width && m_Height == Screen.height && m_LastSafe.x == safe.x && m_LastSafe.y == safe.y && m_LastSafe.width == safe.width && m_LastSafe.height == safe.height) return;
            CancelInput(); m_Width = Screen.width; m_Height = Screen.height; m_LastSafe = safe;
            MobileSafeArea.Anchors(safe, m_Width, m_Height, out var min, out var max);
            SafeRoot.anchorMin = min; SafeRoot.anchorMax = max;
            SafeRoot.offsetMin = SafeRoot.offsetMax = Vector2.zero;
            bool wide = m_Width >= m_Height;
            if (m_Scaler != null)
            {
                m_Scaler.referenceResolution = wide ? new Vector2(1280, 720) : new Vector2(720, 1280);
                m_Scaler.matchWidthOrHeight = wide ? 1f : 0f;
            }
            m_StickRing.anchoredPosition = new Vector2(wide ? 150 : 145, wide ? 145 : 175);
            for (int i = 0; i < Buttons.Length; i++)
            {
                // Four slots remain bounded in two columns; primary is nearest the right thumb.
                var rect = (RectTransform)Buttons[i].transform;
                rect.anchoredPosition = new Vector2(-100 - (i % 2) * 163, (wide ? 140 : 170) + (i / 2) * 168 + (i % 2) * 50);
            }
            float scale = wide ? m_Height / 720f : m_Width / 720f;
            Joystick.Radius = 100 * Mathf.Max(scale, .1f); Joystick.DeadZone = 10 * Mathf.Max(scale, .1f);
            foreach (var button in Buttons) { button.AimRadius = 80 * Mathf.Max(scale, .1f); button.CancelRadius = 240 * Mathf.Max(scale, .1f); }
        }

        public void CancelInput()
        {
            Joystick?.CancelInput();
            if (Buttons != null) foreach (var button in Buttons) button.CancelInput();
            Interrupted?.Invoke();
        }
        void OnDisable()
        {
            CancelInput(); m_Playing = false;
            if (m_Controls != null) m_Controls.gameObject.SetActive(false);
        }
        void OnApplicationPause(bool paused) { m_Paused = paused; if (paused) CancelInput(); }
        void OnApplicationFocus(bool focused) { m_FocusLost = !focused; if (!focused) CancelInput(); }

        sealed class HudInputSource : IInputSource
        {
            readonly MobileCombatHud m_Hud;
            public HudInputSource(MobileCombatHud hud) => m_Hud = hud;
            public bool TryRead(ref InputFrame frame)
            {
                if (!m_Hud.m_Playing || !m_Hud.isActiveAndEnabled || m_Hud.m_Paused || m_Hud.m_FocusLost) return false;
                var result = default(InputFrame);
                if (m_Hud.Joystick.Pressed) result.Move = m_Hud.Joystick.Direction * m_Hud.Joystick.Magnitude;
                bool owning = m_Hud.Joystick.Pressed;
                for (int i = 0; i < m_Hud.Buttons.Length; i++)
                { m_Hud.Buttons[i].Read(ref result, i); owning |= m_Hud.Buttons[i].Pressed; }
                if (!owning && result.Pressed == 0 && result.Held == 0) return false;
                frame = result; return true;
            }
        }
    }
}
