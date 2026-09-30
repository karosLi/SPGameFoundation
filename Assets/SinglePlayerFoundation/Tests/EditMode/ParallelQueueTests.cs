using NUnit.Framework;
using SPF.Contracts.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SPF.Tests.EditMode
{
    public class ParallelQueueTests
    {
        [BurstCompile]
        struct FillJob : IJobParallelFor
        {
            public ParallelQueue<int>.Writer Writer;
            public void Execute(int index) => Writer.TryAdd(index);
        }

        [Test]
        public void ParallelWritesPastCapacityAreDroppedAndCounted()
        {
            var queue = new ParallelQueue<int>(100, Allocator.TempJob);
            try
            {
                new FillJob { Writer = queue.AsWriter() }.Schedule(1000, 16).Complete();

                Assert.AreEqual(100, queue.Count);
                Assert.AreEqual(900, queue.Overflow);
                var seen = new bool[1000];
                for (int i = 0; i < queue.Count; i++)
                {
                    Assert.IsFalse(seen[queue[i]], "no item may be written twice");
                    seen[queue[i]] = true;
                }
            }
            finally
            {
                queue.Dispose();
            }
        }

        [Test]
        public void ClearResetsCountAndOverflow()
        {
            var queue = new ParallelQueue<int>(1, Allocator.Temp);
            try
            {
                Assert.IsTrue(queue.TryAdd(1));
                Assert.IsFalse(queue.TryAdd(2));
                queue.Clear();
                Assert.AreEqual(0, queue.Count);
                Assert.AreEqual(0, queue.Overflow);
            }
            finally
            {
                queue.Dispose();
            }
        }
    }
}
