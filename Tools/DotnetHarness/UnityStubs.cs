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
