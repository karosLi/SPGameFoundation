#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ShooterFoundation.Tests
{
    public class ShooterConfigAssetTests
    {
        [Test]
        public void AuthoredConfigHasMonoScriptAndReloadsFromAssetDatabase()
        {
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/ShooterFoundation/Tests/ShooterConfigRoundTrip.asset");
            var source=ScriptableObject.CreateInstance<ShooterConfig>();source.Settings.Waves=7;source.Settings.HeroHp=123;
            try
            {
                Assert.IsNotNull(MonoScript.FromScriptableObject(source),"ScriptableObject must have a matching source filename");
                AssetDatabase.CreateAsset(source,path);AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
                var loaded=AssetDatabase.LoadAssetAtPath<ShooterConfig>(path);
                Assert.IsNotNull(loaded);Assert.AreEqual(7,loaded.Settings.Waves);Assert.AreEqual(123,loaded.Settings.HeroHp);
                Assert.AreEqual(typeof(ShooterConfig),MonoScript.FromScriptableObject(loaded).GetClass());
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
    }
}
#endif
