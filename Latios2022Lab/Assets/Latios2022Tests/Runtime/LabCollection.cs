using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Latios2022Lab
{
    // partial is essential: the pinned Latios generator supplies its Exist/Cleanup types.
    public partial struct LabCollection : ICollectionComponent
    {
        public NativeArray<int> Values;
        // Borrowed from the fixture; never disposed by this component.
        public NativeArray<int> DisposalWitness;

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            if (!Values.IsCreated)
                return inputDeps;
            var witness = new RecordDisposalJob { Values = Values, Witness = DisposalWitness }.Schedule(inputDeps);
            return Values.Dispose(witness);
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct RecordDisposalJob : IJob
        {
            [ReadOnly] public NativeArray<int> Values;
            public NativeArray<int> Witness;
            public void Execute()
            {
                Witness[0]++;
                Witness[1] = Values[0];
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct FillValuesJob : IJobParallelFor
    {
        public NativeArray<int> Values;
        public void Execute(int index) => Values[index] = index + 17;
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct SumValuesJob : IJob
    {
        [ReadOnly] public NativeArray<int> Values;
        public NativeArray<int> Output;
        public void Execute()
        {
            int sum = 0;
            for (int i = 0; i < Values.Length; i++) sum += Values[i];
            Output[0] = sum;
        }
    }
}
