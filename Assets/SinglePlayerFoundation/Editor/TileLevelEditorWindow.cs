using SPF.L1.Spatial;
using UnityEditor;
using UnityEngine;

namespace SPF.Editor
{
    /// <summary>
    /// Paints <see cref="TileLevelAsset"/>s: pick a symbol from the level's legend (tiles or markers), click or drag
    /// on the grid (right button erases), resize, and copy the level to / from text rows (the clipboard) so levels
    /// stay diffable and code-defined levels can be pasted in. Every stroke is undoable.
    /// </summary>
    public sealed class TileLevelEditorWindow : EditorWindow
    {
        TileLevelAsset m_Level;
        char m_Brush = ' ';
        float m_Cell = 14f;
        Vector2 m_Scroll;
        int m_Width, m_Height, m_Layers;

        [MenuItem("SPF/Tile Level Editor")]
        public static void Open() => GetWindow<TileLevelEditorWindow>("Tile Level");

        /// <summary>Opens the editor on <paramref name="level"/>.</summary>
        public static TileLevelEditorWindow Open(TileLevelAsset level)
        {
            var window = GetWindow<TileLevelEditorWindow>("Tile Level");
            window.m_Level = level;
            window.SyncSize();
            return window;
        }

        void SyncSize()
        {
            if (m_Level == null) return;
            m_Width = m_Level.Width;
            m_Height = m_Level.Height;
            m_Layers = m_Level.LayerCount;
        }

        void OnGUI()
        {
            var level = (TileLevelAsset)EditorGUILayout.ObjectField("Level", m_Level, typeof(TileLevelAsset), false);
            if (level != m_Level) { m_Level = level; SyncSize(); }
            if (m_Level == null)
            {
                EditorGUILayout.HelpBox("Pick a Tile Level asset (Create → SPF → Tile Level). Its legend lists the symbols you can paint.", MessageType.Info);
                return;
            }

            // Palette.
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(m_Brush == ' ', "Erase", "Button", GUILayout.Width(70))) m_Brush = ' ';
            foreach (var entry in m_Level.Legend)
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = entry.Color;
                string label = entry.Symbol + " " + (string.IsNullOrEmpty(entry.Name) ? "" : entry.Name);
                if (GUILayout.Toggle(m_Brush == entry.Symbol, label, "Button")) m_Brush = entry.Symbol;
                GUI.backgroundColor = previous;
            }
            GUILayout.EndHorizontal();

            // Size, zoom, text.
            GUILayout.BeginHorizontal();
            m_Width = EditorGUILayout.IntField("Width", m_Width);
            m_Height = EditorGUILayout.IntField("Height", m_Height);
            m_Layers = EditorGUILayout.IntField("Layers", m_Layers);
            if (GUILayout.Button("Resize", GUILayout.Width(70)))
            {
                Undo.RecordObject(m_Level, "Resize level");
                m_Level.Resize(m_Width, m_Height, m_Layers);
                EditorUtility.SetDirty(m_Level);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            m_Cell = EditorGUILayout.Slider("Zoom", m_Cell, 6f, 32f);
            if (GUILayout.Button("Copy as text", GUILayout.Width(110)))
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", m_Level.ToRows());
            if (GUILayout.Button("Paste text", GUILayout.Width(110)))
            {
                Undo.RecordObject(m_Level, "Paste level");
                m_Level.FromRows(EditorGUIUtility.systemCopyBuffer.Replace("\r", "").Split('\n'));
                EditorUtility.SetDirty(m_Level);
                SyncSize();
            }
            GUILayout.EndHorizontal();

            // Grid.
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            var area = GUILayoutUtility.GetRect(m_Level.Width * m_Cell, m_Level.Height * m_Cell);
            DrawGrid(area);
            HandlePaint(area);
            EditorGUILayout.EndScrollView();
        }

        void DrawGrid(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.14f));
            for (int y = 0; y < m_Level.Height; y++)
                for (int x = 0; x < m_Level.Width; x++)
                {
                    var cell = CellRect(area, x, y);
                    for (int l = 0; l < m_Level.LayerCount; l++)
                    {
                        byte v = m_Level.Get(l, x, y);
                        if (v == 0) continue;
                        foreach (var e in m_Level.Legend)
                            if (!e.Marker && e.Layer == l && e.Value == v) { EditorGUI.DrawRect(cell, e.Color); break; }
                    }
                }
            foreach (var m in m_Level.Markers)
            {
                var cell = CellRect(area, m.Cell.x, m.Cell.y);
                int s = m_Level.FindSymbol(m.Symbol);
                var inner = new Rect(cell.x + cell.width * 0.2f, cell.y + cell.height * 0.2f, cell.width * 0.6f, cell.height * 0.6f);
                EditorGUI.DrawRect(inner, s >= 0 ? m_Level.Legend[s].Color : Color.magenta);
                if (m_Cell >= 12f) GUI.Label(cell, m.Symbol.ToString());
            }
            var line = new Color(1f, 1f, 1f, 0.06f);
            for (int x = 0; x <= m_Level.Width; x++) EditorGUI.DrawRect(new Rect(area.x + x * m_Cell, area.y, 1f, area.height), line);
            for (int y = 0; y <= m_Level.Height; y++) EditorGUI.DrawRect(new Rect(area.x, area.y + y * m_Cell, area.width, 1f), line);
        }

        Rect CellRect(Rect area, int x, int y) => new Rect(area.x + x * m_Cell, area.y + (m_Level.Height - 1 - y) * m_Cell, m_Cell, m_Cell);

        void HandlePaint(Rect area)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;
            if (!area.Contains(e.mousePosition)) return;
            int x = (int)((e.mousePosition.x - area.x) / m_Cell);
            int y = m_Level.Height - 1 - (int)((e.mousePosition.y - area.y) / m_Cell);
            char brush = e.button == 1 ? ' ' : m_Brush;
            Undo.RecordObject(m_Level, "Paint level");
            m_Level.Paint(brush, x, y);
            EditorUtility.SetDirty(m_Level);
            e.Use();
            Repaint();
        }
    }

    /// <summary>Inspector shortcut: open the painter from the asset.</summary>
    [CustomEditor(typeof(TileLevelAsset))]
    public sealed class TileLevelAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open in Tile Level Editor")) TileLevelEditorWindow.Open((TileLevelAsset)target);
            DrawDefaultInspector();
        }
    }
}
