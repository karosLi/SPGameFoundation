using System.IO;
using ShooterFoundation.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShooterFoundation.Editor
{
    public static class ShooterProjectSetup
    {
        public const string ScenePath="Assets/ShooterFoundation/Scenes/Shooter.unity";
        [MenuItem("SPF/Shooter/Apply Portrait Mobile Settings")]
        public static void ApplyPortraitMobileSettings()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            Debug.Log("[SPF] Portrait mobile orientation selected for this shared project. Other game modes may require their own orientation setting.");
        }
        [MenuItem("SPF/Shooter/Create Or Update Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("ShooterGame").AddComponent<ShooterGameBootstrap>();
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            Debug.Log("[SPF] Portrait shooter scene written to "+ScenePath);
        }
    }
}
