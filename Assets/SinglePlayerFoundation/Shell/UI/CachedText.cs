using SPF.Contracts.Pooling;
using UnityEngine.UI;

namespace SPF.Shell.UI
{
    /// <summary>
    /// A UGUI label fed through a <see cref="TextBuilder"/>: build the text every frame if convenient, the label
    /// only gets a new string (and a mesh rebuild) when the characters actually change.
    /// <code>hud.Begin().Append("Kills ").Append(kills); hud.Commit();</code>
    /// </summary>
    public sealed class CachedText
    {
        readonly TextBuilder m_Builder;
        public readonly Text Label;
        string m_Current;

        public CachedText(Text label, int capacity = 64)
        {
            Label = label;
            m_Builder = new TextBuilder(capacity);
            m_Current = label != null ? label.text : null;
        }

        /// <summary>Strings allocated so far (one per visible change).</summary>
        public int Commits { get; private set; }

        public TextBuilder Begin() => m_Builder.Clear();

        /// <summary>Applies the built text; returns true if the label changed.</summary>
        public bool Commit()
        {
            if (m_Builder.ContentEquals(m_Current)) return false;
            m_Current = m_Builder.ToString();
            Commits++;
            if (Label != null) Label.text = m_Current;
            return true;
        }
    }
}
