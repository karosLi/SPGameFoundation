using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class WorldSnapshotTests
    {
        static byte[] Capture(SimWorld world)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer)) world.WriteSnapshot(writer);
            return buffer.ToArray();
        }

        static void Restore(SimWorld world, byte[] data)
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            world.ReadSnapshot(reader);
        }

        [Test]
        public void RestoredWorldMatchesRowsHandlesAndFutureAllocations()
        {
            using var world = TestKeys.CreateWorld(16);
            var handles = new EntityHandle[10];
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = world.CreateEntity(TestKeys.Item, out int row);
                world.Column(TestKeys.Value).Set(row, i * 10);
                world.Column(TestKeys.Weight).Set(row, i * 0.5f);
            }
            world.DestroyEntity(handles[2]);
            world.DestroyEntity(handles[7]);
            world.Resource(SimWorld.DestroyQueueKey).Request(handles[4]);   // pending destroy is part of the state
            var data = Capture(world);

            // Restore into a world that went further (different rows, higher generations): in-place restore.
            using var other = TestKeys.CreateWorld(16);
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < 14; i++) other.CreateEntity(TestKeys.Item, out _);
                other.Reset();
            }
            Restore(other, data);

            var table = world.Table(TestKeys.Item);
            var restored = other.Table(TestKeys.Item);
            Assert.AreEqual(table.Count, restored.Count);
            Assert.AreEqual(world.Registry.AliveCount, other.Registry.AliveCount);
            for (int row = 0; row < table.Count; row++)
            {
                Assert.AreEqual(table.Handles[row], restored.Handles[row]);
                Assert.AreEqual(world.Column(TestKeys.Value)[row], other.Column(TestKeys.Value)[row]);
                Assert.AreEqual(world.Column(TestKeys.Weight)[row], other.Column(TestKeys.Weight)[row]);
                Assert.IsTrue(other.Registry.TryResolve(table.Handles[row], out _, out int r));
                Assert.AreEqual(row, r);
            }
            Assert.IsFalse(other.Registry.IsAlive(handles[2]));
            Assert.AreEqual(1, other.Resource(SimWorld.DestroyQueueKey).Queue.Count);

            // Both continue identically: same recycled slots and generations.
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(world.CreateEntity(TestKeys.Item, out _), other.CreateEntity(TestKeys.Item, out _));
            CollectionAssert.AreEqual(Capture(world), Capture(other));
        }

        [Test]
        public void SnapshotIsRejectedByADifferentWorld()
        {
            using var world = TestKeys.CreateWorld(16);
            world.CreateEntity(TestKeys.Item, out _);
            var data = Capture(world);
            using var smaller = TestKeys.CreateWorld(8);
            Assert.Throws<InvalidDataException>(() => Restore(smaller, data));
            data[0] ^= 0xFF;
            using var same = TestKeys.CreateWorld(16);
            Assert.Throws<InvalidDataException>(() => Restore(same, data));
        }

        [Test]
        public void JobResourcesWithoutSnapshotSupportBlockSaving()
        {
            var key = new ResourceKey<Opaque>("Test.Opaque");
            var layout = new WorldLayout();
            layout.Table(TestKeys.Item, 4).Column(TestKeys.Value).Column(TestKeys.Weight);
            layout.Resource(key, new Opaque());
            using var world = new SimWorld(layout, 1);
            CollectionAssert.AreEqual(new[] { "Test.Opaque" }, world.SnapshotGaps());
            Assert.Throws<System.NotSupportedException>(() => Capture(world));
        }

        sealed class Opaque : IJobData { }

        [Test]
        public void SpatialStructuresRoundTrip()
        {
            using var grid = new SpatialGrid(new int2(8, 8), 2f, 64, largeRadius: 3f);
            var staging = grid.Staging;
            for (int i = 0; i < 20; i++)
                staging[i] = new GridEntry { Position = new float2(i * 0.7f, i * 0.4f), Radius = i == 3 ? 5f : 0.5f, Owner = i };
            grid.StagingCount.Set(0, 20);
            grid.ScheduleBuild(default).Complete();

            using var copy = new SpatialGrid(new int2(8, 8), 2f, 64, largeRadius: 3f);
            using (var buffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) grid.WriteSnapshot(writer);
                buffer.Position = 0;
                using var reader = new BinaryReader(buffer);
                copy.ReadSnapshot(reader);
            }
            Assert.AreEqual(grid.EntryCount, copy.EntryCount);
            Assert.AreEqual(grid.LargeEntryCount, copy.LargeEntryCount);
            var a = new Counter(); var b = new Counter();
            grid.AsReader().Query(new float2(5f, 4f), 4f, ref a);
            copy.AsReader().Query(new float2(5f, 4f), 4f, ref b);
            Assert.Greater(a.Sum, 0);
            Assert.AreEqual(a.Sum, b.Sum);

            using var map = new TileMap(new int2(6, 5), 1f);
            map[new int2(2, 3)] = 1;
            using var mapCopy = new TileMap(new int2(6, 5), 1f);
            using (var buffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) map.WriteSnapshot(writer);
                buffer.Position = 0;
                using var reader = new BinaryReader(buffer);
                mapCopy.ReadSnapshot(reader);
            }
            Assert.AreEqual(map.Version, mapCopy.Version);
            Assert.AreEqual(1, mapCopy[new int2(2, 3)]);
        }

        struct Counter : IGridVisitor
        {
            public int Sum;
            public bool Visit(in GridEntry entry) { Sum += entry.Owner + 1; return true; }
        }
    }
}
