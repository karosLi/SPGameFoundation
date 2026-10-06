#if UNITY_EDITOR
using BrawlerFoundation.Game;
using SurvivorFoundation.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SPF.MobileExamples.Editor
{
    /// <summary>Orientation preference belongs to the example layout. These menus never change global PlayerSettings.</summary>
    public static class MobileSkillSceneTools
    {
        [MenuItem("SPF/Mobile Skill HUD/Create Landscape Brawler Scene")]
        public static void Brawler() => Create(true);
        [MenuItem("SPF/Mobile Skill HUD/Create Portrait Horde Scene")]
        public static void Horde() => Create(false);
        static void Create(bool brawler)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject(brawler ? "Mobile Skill Brawler" : "Mobile Skill Horde");
            Component component = brawler ? (Component)go.AddComponent<BwGameBootstrap>() : go.AddComponent<SvGameBootstrap>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty(brawler ? "m_MobileCombat" : "m_MobileCombatExample").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            const string folder = "Assets/SinglePlayerFoundation/MobileExamples/Scenes";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/SinglePlayerFoundation/MobileExamples", "Scenes");
            EditorSceneManager.SaveScene(scene, folder + (brawler ? "/MobileBrawler.unity" : "/MobileHorde.unity"));
            Selection.activeGameObject = go;
        }
    }
}
#endif
