#if UNITY_EDITOR
using SurvivorFoundation.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SurvivorFoundation.Editor
{
    public static class SvGuardSceneTools
    {
        [MenuItem("SPF/Survivor/Create Guard Example Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Beacon Guard - Original Example");
            var component = go.AddComponent<SvGameBootstrap>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("m_GuardExample").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!AssetDatabase.IsValidFolder("Assets/SurvivorFoundation/Scenes")) AssetDatabase.CreateFolder("Assets/SurvivorFoundation", "Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/SurvivorFoundation/Scenes/BeaconGuard.unity");
            Selection.activeGameObject = go;
        }
    }
}
#endif
