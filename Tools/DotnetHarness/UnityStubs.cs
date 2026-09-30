// Minimal Unity API surface for compile-checking outside Unity. Signatures mirror Unity 2022.3 / Collections 2.1.
using System;
using System.Collections;
using System.Collections.Generic;

namespace Unity.Collections
{
    public enum Allocator { Invalid, None, Temp, TempJob, Persistent }
    public enum NativeArrayOptions { UninitializedMemory, ClearMemory }
    [AttributeUsage(AttributeTargets.Field)] public sealed class ReadOnlyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class WriteOnlyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class NativeDisableParallelForRestrictionAttribute : Attribute { }
    public unsafe struct NativeArray<T> : IDisposable, IEnumerable<T> where T : struct
    {
        internal byte* m_Ptr; int len;
        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>() * Math.Max(length, 1);
            m_Ptr = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            new Span<byte>(m_Ptr, size).Clear();
            len = length;
        }
        public int Length => len;
        public bool IsCreated => m_Ptr != null;
        public T this[int i]
        {
            get { if ((uint)i >= (uint)len) throw new IndexOutOfRangeException(); return System.Runtime.CompilerServices.Unsafe.Read<T>(m_Ptr + i * System.Runtime.CompilerServices.Unsafe.SizeOf<T>()); }
            set { if ((uint)i >= (uint)len) throw new IndexOutOfRangeException(); System.Runtime.CompilerServices.Unsafe.Write(m_Ptr + i * System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), value); }
        }
        public void Dispose() { System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)m_Ptr); m_Ptr = null; }
        public NativeArray<T> GetSubArray(int start, int length)
        {
            if (start < 0 || length < 0 || start + length > len) throw new ArgumentOutOfRangeException();
            return new NativeArray<T> { m_Ptr = m_Ptr + start * System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), len = length };
        }
        public static void Copy(NativeArray<T> src, NativeArray<T> dst, int length)
        {
            if (length > src.len || length > dst.len) throw new ArgumentOutOfRangeException();
            Buffer.MemoryCopy(src.m_Ptr, dst.m_Ptr, (long)dst.len * System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), (long)length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>());
        }
        public IEnumerator<T> GetEnumerator() { for (int i = 0; i < len; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace Unity.Collections.LowLevel.Unsafe
{
    public static class NativeArrayUnsafeUtility
    {
        public static unsafe void* GetUnsafePtr<T>(this NativeArray<T> a) where T : struct => a.m_Ptr;
    }
}
namespace Unity.Jobs
{
    public struct JobHandle
    {
        public void Complete() { }
        public static JobHandle CombineDependencies(JobHandle a, JobHandle b) => default;
        public static JobHandle CombineDependencies(JobHandle a, JobHandle b, JobHandle c) => default;
        public static void ScheduleBatchedJobs() { }
    }
    public interface IJob { void Execute(); }
    public interface IJobParallelFor { void Execute(int index); }
    public static class IJobExtensions { public static JobHandle Schedule<T>(this T job, JobHandle dependsOn = default) where T : struct, IJob { job.Execute(); return default; } }
    public static class IJobParallelForExtensions { public static JobHandle Schedule<T>(this T job, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default) where T : struct, IJobParallelFor { for (int i = 0; i < arrayLength; i++) job.Execute(i); return default; } }
}
namespace Unity.Burst
{
    public enum FloatMode { Default, Strict, Deterministic, Fast }
    public enum FloatPrecision { Standard, High, Medium, Low }
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Assembly)]
    public sealed class BurstCompileAttribute : Attribute { public FloatMode FloatMode { get; set; } public FloatPrecision FloatPrecision { get; set; } public bool CompileSynchronously { get; set; } }
}
namespace Unity.Profiling
{
    public struct ProfilerMarker { public ProfilerMarker(string name) { } public void Begin() { } public void End() { } }
    public enum ProfilerCategoryEnum { }
    public struct ProfilerCategory { public static ProfilerCategory Memory => default; public static ProfilerCategory Render => default; }
    public struct ProfilerRecorder : IDisposable
    {
        public static ProfilerRecorder StartNew(ProfilerCategory c, string name, int capacity = 1) => default;
        public bool Valid => false; public long LastValue => 0; public void Dispose() { }
    }
}
namespace UnityEngine
{
    public abstract class PropertyAttribute : Attribute { }
    public class Object { public static void DestroyImmediate(Object o) { } public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b); public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b); public override bool Equals(object o) => base.Equals(o); public override int GetHashCode() => 0; }
    public class Component : Object { }
    public class Behaviour : Component { public bool enabled { get; set; } }
    public class MonoBehaviour : Behaviour { }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject, new() => new T(); }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float a) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class CreateAssetMenuAttribute : Attribute { public string menuName { get; set; } public string fileName { get; set; } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int o) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    public static class Debug { public static void LogError(object m, Object ctx = null) { } }
    public static class Time { public static float deltaTime => 0; public static float unscaledDeltaTime => 0; public static float unscaledTime => 0; }
    public enum KeyCode { F1 }
    public enum TouchPhase { Began }
    public struct Touch { public TouchPhase phase => default; }
    public static class Input { public static bool GetKeyDown(KeyCode k) => false; public static int touchCount => 0; public static Touch GetTouch(int i) => default; }
    public enum TextAnchor { UpperLeft }
    public struct Color { public static Color white => default; public static Color cyan => default; }
    public struct Vector2 { public float x, y; }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    public struct Rect { public Rect(float x, float y, float w, float h) { } }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle { public GUIStyle(GUIStyle o) { } public TextAnchor alignment; public int fontSize; public bool richText; public GUIStyleState normal = new GUIStyleState(); public Vector2 CalcSize(GUIContent c) => default; }
    public class GUISkin { public GUIStyle box => null; }
    public class GUIContent { public GUIContent(string t) { text = t; } public string text; }
    public static class GUI { public static GUISkin skin => null; public static void Box(Rect r, GUIContent c, GUIStyle s) { } }
    public static class Gizmos { public static Color color { get; set; } public static void DrawCube(Vector3 c, Vector3 s) { } }
    public static class Mathf { public static int Min(int a, int b) => Math.Min(a, b); public static float Abs(float a) => Math.Abs(a); }
}
namespace UnityEngine.TestTools.Constraints
{
    public class Is : NUnit.Framework.Is
    {
        public static new NotHelper Not => new NotHelper();
        public class NotHelper { public NUnit.Framework.Constraints.IResolveConstraint AllocatingGCMemory() => new NoAllocConstraint(); }
        class NoAllocConstraint : NUnit.Framework.Constraints.Constraint
        {
            public override NUnit.Framework.Constraints.ConstraintResult ApplyTo<TActual>(TActual actual)
            {
                var action = (NUnit.Framework.TestDelegate)(object)actual;
                long before = GC.GetAllocatedBytesForCurrentThread();
                action();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Description = "no GC allocation (allocated " + allocated + " bytes)";
                return new NUnit.Framework.Constraints.ConstraintResult(this, allocated, allocated == 0);
            }
        }
    }
}
