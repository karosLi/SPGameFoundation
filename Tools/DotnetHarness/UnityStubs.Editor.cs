// UnityEditor surface for compiling editor assemblies in the harness.
using System;
using UnityEngine;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class InitializeOnLoadAttribute : Attribute { }
    [Flags] public enum BuildOptions { None = 0, Development = 1 }
    public struct BuildPlayerOptions { public string[] scenes; public string locationPathName; public BuildTarget target; public BuildOptions options; public string[] extraScriptingDefines; }
    namespace Build.Reporting { public enum BuildResult { Unknown, Succeeded, Failed, Cancelled } public struct BuildSummary { public BuildResult result; } public sealed class BuildReport { public BuildSummary summary; } }
    public static class BuildPipeline { public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions o) => new Build.Reporting.BuildReport(); }
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
        public static bool enableFrameTimingStats { get; set; }
        public static UIOrientation defaultInterfaceOrientation { get; set; }
        public static void SetScriptingBackend(BuildTargetGroup g, ScriptingImplementation s) { }
        public static void SetUseDefaultGraphicsAPIs(BuildTarget t, bool v) { }
        public static void SetGraphicsAPIs(BuildTarget t, UnityEngine.Rendering.GraphicsDeviceType[] apis) { }
        public static class Android { public static AndroidArchitecture targetArchitectures { get; set; } }
    }
    public sealed class EditorBuildSettingsScene { public EditorBuildSettingsScene(string path, bool enabled) { this.path = path; } public string path; public bool enabled = true; }
    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes { get; set; } }
    public static class Selection { public static UnityEngine.GameObject activeGameObject { get; set; } }
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string p) where T : UnityEngine.Object => null;
        public static void CreateAsset(UnityEngine.Object o, string p) { }
        public static void SaveAssets() { }
        public static bool IsValidFolder(string p) => true;
        public static string CreateFolder(string parent, string name) => parent + "/" + name;
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
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() => true;
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

namespace UnityEditor
{
    public class EditorWindow : UnityEngine.ScriptableObject
    {
        public static T GetWindow<T>(string title) where T : EditorWindow => UnityEngine.ScriptableObject.CreateInstance<T>();
        public void Repaint() { }
    }
    public class Editor : UnityEngine.ScriptableObject
    {
        public UnityEngine.Object target { get; set; }
        public virtual void OnInspectorGUI() { }
        public bool DrawDefaultInspector() => true;
    }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CustomEditor : Attribute { public CustomEditor(Type t) { } }
    public enum MessageType { None, Info, Warning, Error }
    public static class EditorGUILayout
    {
        public static UnityEngine.Object ObjectField(string label, UnityEngine.Object o, Type t, bool allowScene) => o;
        public static int IntField(string label, int v) => v;
        public static float Slider(string label, float v, float min, float max) => v;
        public static void HelpBox(string message, MessageType type) { }
        public static UnityEngine.Vector2 BeginScrollView(UnityEngine.Vector2 p) => p;
        public static void EndScrollView() { }
    }
    public static class EditorGUI { public static void DrawRect(UnityEngine.Rect r, UnityEngine.Color c) { } }
    public static class EditorGUIUtility { public static string systemCopyBuffer { get; set; } = ""; }
    public static class Undo { public static void RecordObject(UnityEngine.Object o, string name) { } }
    public static class EditorUtility { public static void SetDirty(UnityEngine.Object o) { } }
}
namespace UnityEngine
{
    public sealed class GUILayoutOption { }
    public static class GUILayout
    {
        public static GUILayoutOption Width(float w) => new GUILayoutOption();
        public static GUILayoutOption Height(float h) => new GUILayoutOption();
        public static void BeginHorizontal(params GUILayoutOption[] o) { }
        public static void EndHorizontal() { }
        public static bool Button(string text, params GUILayoutOption[] o) => false;
        public static bool Toggle(bool value, string text, string style, params GUILayoutOption[] o) => value;
        public static void Label(string text, params GUILayoutOption[] o) { }
    }
    public static class GUILayoutUtility { public static Rect GetRect(float w, float h) => new Rect(0, 0, w, h); }
    public enum EventType { MouseDown, MouseUp, MouseMove, MouseDrag, KeyDown, KeyUp, Repaint, Layout }
    public sealed class Event
    {
        public static Event current { get; set; } = new Event();
        public EventType type { get; set; } = EventType.Layout;
        public Vector2 mousePosition { get; set; }
        public int button { get; set; }
        public void Use() { }
    }
}
