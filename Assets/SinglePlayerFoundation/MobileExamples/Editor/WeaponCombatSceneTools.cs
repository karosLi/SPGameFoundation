#if UNITY_EDITOR
using BrawlerFoundation.Game;
using SurvivorFoundation.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SPF.MobileExamples.Editor
{
    public static class WeaponCombatSceneTools
    {
        [MenuItem("SPF/Weapons/Create Landscape Weapon Brawler Scene")]
        public static void CreateBrawler()=>Create(true);
        [MenuItem("SPF/Weapons/Create Portrait Weapon Horde Scene")]
        public static void CreateSurvivor()=>Create(false);
        static void Create(bool belt)
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var go=new GameObject(belt?"Landscape Weapon Brawler":"Portrait Weapon Horde");
            var serialized=new SerializedObject(belt?(Object)go.AddComponent<BwGameBootstrap>():go.AddComponent<SvGameBootstrap>());
            serialized.FindProperty(belt?"m_WeaponCombat":"m_WeaponCombatExample").boolValue=true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            const string folder="Assets/SinglePlayerFoundation/MobileExamples/Scenes";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/SinglePlayerFoundation/MobileExamples","Scenes");
            EditorSceneManager.SaveScene(scene,folder+(belt?"/WeaponBrawler.unity":"/WeaponHorde.unity"));Selection.activeGameObject=go;
        }
    }
}
#endif
