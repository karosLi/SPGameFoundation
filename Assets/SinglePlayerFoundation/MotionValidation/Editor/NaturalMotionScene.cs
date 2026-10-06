#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace SPF.MotionValidation.Editor
{
    public static class NaturalMotionScene
    {
        [MenuItem("SPF/Characters/Create Natural Motion Showcase")]
        public static void Create()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Selection.activeGameObject=new GameObject("Natural Motion - CPU Burst Cutout");
            Selection.activeGameObject.AddComponent<NaturalMotionDemo>();
            const string parent="Assets/SinglePlayerFoundation/MotionValidation";
            if(!AssetDatabase.IsValidFolder(parent+"/Scenes"))AssetDatabase.CreateFolder(parent,"Scenes");
            EditorSceneManager.SaveScene(scene,parent+"/Scenes/NaturalMotion.unity");
            // Deliberately no global orientation or PlayerSettings mutation.
        }
    }
}
#endif
