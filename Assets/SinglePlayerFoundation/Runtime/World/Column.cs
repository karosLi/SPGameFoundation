using System;
using Unity.Collections;

namespace SPF.Runtime.World
{
    /// <summary>Untyped view of a table column, used for structural operations on the main thread.</summary>
    internal interface IColumn : IDisposable
    {
        void Move(int from, int to);
        void Reset(int index);
    }

    internal sealed class Column<T> : IColumn where T : unmanaged
    {
        public NativeArray<T> Data;

        public Column(int capacity)
        {
            Data = new NativeArray<T>(capacity, Allocator.Persistent);
        }

        public void Move(int from, int to) => Data[to] = Data[from];
        public void Reset(int index) => Data[index] = default;

        public void Dispose()
        {
            if (Data.IsCreated) Data.Dispose();
        }
    }
}
