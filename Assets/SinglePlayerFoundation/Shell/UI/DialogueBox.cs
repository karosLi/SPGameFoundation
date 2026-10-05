using System;
using SPF.L2.Narrative;
using SPF.Shell.Pooling;
using UnityEngine;
using UnityEngine.UI;

namespace SPF.Shell.UI
{
    /// <summary>
    /// Dialogue UI for a <see cref="DialogueRunner"/>: speaker name, a word-wrapped body revealed by a typewriter
    /// (<see cref="BufferText.MaxVisible"/>, so revealing allocates nothing), tap to finish the line or continue,
    /// and choice buttons from a <see cref="ComponentPool{T}"/> (built once, reused for every question).
    /// The box only reports intent (<see cref="Advanced"/>, <see cref="Chosen"/>); the game applies it to the
    /// runner (directly, or as a simulation command when the story lives in a session).
    /// </summary>
    public sealed class DialogueBox : MonoBehaviour
    {
        sealed class ChoiceSlot : MonoBehaviour { public int Index; public Text Label; }

        public float CharactersPerSecond = 45f;
        public DialogueRunner Runner;
        public LocalizationTable Strings;
        public event Action Advanced;
        public event Action<int> Chosen;

        ComponentPool<Button> m_Pool;
        readonly Button[] m_Shown = new Button[8];
        int m_ShownCount;
        int m_Serial = -1, m_StringsVersion = -1;
        float m_Visible;

        public RectTransform Panel { get; private set; }
        public BufferText SpeakerText { get; private set; }
        public BufferText BodyText { get; private set; }
        public Button TapCatcher { get; private set; }
        public RectTransform ChoiceRoot { get; private set; }
        public int ChoicesShown => m_ShownCount;
        public Button Choice(int i) => m_Shown[i];
        public bool Typing => BodyText != null && BodyText.MaxVisible < BodyText.Length;
        /// <summary>Choice buttons ever created (stays at the prewarmed count: they are pooled).</summary>
        public int ChoiceButtonsCreated => m_Pool?.Created ?? 0;

        public static DialogueBox Create(Transform canvas, int fontSize = 40)
        {
            var root = UIFactory.Panel(canvas, "Dialogue", Color.clear, Vector2.zero, Vector2.one, raycast: false);
            var box = root.gameObject.AddComponent<DialogueBox>();
            box.Build(fontSize);
            return box;
        }

        void Build(int fontSize)
        {
            var tap = UIFactory.Panel(transform, "TapCatcher", new Color(0f, 0f, 0f, 0.001f), Vector2.zero, Vector2.one);
            TapCatcher = tap.gameObject.AddComponent<Button>();
            TapCatcher.onClick.AddListener(OnTap);
            Panel = UIFactory.Panel(transform, "Panel", new Color(0.05f, 0.05f, 0.1f, 0.85f), new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.33f), raycast: false);
            SpeakerText = BufferText.Create(Panel, "Speaker", fontSize, TextAnchor.UpperLeft, new Vector2(0.02f, 0.75f), new Vector2(0.98f, 0.98f));
            SpeakerText.color = new Color(1f, 0.85f, 0.45f);
            BodyText = BufferText.Create(Panel, "Body", fontSize, TextAnchor.UpperLeft, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.76f));
            BodyText.Wrap = true;
            ChoiceRoot = UIFactory.Panel(transform, "Choices", Color.clear, new Vector2(0.15f, 0.36f), new Vector2(0.85f, 0.9f), raycast: false);
            m_Pool = new ComponentPool<Button>(parent =>
            {
                var b = UIFactory.Button(parent, "Choice", "", Vector2.zero, new Vector2(900, 110), new Color(0.2f, 0.3f, 0.5f, 0.95f), new Vector2(0.5f, 1f), fontSize);
                var slot = b.gameObject.AddComponent<ChoiceSlot>();
                slot.Label = b.GetComponentInChildren<Text>();
                b.onClick.AddListener(() => OnChoice(slot));
                return b;
            }, ChoiceRoot, prewarm: 4, name: "DialogueChoice");
        }

        void OnDestroy() => m_Pool?.Dispose();

        void OnTap()
        {
            if (Runner == null) return;
            if (Typing) { m_Visible = BodyText.Length; BodyText.MaxVisible = BodyText.Length; return; }
            if (Runner.State == DialogueRunner.Mode.Line) Advanced?.Invoke();
        }

        void OnChoice(ChoiceSlot slot)
        {
            if (Runner != null && Runner.State == DialogueRunner.Mode.Choice && !Typing) Chosen?.Invoke(slot.Index);
        }

        /// <summary>Shows the whole current line at once (tests, skip button).</summary>
        public void Finish()
        {
            if (BodyText == null) return;
            m_Visible = BodyText.Length;
            BodyText.MaxVisible = BodyText.Length;
        }

        void ClearChoices()
        {
            for (int i = 0; i < m_ShownCount; i++) m_Pool.Release(m_Shown[i]);
            m_ShownCount = 0;
        }

        void Update()
        {
            if (Runner == null || Strings == null) return;
            bool active = Runner.State == DialogueRunner.Mode.Line || Runner.State == DialogueRunner.Mode.Choice;
            if (Panel.gameObject.activeSelf != active) Panel.gameObject.SetActive(active);
            if (TapCatcher.gameObject.activeSelf != active) TapCatcher.gameObject.SetActive(active);
            if (!active) { ClearChoices(); return; }

            bool newLine = Runner.Serial != m_Serial;
            if (newLine || Strings.Version != m_StringsVersion)
            {
                SpeakerText.SetText(Strings.Get(Runner.Speaker));
                BodyText.SetText(Strings.Get(Runner.Text));
                if (newLine) { m_Visible = 0f; BodyText.MaxVisible = 0; }
                m_Serial = Runner.Serial;
                m_StringsVersion = Strings.Version;
                ClearChoices();
            }
            if (Typing)
            {
                m_Visible += Time.unscaledDeltaTime * CharactersPerSecond;
                BodyText.MaxVisible = (int)m_Visible;
            }
            if (!Typing && Runner.State == DialogueRunner.Mode.Choice && m_ShownCount == 0)
            {
                for (int i = 0; i < Runner.ChoiceCount && i < m_Shown.Length; i++)
                {
                    var b = m_Pool.Get();
                    var slot = b.GetComponent<ChoiceSlot>();
                    slot.Index = i;
                    slot.Label.text = Strings.Get(Runner.ChoiceText(i));
                    ((RectTransform)b.transform).anchoredPosition = new Vector2(0f, -70f - i * 130f);
                    m_Shown[m_ShownCount++] = b;
                }
            }
        }
    }
}
