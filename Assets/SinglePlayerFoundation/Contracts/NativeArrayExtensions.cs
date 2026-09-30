using System.Runtime.CompilerServices;
using Unity.Collections;

namespace SPF.Contracts
{
    public static class NativeArrayExtensions
    {
        /// <summary>
        /// Writes an element of an array returned by a property / method (C# forbids indexer assignment
        /// on such temporaries; the array handle still points to the same memory).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Set<T>(this NativeArray<T> array, int index, T value) where T : struct => array[index] = value;
    }
}
