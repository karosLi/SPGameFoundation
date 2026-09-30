using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.World;

namespace SPF.Tests.EditMode
{
    public class EntityRegistryTests
    {
        [Test]
        public void AllocateResolveRelease()
        {
            using var registry = new EntityRegistry(4);
            var handle = registry.Allocate(tableIndex: 2, row: 9);

            Assert.IsFalse(handle.IsNull);
            Assert.IsTrue(registry.TryResolve(handle, out int table, out int row));
            Assert.AreEqual(2, table);
            Assert.AreEqual(9, row);

            Assert.IsTrue(registry.Release(handle));
            Assert.IsFalse(registry.IsAlive(handle));
            Assert.IsFalse(registry.Release(handle), "double release must be ignored");
            Assert.AreEqual(0, registry.AliveCount);
        }

        [Test]
        public void RecycledSlotGetsNewGeneration()
        {
            using var registry = new EntityRegistry(1);
            var first = registry.Allocate(0, 0);
            registry.Release(first);
            var second = registry.Allocate(0, 0);

            Assert.AreEqual(first.Index, second.Index);
            Assert.AreNotEqual(first.Generation, second.Generation);
            Assert.IsFalse(registry.IsAlive(first));
            Assert.IsTrue(registry.IsAlive(second));
        }

        [Test]
        public void FullRegistryReturnsNull()
        {
            using var registry = new EntityRegistry(2);
            registry.Allocate(0, 0);
            registry.Allocate(0, 1);
            Assert.IsTrue(registry.Allocate(0, 2).IsNull);
        }

        [Test]
        public void ClearInvalidatesAllHandles()
        {
            using var registry = new EntityRegistry(3);
            var a = registry.Allocate(0, 0);
            var b = registry.Allocate(0, 1);
            registry.Clear();

            Assert.IsFalse(registry.IsAlive(a));
            Assert.IsFalse(registry.IsAlive(b));
            Assert.AreEqual(0, registry.AliveCount);
            for (int i = 0; i < 3; i++)
                Assert.IsFalse(registry.Allocate(0, i).IsNull);
        }

        [Test]
        public void DefaultHandleIsNeverAlive()
        {
            using var registry = new EntityRegistry(1);
            registry.Allocate(0, 0);
            Assert.IsFalse(registry.IsAlive(EntityHandle.Null));
        }
    }
}
