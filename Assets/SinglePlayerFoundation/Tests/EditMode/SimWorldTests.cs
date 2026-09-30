using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.World;

namespace SPF.Tests.EditMode
{
    public class SimWorldTests
    {
        [Test]
        public void SwapBackRemovalKeepsHandlesAndColumnsConsistent()
        {
            using var world = TestKeys.CreateWorld(8);
            var values = world.Column(TestKeys.Value);
            var handles = new EntityHandle[5];
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = world.CreateEntity(TestKeys.Item, out int row);
                values[row] = 100 + i;
            }

            Assert.IsTrue(world.DestroyEntity(handles[1]));

            var table = world.Table(TestKeys.Item);
            Assert.AreEqual(4, table.Count);
            for (int i = 0; i < handles.Length; i++)
            {
                if (i == 1) continue;
                Assert.IsTrue(world.Registry.TryResolve(handles[i], out _, out int row));
                Assert.AreEqual(handles[i], table.Handles[row]);
                Assert.AreEqual(100 + i, values[row], "column data must follow its entity");
            }
            Assert.IsFalse(world.Registry.IsAlive(handles[1]));
        }

        [Test]
        public void NewRowsAreZeroed()
        {
            using var world = TestKeys.CreateWorld(2);
            var values = world.Column(TestKeys.Value);
            var h = world.CreateEntity(TestKeys.Item, out int row);
            values[row] = 42;
            world.DestroyEntity(h);
            world.CreateEntity(TestKeys.Item, out row);
            Assert.AreEqual(0, values[row]);
        }

        [Test]
        public void CreateBeyondCapacityFailsGracefully()
        {
            using var world = TestKeys.CreateWorld(2);
            world.CreateEntity(TestKeys.Item, out _);
            world.CreateEntity(TestKeys.Item, out _);
            var third = world.CreateEntity(TestKeys.Item, out int row);

            Assert.IsTrue(third.IsNull);
            Assert.AreEqual(-1, row);
            Assert.AreEqual(1, world.CreateFailures);
        }

        [Test]
        public void DestroyQueueIgnoresDuplicateAndStaleHandles()
        {
            using var world = TestKeys.CreateWorld(4);
            var a = world.CreateEntity(TestKeys.Item, out _);
            var b = world.CreateEntity(TestKeys.Item, out _);
            var queue = world.Resource(SimWorld.DestroyQueueKey);
            queue.Request(a);
            queue.Request(a);
            queue.Request(EntityHandle.Null);

            world.PlaybackDestroys();

            Assert.AreEqual(1, world.Table(TestKeys.Item).Count);
            Assert.IsTrue(world.Registry.IsAlive(b));
        }

        [Test]
        public void DestroyQueueOverflowIsCountedNotFatal()
        {
            using var world = TestKeys.CreateWorld(8, destroyQueueCapacity: 2);
            var queue = world.Resource(SimWorld.DestroyQueueKey);
            for (int i = 0; i < 5; i++)
                queue.Request(world.CreateEntity(TestKeys.Item, out _));

            world.PlaybackDestroys();

            Assert.AreEqual(3, world.Table(TestKeys.Item).Count);
            Assert.AreEqual(3, queue.TotalOverflow);
        }

        [Test]
        public void ResetEmptiesTablesAndInvalidatesHandles()
        {
            using var world = TestKeys.CreateWorld(4);
            var a = world.CreateEntity(TestKeys.Item, out _);
            world.Reset();

            Assert.AreEqual(0, world.Table(TestKeys.Item).Count);
            Assert.IsFalse(world.Registry.IsAlive(a));
        }
    }
}
