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
        public static void Copy(NativeArray<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
        {
            if (srcIndex < 0 || dstIndex < 0 || length < 0 || srcIndex + length > src.len || dstIndex + length > dst.len) throw new ArgumentOutOfRangeException();
            int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            Buffer.MemoryCopy(src.m_Ptr + (long)srcIndex * size, dst.m_Ptr + (long)dstIndex * size, (long)(dst.len - dstIndex) * size, (long)length * size);
        }
        public NativeArray<U> Reinterpret<U>(int expectedTypeSize) where U : struct
        {
            int tSize = System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), uSize = System.Runtime.CompilerServices.Unsafe.SizeOf<U>();
            if (tSize != expectedTypeSize) throw new InvalidOperationException("Reinterpret: type size mismatch");
            long bytes = (long)len * tSize;
            if (bytes % uSize != 0) throw new InvalidOperationException("Reinterpret: length not a multiple of the target size");
            return new NativeArray<U> { m_Ptr = m_Ptr, len = (int)(bytes / uSize) };
        }
        public T[] ToArray() { var r = new T[len]; for (int i = 0; i < len; i++) r[i] = this[i]; return r; }
        public void CopyFrom(T[] src) { for (int i = 0; i < src.Length; i++) this[i] = src[i]; }
        public void CopyFrom(NativeArray<T> src) => Copy(src, this, src.Length);
        public IEnumerator<T> GetEnumerator() { for (int i = 0; i < len; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace Unity.Collections
{
    public static class NativeSortExtension
    {
        public static void Sort<T>(this NativeArray<T> array) where T : unmanaged, IComparable<T> => Sort(array, Comparer<T>.Default);
        public static void Sort<T, U>(this NativeArray<T> array, U comparer) where T : unmanaged where U : IComparer<T>
        {
            // Insertion sort: allocation-free and stable, fine for harness-sized data.
            for (int i = 1; i < array.Length; i++)
            {
                var item = array[i];
                int j = i - 1;
                while (j >= 0 && comparer.Compare(array[j], item) > 0) { array[j + 1] = array[j]; j--; }
                array[j + 1] = item;
            }
        }
    }
}
namespace Unity.Collections.LowLevel.Unsafe
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class NativeDisableContainerSafetyRestrictionAttribute : Attribute { }
    public static unsafe class UnsafeUtility
    {
        public static void MemCpy(void* destination, void* source, long size) => Buffer.MemoryCopy(source, destination, size, size);
        public static void MemMove(void* destination, void* source, long size) => Buffer.MemoryCopy(source, destination, size, size);
        public static int SizeOf<T>() where T : struct => System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
    }
    [AttributeUsage(AttributeTargets.Field)] public sealed class NativeDisableUnsafePtrRestrictionAttribute : Attribute { }
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
    public static class IJobExtensions
    {
        public static JobHandle Schedule<T>(this T job, JobHandle dependsOn = default) where T : struct, IJob { job.Execute(); return default; }
        public static void Run<T>(this T job) where T : struct, IJob => job.Execute();
    }
    public static class IJobParallelForExtensions
    {
        static uint s_Call;

        // Real worker threads run iterations in an unpredictable order. Each schedule here visits the
        // indices in a different permutation (i -> (a*i + b) mod n, gcd(a, n) = 1) so code that
        // depends on parallel write order fails deterministically in the harness. Allocation-free.
        public static JobHandle Schedule<T>(this T job, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default) where T : struct, IJobParallelFor
        {
            if (arrayLength <= 0) return default;
            uint call = ++s_Call;
            uint n = (uint)arrayLength;
            uint a = (call * 2654435761u) % n | 1u;
            while (Gcd(a, n) != 1u) a += 2u;
            uint b = (call * 40503u) % n;
            for (uint i = 0; i < n; i++)
                job.Execute((int)(((ulong)a * i + b) % n));
            return default;
        }

        static uint Gcd(uint x, uint y) { while (y != 0) { uint t = x % y; x = y; y = t; } return x; }
    }
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
namespace UnityEngine.TestTools.Constraints
{
    // Mirrors the Unity Test Framework API: Is.AllocatingGCMemory() plus the
    // ConstraintExpression extension (Is.Not.AllocatingGCMemory()) that needs
    // `using UnityEngine.TestTools.Constraints;`.
    public class Is : NUnit.Framework.Is
    {
        public static AllocatingGCMemoryConstraint AllocatingGCMemory() => new AllocatingGCMemoryConstraint();
    }
    public static class ConstraintExtensions
    {
        public static AllocatingGCMemoryConstraint AllocatingGCMemory(this NUnit.Framework.Constraints.ConstraintExpression chain)
        {
            var c = new AllocatingGCMemoryConstraint();
            chain.Append(c);
            return c;
        }
    }
    public class AllocatingGCMemoryConstraint : NUnit.Framework.Constraints.Constraint
    {
        public override NUnit.Framework.Constraints.ConstraintResult ApplyTo<TActual>(TActual actual)
        {
            var action = (NUnit.Framework.TestDelegate)(object)actual;
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Description = "allocates GC memory (allocated " + allocated + " bytes)";
            return new NUnit.Framework.Constraints.ConstraintResult(this, allocated, allocated > 0);
        }
    }
}

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class UnityTestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class UnitySetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class UnityTearDownAttribute : Attribute { }
    public static class LogAssert
    {
        public static void NoUnexpectedReceived() { }
        public static void Expect(UnityEngine.LogType t, string m) { }
        public static bool ignoreFailingMessages { get; set; }
    }
}
namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
}
