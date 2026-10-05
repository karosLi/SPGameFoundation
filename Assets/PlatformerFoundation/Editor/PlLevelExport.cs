using SPF.L1.Spatial;
using UnityEditor;
using UnityEngine;

namespace PlatformerFoundation.Editor
{
    /// <summary>Turns the built-in text levels into editable assets (Assets/PlatformerFoundation/Levels).</summary>
    public static class PlLevelExport
    {
        [MenuItem("SPF/Platformer/Export Levels To Assets")]
        public static void Export()
        {
            const string folder = "Assets/PlatformerFoundation/Levels";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/PlatformerFoundation", "Levels");
            TileLevelAsset first = null;
            for (int i = 0; i < PlLevels.All.Length; i++)
            {
                var level = ScriptableObject.CreateInstance<TileLevelAsset>();
                level.Legend = PlLevels.Legend;
                level.LayerCount = 2;
                level.FromRows(PlLevels.All[i]);
                AssetDatabase.CreateAsset(level, $"{folder}/Level{i + 1}.asset");
                if (first == null) first = level;
            }
            AssetDatabase.SaveAssets();
            if (first != null) SPF.Editor.TileLevelEditorWindow.Open(first);
        }
    }
}
