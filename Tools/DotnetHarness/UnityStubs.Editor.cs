// UnityEditor surface for compiling editor assemblies in the harness.
using System;
using UnityEngine;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class MenuItem : Attribute { public MenuItem(string path) { } public MenuItem(string path, bool validate, int priority) { } }
    public enum BuildTargetGroup { Standalone, Android, iOS }
    public enum BuildTarget { StandaloneLinux64, Android, iOS, StandaloneWindows64 }
    public enum ScriptingImplementation { Mono2x, IL2CPP }
    [Flags] public enum AndroidArchitecture { None = 0, ARMv7 = 1, ARM64 = 2 }
    public enum UIOrientation { Portrait, PortraitUpsideDown, LandscapeRight, LandscapeLeft, AutoRotation }
    public static class PlayerSettings
    {
        public static ColorSpace colorSpace { get; set; }
        public static bool gcIncremental { get; set; }
        public static UIOrientation defaultInterfaceOrientation { get; set; }
        public static void SetScriptingBackend(BuildTargetGroup g, ScriptingImplementation s) { }
        public static void SetUseDefaultGraphicsAPIs(BuildTarget t, bool v) { }
        public static void SetGraphicsAPIs(BuildTarget t, UnityEngine.Rendering.GraphicsDeviceType[] apis) { }
        public static class Android { public static AndroidArchitecture targetArchitectures { get; set; } }
    }
    public sealed class EditorBuildSettingsScene { public EditorBuildSettingsScene(string path, bool enabled) { } }
    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes { get; set; } }
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string p) where T : UnityEngine.Object => null;
        public static void CreateAsset(UnityEngine.Object o, string p) { }
        public static void SaveAssets() { }
        public static void Refresh() { }
    }
    public class SerializedProperty { public UnityEngine.Object objectReferenceValue { get; set; } public int intValue { get; set; } public bool boolValue { get; set; } }
    public class SerializedObject { public SerializedObject(UnityEngine.Object o) { } public SerializedProperty FindProperty(string n) => new SerializedProperty(); public bool ApplyModifiedPropertiesWithoutUndo() => true; }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }
    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup s, NewSceneMode m) => default;
        public static bool SaveScene(UnityEngine.SceneManagement.Scene s, string path) => true;
    }
}

namespace UnityEngine
{
    public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name => ""; }
}
