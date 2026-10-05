using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class PooledTableTests
    {
        static readonly TableKey Bullet = new TableKey("Test.Bullet");
        static readonly ColumnKey<float2> BulletPos = new ColumnKey<float2>(Bullet, "Pos");
        static readonly ColumnKey<int> BulletId = new ColumnKey<int>(Bullet, "Id");

        static SimWorld World(int capacity)
        {
            var layout = new WorldLayout();
            layout.Table(Bullet, capacity).Pooled().Column(BulletPos).Column(BulletId);
            layout.Table(TestKeys.Item, 64).Column(TestKeys.Value).Column(TestKeys.Weight);
            return new SimWorld(layout, 1);
        }

        [Test]
        public void PooledRowsSpawnWithoutHandlesAndCompactInOrder()
        {
            using var world = World(16);
            var table = world.Table(Bullet);
            Assert.IsTrue(table.IsPooled);
            for (int i = 0; i < 10; i++)
            {
                int row = world.Spawn(Bullet);
                world.Column(BulletId).Set(row, i);
            }
            Assert.AreEqual(0, world.Registry.AliveCount, "no registry slots used");
            Assert.Throws<System.InvalidOperationException>(() => world.CreateEntity(Bullet, out _));
            Assert.Throws<System.InvalidOperationException>(() => world.Spawn(TestKeys.Item));

            var dead = table.DeadFlags;
            foreach (int r in new[] { 0, 3, 4, 9 }) dead.Set(r, (byte)1);
            uint version = table.Version;
            world.CompactPools();
            Assert.AreEqual(6, table.Count);
            Assert.Greater(table.Version, version);
            CollectionAssert.AreEqual(new[] { 1, 2, 5, 6, 7, 8 }, world.Column(BulletId).GetSubArray(0, 6).ToArray(), "survivors keep their order");
            for (int i = 0; i < 6; i++) Assert.AreEqual(0, dead[i], "dead flags moved with the rows");

            int again = world.Spawn(Bullet);
            Assert.AreEqual(6, again);
            Assert.AreEqual(0, world.Column(BulletId)[again], "new rows are zeroed");
            for (int i = 0; i < 9; i++) world.Spawn(Bullet);
            Assert.AreEqual(16, table.Count);
            Assert.AreEqual(-1, world.Spawn(Bullet), "full");
            Assert.AreEqual(1, world.CreateFailures);
        }

        [Test]
        public void SpawnRangeAddsZeroedRowsInBulk()
        {
            using var world = World(16);
            for (int i = 0; i < 3; i++) world.Column(BulletId).Set(world.Spawn(Bullet), 7);
            world.Table(Bullet).DeadFlags.Set(1, (byte)1);
            world.CompactPools();
            int start = world.SpawnRange(Bullet, 20, out int added);
            Assert.AreEqual(2, start);
            Assert.AreEqual(14, added, "clamped to capacity");
            Assert.AreEqual(6, world.CreateFailures);
            for (int r = start; r < 16; r++)
            {
                Assert.AreEqual(0, world.Column(BulletId)[r]);
                Assert.AreEqual(0, world.Table(Bullet).DeadFlags[r]);
            }
        }

        [Test]
        public void PooledTablesSnapshotIncludingPendingRemovals()
        {
            using var world = World(16);
            for (int i = 0; i < 5; i++) world.Column(BulletId).Set(world.Spawn(Bullet), i + 1);
            world.Table(Bullet).DeadFlags.Set(2, (byte)1);
            byte[] data;
            using (var buffer = new System.IO.MemoryStream())
            {
                using (var writer = new System.IO.BinaryWriter(buffer)) world.WriteSnapshot(writer);
                data = buffer.ToArray();
            }
            using var copy = World(16);
            using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(data))) copy.ReadSnapshot(reader);
            copy.CompactPools();
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 5 }, copy.Column(BulletId).GetSubArray(0, 4).ToArray());
        }

        [Test]
        public void SortRowsKeepsHandlesValid()
        {
            using var world = World(4);
            var handles = new EntityHandle[20];
            var keys = new NativeArray<uint>(20, Allocator.Temp);
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = world.CreateEntity(TestKeys.Item, out int row);
                world.Column(TestKeys.Value).Set(row, i);
                keys[row] = (uint)((i * 7) % 5);
            }
            world.DestroyEntity(handles[3]);   // swap-back: rows no longer in creation order
            for (int r = 0; r < world.Table(TestKeys.Item).Count; r++) keys[r] = (uint)((world.Column(TestKeys.Value)[r] * 7) % 5);
            world.SortRows(TestKeys.Item, keys);
            var values = world.Column(TestKeys.Value);
            int count = world.Table(TestKeys.Item).Count;
            for (int r = 1; r < count; r++)
                Assert.LessOrEqual((values[r - 1] * 7) % 5, (values[r] * 7) % 5, "sorted by key");
            for (int i = 0; i < handles.Length; i++)
            {
                if (i == 3) { Assert.IsFalse(world.Registry.IsAlive(handles[i])); continue; }
                Assert.IsTrue(world.Registry.TryResolve(handles[i], out _, out int row));
                Assert.AreEqual(i, values[row], "handles follow their rows");
                Assert.AreEqual(handles[i], world.Table(TestKeys.Item).Handles[row]);
            }
            keys.Dispose();
        }

        [Test]
        public void MortonCodesInterleaveAndKeepNeighboursClose()
        {
            Assert.AreEqual(0u, Morton.Encode(new int2(0, 0)));
            Assert.AreEqual(1u, Morton.Encode(new int2(1, 0)));
            Assert.AreEqual(2u, Morton.Encode(new int2(0, 1)));
            Assert.AreEqual(3u, Morton.Encode(new int2(1, 1)));
            Assert.AreEqual(0xFFFFFFFFu, Morton.Encode(new int2(70000, 70000)), "clamped");
            Assert.AreEqual(Morton.Encode(new int2(2, 3)), Morton.Encode(new float2(5.5f, 7.9f), float2.zero, 2f));
        }
    }
}
