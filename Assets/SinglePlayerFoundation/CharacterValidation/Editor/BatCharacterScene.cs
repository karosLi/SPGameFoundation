#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using SPF.Presentation.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SPF.CharacterValidation.Editor
{
    public static class BatCharacterScene
    {
        [MenuItem("SPF/Characters/Create Or Update Weighted BAT Scene")]
        public static void Create()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Original Weighted BAT Character Validation").AddComponent<BatCharacterDemo>();
            if(!AssetDatabase.IsValidFolder("Assets/SinglePlayerFoundation/CharacterValidation/Scenes"))AssetDatabase.CreateFolder("Assets/SinglePlayerFoundation/CharacterValidation","Scenes");
            EditorSceneManager.SaveScene(scene,"Assets/SinglePlayerFoundation/CharacterValidation/Scenes/WeightedBat.unity");
            Selection.activeGameObject=SceneManager.GetActiveScene().GetRootGameObjects()[0];
        }
    }
}
#endif
