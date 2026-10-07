using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SPF.Tests.EditMode
{
    public class DestroyQueueSnapshotTests
    {
        static byte[] Capture(SimWorld world)
        { using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream)) world.WriteSnapshot(writer); return stream.ToArray(); }
        static void Restore(SimWorld world, byte[] bytes)
        { using var reader = new BinaryReader(new MemoryStream(bytes)); world.ReadSnapshot(reader); }
        [BurstCompile(CompileSynchronously = true)]
        struct EnqueueJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public ParallelQueue<EntityHandle>.Writer Queue;
            public void Execute(int i) => Queue.TryAdd(Handles[i % Handles.Length]);
        }
        [TestCase(1)] [TestCase(7)] [TestCase(32)]
        public void ScheduledArrivalOrdersHaveCanonicalRepeatedSnapshotBytes(int batch)
        {
            using var world = TestKeys.CreateWorld(32, destroyQueueCapacity: 128);
            using var handles = new NativeArray<EntityHandle>(32, Allocator.TempJob); var writable = handles;
            for (int i = 0; i < 32; i++) writable[i] = world.CreateEntity(TestKeys.Item, out _);
            var queue = world.Resource(SimWorld.DestroyQueueKey); byte[] expected = null;
            for (int repeat = 0; repeat < 12; repeat++)
            {
                queue.OnReset(); new EnqueueJob { Handles = handles, Queue = queue.AsWriter() }.Schedule(64, batch).Complete();
                var actual = Capture(world); Assert.AreEqual(64, queue.Queue.Count, "snapshot retains duplicate accepted requests");
                Assert.AreEqual(32, world.Table(TestKeys.Item).Count, "snapshot never performs destruction");
                CollectionAssert.AreEqual(actual, Capture(world), "repeated write");
                if (expected == null) expected = actual; else CollectionAssert.AreEqual(expected, actual, "permuted schedule=" + repeat);
            }
        }
        [Test] public void RestoreResetAndPlaybackKeepDuplicatesStaleHandlesAndRecyclingSemantics()
        {
            using var a = TestKeys.CreateWorld(8); using var b = TestKeys.CreateWorld(8);
            var handles = new EntityHandle[4]; for (int i = 0; i < handles.Length; i++) handles[i] = a.CreateEntity(TestKeys.Item, out _);
            var q = a.Resource(SimWorld.DestroyQueueKey);
            q.Request(handles[3]); q.Request(handles[1]); q.Request(handles[1]); q.Request(new EntityHandle(handles[2].Index, handles[2].Generation + 1)); q.Request(EntityHandle.Null);
            var bytes = Capture(a); Restore(b, bytes); Restore(b, bytes);
            Assert.AreEqual(5, b.Resource(SimWorld.DestroyQueueKey).Queue.Count); CollectionAssert.AreEqual(bytes, Capture(b));
            a.PlaybackDestroys(); b.PlaybackDestroys();
            Assert.AreEqual(2, a.Table(TestKeys.Item).Count); CollectionAssert.AreEqual(Capture(a), Capture(b));
            Assert.AreEqual(a.CreateEntity(TestKeys.Item, out _), b.CreateEntity(TestKeys.Item, out _));
            b.Reset(); Assert.AreEqual(0, b.Resource(SimWorld.DestroyQueueKey).Queue.Count);
            Restore(b, bytes); CollectionAssert.AreEqual(bytes, Capture(b));
        }
    }
}
