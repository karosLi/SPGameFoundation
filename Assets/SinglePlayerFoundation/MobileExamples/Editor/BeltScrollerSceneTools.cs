#if UNITY_EDITOR
using BrawlerFoundation.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SPF.MobileExamples.Editor
{
    public static class BeltScrollerSceneTools
    {
        [MenuItem("SPF/Mobile Gameplay/Create Landscape Belt Scroller Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Landscape Belt Scroller");
            var serialized = new SerializedObject(go.AddComponent<BwGameBootstrap>());
            serialized.FindProperty("m_BeltScroller").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            const string folder = "Assets/SinglePlayerFoundation/MobileExamples/Scenes";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/SinglePlayerFoundation/MobileExamples", "Scenes");
            EditorSceneManager.SaveScene(scene, folder + "/LandscapeBeltScroller.unity");
            Selection.activeGameObject = go;
        }
    }
}
#endif
