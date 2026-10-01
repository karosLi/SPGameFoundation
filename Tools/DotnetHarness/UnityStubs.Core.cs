// Minimal UnityEngine core surface for the harness. Behaviour is simplified (no engine behind it).
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public abstract class PropertyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public sealed class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : PropertyAttribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : PropertyAttribute { public MinAttribute(float a) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute : PropertyAttribute { public TextAreaAttribute() { } public TextAreaAttribute(int a, int b) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string menuName { get; set; } public string fileName { get; set; } public int order { get; set; } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int o) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public sealed class RequireComponent : Attribute { public RequireComponent(Type t) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string m) { } }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }
    [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute() { } public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }

    public enum HideFlags { None = 0, HideInInspector = 2, HideAndDontSave = 61, DontSave = 52 }

    public class Object
    {
        public string name { get; set; } = "";
        public HideFlags hideFlags { get; set; }
        internal bool m_Destroyed;
        public static void Destroy(Object o) { if (o != null) o.m_Destroyed = true; }
        public static void Destroy(Object o, float t) => Destroy(o);
        public static void DestroyImmediate(Object o) => Destroy(o);
        public static void DontDestroyOnLoad(Object o) { }
        public static T Instantiate<T>(T original) where T : Object => original;
        public static T FindObjectOfType<T>() where T : Object => null;
        public int GetInstanceID() => GetHashCode();
        public static implicit operator bool(Object o) => o is object && !o.m_Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = a is null || a.m_Destroyed, bn = b is null || b.m_Destroyed;
            if (an || bn) return an && bn;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object o) => o is Object other ? this == other : false;
        public override int GetHashCode() => base.GetHashCode();
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject?.transform;
        public T GetComponent<T>() where T : class => gameObject?.GetComponent<T>();
        public T GetComponentInChildren<T>() where T : class => GetComponent<T>();
        public T[] GetComponentsInChildren<T>() where T : class => Array.Empty<T>();
        public string tag { get; set; }
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; } = Vector3.one;
        public Quaternion rotation { get; set; } = Quaternion.identity;
        public Quaternion localRotation { get; set; } = Quaternion.identity;
        public Vector3 eulerAngles { get; set; }
        public Transform parent { get; private set; }
        readonly List<Transform> m_Children = new List<Transform>();
        public int childCount => m_Children.Count;
        public Transform GetChild(int i) => m_Children[i];
        public void SetParent(Transform p) => SetParent(p, true);
        public void SetParent(Transform p, bool worldPositionStays) { parent?.m_Children.Remove(this); parent = p; p?.m_Children.Add(this); }
        public void SetAsLastSibling() { }
        public void SetAsFirstSibling() { }
        public Transform Find(string n) { foreach (var c in m_Children) if (c.gameObject.name == n) return c; return null; }
        public IEnumerator GetEnumerator() => m_Children.GetEnumerator();
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Rect rect => new Rect(0, 0, sizeDelta.x, sizeDelta.y);
        public void GetWorldCorners(Vector3[] corners) { }
    }

    public class GameObject : Object
    {
        readonly List<Component> m_Components = new List<Component>();
        public GameObject() : this("GameObject") { }
        public GameObject(string name, params Type[] components)
        {
            this.name = name;
            bool rect = Array.IndexOf(components, typeof(RectTransform)) >= 0;
            var t = rect ? new RectTransform() : new Transform();
            t.gameObject = this;
            transform = t;
            m_Components.Add(t);
            foreach (var c in components) if (c != typeof(RectTransform) && c != typeof(Transform)) AddComponent(c);
        }
        public Transform transform { get; private set; }
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => activeSelf;
        public int layer { get; set; }
        public string tag { get; set; } = "Untagged";
        public void SetActive(bool v) => activeSelf = v;
        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));
        public Component AddComponent(Type t)
        {
            var c = (Component)Activator.CreateInstance(t, true);
            c.gameObject = this;
            m_Components.Add(c);
            return c;
        }
        public T GetComponent<T>() where T : class { foreach (var c in m_Components) if (c is T t) return t; return null; }
        public bool TryGetComponent<T>(out T component) where T : class { component = GetComponent<T>(); return component != null; }
    }

    public class Behaviour : Component { public bool enabled { get; set; } = true; public bool isActiveAndEnabled => enabled; }
    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => null;
        public void StopAllCoroutines() { }
    }
    public sealed class Coroutine { }
    public class YieldInstruction { }
    public sealed class WaitForSeconds : YieldInstruction { public WaitForSeconds(float s) { } }
    public sealed class WaitForEndOfFrame : YieldInstruction { }
    public class CustomYieldInstruction : IEnumerator { public virtual bool keepWaiting => false; public object Current => null; public bool MoveNext() => keepWaiting; public void Reset() { } }
    public sealed class WaitUntil : CustomYieldInstruction { public WaitUntil(Func<bool> f) { } }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => (T)Activator.CreateInstance(typeof(T), true);
    }

    public static class Debug
    {
        public static void Log(object m) { }
        public static void Log(object m, Object ctx) { }
        public static void LogWarning(object m) { }
        public static void LogWarning(object m, Object ctx) { }
        public static void LogError(object m) { }
        public static void LogError(object m, Object ctx) { }
        public static void LogException(Exception e) { }
        public static void Assert(bool c) { }
        public static void Assert(bool c, string m) { }
        public static void DrawLine(Vector3 a, Vector3 b, Color c) { }
    }

    public static class Time
    {
        public static float deltaTime { get; set; } = 1f / 60f;
        public static float unscaledDeltaTime { get; set; } = 1f / 60f;
        public static float time { get; set; }
        public static float unscaledTime { get; set; }
        public static float realtimeSinceStartup { get; set; }
        public static int frameCount { get; set; }
        public static float timeScale { get; set; } = 1f;
    }

    public enum RuntimePlatform { WindowsEditor, OSXEditor, LinuxEditor, Android, IPhonePlayer, WindowsPlayer, LinuxPlayer }
    public static class Application
    {
        public static int targetFrameRate { get; set; }
        public static bool isPlaying => true;
        public static bool isMobilePlatform => false;
        public static bool isBatchMode => true;
        public static RuntimePlatform platform => RuntimePlatform.LinuxEditor;
        public static string persistentDataPath => System.IO.Path.GetTempPath();
        public static string dataPath => System.IO.Path.GetTempPath();
        public static void Quit() { }
    }

    public static class Screen
    {
        public static int width { get; set; } = 1920;
        public static int height { get; set; } = 1080;
        public static float dpi => 160f;
        public static bool fullScreen { get; set; }
        public static int sleepTimeout { get; set; }
    }

    public static class SleepTimeout { public const int NeverSleep = -1; }

    public enum KeyCode { None, Space, Return, Escape, F1, F2, W, A, S, D, E, Q, R, LeftShift, RightShift, UpArrow, DownArrow, LeftArrow, RightArrow, Mouse0, Mouse1 }
    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
    public struct Touch { public int fingerId { get; set; } public Vector2 position { get; set; } public TouchPhase phase { get; set; } }
    public static class Input
    {
        public static bool GetKey(KeyCode k) => false;
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetKeyUp(KeyCode k) => false;
        public static bool GetMouseButton(int b) => false;
        public static bool GetMouseButtonDown(int b) => false;
        public static Vector3 mousePosition => default;
        public static bool mousePresent => false;
        public static int touchCount => 0;
        public static Touch GetTouch(int i) => default;
        public static float GetAxisRaw(string a) => 0f;
        public static bool multiTouchEnabled { get; set; }
    }

    public struct Vector2 : IEquatable<Vector2>
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 up => new Vector2(0, 1);
        public static Vector2 right => new Vector2(1, 0);
        public float magnitude => MathF.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 ClampMagnitude(Vector2 v, float m) => v.magnitude > m ? v.normalized * m : v;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
        public bool Equals(Vector2 o) => this == o;
        public override bool Equals(object o) => o is Vector2 v && this == v;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode();
    }

    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public static Vector3 zero => default;
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 back => new Vector3(0, 0, -1);
        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
        public bool Equals(Vector3 o) => this == o;
        public override bool Equals(object o) => o is Vector3 v && this == v;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => default;
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0);
    }

    public struct Vector2Int { public int x, y; public Vector2Int(int x, int y) { this.x = x; this.y = y; } }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion Euler(float x, float y, float z) => identity;
    }

    public struct Matrix4x4
    {
        Vector4 c0, c1, c2, c3;
        public Matrix4x4(Vector4 a, Vector4 b, Vector4 c, Vector4 d) { c0 = a; c1 = b; c2 = c; c3 = d; }
        public Vector4 GetColumn(int i) => i == 0 ? c0 : i == 1 ? c1 : i == 2 ? c2 : c3;
        public static Matrix4x4 identity => new Matrix4x4(new Vector4(1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 0, 0, 1));
        public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => identity;
        public static Matrix4x4 Ortho(float l, float r, float b, float t, float n, float f) => identity;
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => a;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color clear => new Color(0, 0, 0, 0);
        public static Color cyan => new Color(0, 1, 1);
        public static Color yellow => new Color(1, 0.92f, 0.016f);
        public static Color red => new Color(1, 0, 0);
        public static Color green => new Color(0, 1, 0);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f);
        public static Color Lerp(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        public static Color operator *(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a * f);
        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
        public Color linear => this;
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public Rect(Vector2 p, Vector2 s) { x = p.x; y = p.y; width = s.x; height = s.y; }
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
        public Vector2 size => new Vector2(width, height);
        public float xMin => x; public float yMin => y; public float xMax => x + width; public float yMax => y + height;
        public bool Contains(Vector2 p) => p.x >= x && p.y >= y && p.x < x + width && p.y < y + height;
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 c, Vector3 s) { center = c; size = s; }
    }

    public static class Mathf
    {
        public const float PI = MathF.PI;
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Abs(float a) => Math.Abs(a);
        public static float Clamp(float v, float a, float b) => Math.Clamp(v, a, b);
        public static int Clamp(int v, int a, int b) => Math.Clamp(v, a, b);
        public static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Sqrt(float v) => MathF.Sqrt(v);
        public static float Log(float v) => MathF.Log(v);
        public static float Pow(float a, float b) => MathF.Pow(a, b);
        public static int RoundToInt(float v) => (int)MathF.Round(v);
        public static int CeilToInt(float v) => (int)MathF.Ceiling(v);
        public static int FloorToInt(float v) => (int)MathF.Floor(v);
        public static float SmoothDamp(float c, float t, ref float v, float st) => t;
        public static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
        public static float Sin(float v) => MathF.Sin(v);
        public static float Cos(float v) => MathF.Cos(v);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public const float Rad2Deg = 57.29578f;
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
    }

    public static class Gizmos
    {
        public static Color color { get; set; }
        public static void DrawCube(Vector3 c, Vector3 s) { }
        public static void DrawWireCube(Vector3 c, Vector3 s) { }
        public static void DrawWireSphere(Vector3 c, float r) { }
        public static void DrawLine(Vector3 a, Vector3 b) { }
    }

    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public class Font : Object { public static Font CreateDynamicFontFromOSFont(string n, int s) => new Font(); }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle { public GUIStyle() { } public GUIStyle(GUIStyle o) { } public TextAnchor alignment; public int fontSize; public bool richText; public GUIStyleState normal = new GUIStyleState(); public Vector2 CalcSize(GUIContent c) => default; }
    public class GUISkin { public GUIStyle box => new GUIStyle(); public GUIStyle label => new GUIStyle(); }
    public class GUIContent { public GUIContent(string t) { text = t; } public string text; }
    public static class GUI { public static GUISkin skin => new GUISkin(); public static void Box(Rect r, GUIContent c, GUIStyle s) { } public static void Label(Rect r, string t) { } }

    public enum ScreenOrientation { Portrait, LandscapeLeft, LandscapeRight, AutoRotation }
    public static class SystemInfo
    {
        public static bool supportsComputeShaders => true;
        public static int maxComputeBufferInputsVertex => 8;
        public static bool supportsInstancing => true;
        public static int systemMemorySize => 4096;
        public static int processorCount => 4;
        public static string deviceModel => "Harness";
        public static Rendering.GraphicsDeviceType graphicsDeviceType => Rendering.GraphicsDeviceType.Null;
        public static bool SupportsTextureFormat(TextureFormat f) => true;
        public static int maxTextureSize => 4096;
    }

    public static class QualitySettings
    {
        public static int vSyncCount { get; set; }
        public static int antiAliasing { get; set; }
    }

    public static class ScreenCapture
    {
        public static Texture2D CaptureScreenshotAsTexture() => new Texture2D(1, 1);
        public static void CaptureScreenshot(string path) { }
    }
}
