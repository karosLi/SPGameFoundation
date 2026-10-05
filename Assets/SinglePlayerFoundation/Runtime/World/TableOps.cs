using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace SPF.Runtime.World
{
    /// <summary>Raw column storage for bulk Burst operations.</summary>
    internal unsafe struct ColumnRef
    {
        [NativeDisableUnsafePtrRestriction] public byte* Pointer;
        public int Size;
    }

    /// <summary>
    /// Stable compaction of a pooled table: rows flagged dead are dropped and survivors slide forward in
    /// order, copying runs of consecutive survivors with one memcpy per column (no per-row branching on
    /// the column data, sequential memory traffic). Order-preserving, so the result is deterministic.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct CompactJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public byte* Dead;
        [ReadOnly] public NativeArray<ColumnRef> Columns;
        public int Count;
        public NativeArray<int> Result;

        public void Execute()
        {
            int write = 0, read = 0;
            while (read < Count)
            {
                while (read < Count && Dead[read] != 0) read++;
                int start = read;
                while (read < Count && Dead[read] == 0) read++;
                int run = read - start;
                if (run == 0) break;
                if (start != write)
                    for (int c = 0; c < Columns.Length; c++)
                    {
                        var col = Columns[c];
                        UnsafeUtility.MemMove(col.Pointer + (long)write * col.Size, col.Pointer + (long)start * col.Size, (long)run * col.Size);
                    }
                write += run;
            }
            Result[0] = write;
        }
    }

    /// <summary>Gathers one column into a new row order: dst[i] = src[order[i]] (through a scratch copy).</summary>
    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct GatherJob : IJob
    {
        [ReadOnly] public NativeArray<ColumnRef> Columns;
        [ReadOnly] public NativeArray<int> Order;
        [NativeDisableUnsafePtrRestriction] public byte* Scratch;

        public void Execute()
        {
            int n = Order.Length;
            for (int c = 0; c < Columns.Length; c++)
            {
                var col = Columns[c];
                long size = col.Size;
                UnsafeUtility.MemCpy(Scratch, col.Pointer, n * size);
                for (int i = 0; i < n; i++)
                    UnsafeUtility.MemCpy(col.Pointer + i * size, Scratch + Order[i] * size, size);
            }
        }
    }
}
