using System.IO;
using SnakeFoundation.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnakeFoundation.Editor
{
    /// <summary>
    /// One-click project setup (menu SPF/…) and the entry points CI calls with -executeMethod.
    /// Creates the config asset and the playable scene, and applies mobile player settings.
    /// </summary>
    public static class SnakeProjectSetup
    {
        public const string ScenePath = "Assets/SnakeFoundation/Scenes/Snake.unity";
        public const string ConfigPath = "Assets/SnakeFoundation/Config/SnakeConfig.asset";

        [MenuItem("SPF/Snake/Create Or Update Scene")]
        public static void CreateScene()
        {
            var config = LoadOrCreateConfig();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);

            var game = new GameObject("SnakeGame");
            var bootstrap = game.AddComponent<SnakeGameBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("m_Config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[SPF] Scene written to {ScenePath}");
        }

        [MenuItem("SPF/Snake/Apply Mobile Player Settings")]
        public static void ApplyMobileSettings()
        {
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.gcIncremental = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            // Vulkan first (GPU-driven tier), GLES 3 as the fallback (data-texture tier).
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            QualitySettings.vSyncCount = 0;
            Debug.Log("[SPF] Mobile player settings applied");
        }

        /// <summary>CI entry point: -executeMethod SnakeFoundation.Editor.SnakeProjectSetup.SetupForCI</summary>
        public static void SetupForCI()
        {
            ApplyMobileSettings();
            CreateScene();
        }

        static SnakeConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<SnakeConfig>(ConfigPath);
            if (config != null) return config;
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            config = ScriptableObject.CreateInstance<SnakeConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }
    }
}
