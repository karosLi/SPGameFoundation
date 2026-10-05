using System.Collections.Generic;
using System.IO;
using RpgFoundation.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RpgFoundation.Editor
{
    /// <summary>Menu SPF/RPG/…: creates the config asset and the playable dungeon scene (added to the build list).</summary>
    public static class RpgProjectSetup
    {
        public const string ScenePath = "Assets/RpgFoundation/Scenes/Rpg.unity";
        public const string ConfigPath = "Assets/RpgFoundation/Config/RpgConfig.asset";

        [MenuItem("SPF/RPG/Create Or Update Scene")]
        public static void CreateScene()
        {
            var config = AssetDatabase.LoadAssetAtPath<RpgConfig>(ConfigPath);
            if (config == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                config = ScriptableObject.CreateInstance<RpgConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.GetComponent<Camera>().orthographic = true;
            var game = new GameObject("RpgGame");
            var bootstrap = game.AddComponent<RpgGameBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("m_Config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0]);
            if (!scenes.Exists(s => s.path == ScenePath))
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[SPF] RPG scene written to {ScenePath}");
        }
    }
}
