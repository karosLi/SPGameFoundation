#if UNITY_EDITOR
using SurvivorFoundation.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SurvivorFoundation.Editor
{
    public static class SvFlyingSwordSceneTools
    {
        [MenuItem("SPF/Survivor/Create Flying Sword Horde Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Flying Sword Horde - Survivor Variant");
            var component = go.AddComponent<SvGameBootstrap>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("m_FlyingSwordExample").boolValue = true;
            serialized.FindProperty("m_NaturalCharacters").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!AssetDatabase.IsValidFolder("Assets/SurvivorFoundation/Scenes")) AssetDatabase.CreateFolder("Assets/SurvivorFoundation", "Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/SurvivorFoundation/Scenes/FlyingSwordHorde.unity");
            Selection.activeGameObject = go;
        }
    }
}
#endif
