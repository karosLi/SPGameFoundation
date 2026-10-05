using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.World;

namespace SPF.Tests.EditMode
{
    public class LevelScopeTests
    {
        static readonly TableKey Monster = new TableKey("Test.Monster");
        static readonly ColumnKey<int> MonsterHp = new ColumnKey<int>(Monster, "Hp");
        static readonly ResourceKey<EventQueue<int>> LevelEvents = new ResourceKey<EventQueue<int>>("Test.LevelEvents");
        static readonly ResourceKey<EventQueue<int>> SessionEvents = new ResourceKey<EventQueue<int>>("Test.SessionEvents");

        [Test]
        public void ClearLevelRemovesOnlyLevelData()
        {
            var layout = new WorldLayout();
            layout.Table(TestKeys.Item, 8).Column(TestKeys.Value).Column(TestKeys.Weight);
            layout.Table(Monster, 8).LevelScoped().Column(MonsterHp);
            layout.Resource(LevelEvents, new EventQueue<int>(4), levelScoped: true);
            layout.Resource(SessionEvents, new EventQueue<int>(4));
            using var world = new SimWorld(layout, 3);

            var kept = world.CreateEntity(TestKeys.Item, out _);
            var monsters = new EntityHandle[5];
            for (int i = 0; i < monsters.Length; i++) monsters[i] = world.CreateEntity(Monster, out _);
            world.Resource(LevelEvents).TryAdd(1);
            world.Resource(SessionEvents).TryAdd(2);

            Assert.AreEqual(5, world.ClearLevel());
            Assert.AreEqual(0, world.Table(Monster).Count);
            foreach (var m in monsters) Assert.IsFalse(world.Registry.IsAlive(m));
            Assert.IsTrue(world.Registry.IsAlive(kept));
            Assert.AreEqual(1, world.Table(TestKeys.Item).Count);
            Assert.AreEqual(0, world.Resource(LevelEvents).Count);
            Assert.AreEqual(1, world.Resource(SessionEvents).Count);
            Assert.AreEqual(1, world.LevelVersion);
            Assert.AreEqual(1, world.Registry.AliveCount);

            // Slots are recycled for the next level.
            for (int i = 0; i < 5; i++) Assert.IsFalse(world.CreateEntity(Monster, out _).IsNull);
            Assert.AreEqual(6, world.Registry.AliveCount);
        }

        [Test]
        public void LevelScopedResourcesMustBeResettable()
        {
            var layout = new WorldLayout();
            Assert.Throws<System.ArgumentException>(() =>
                layout.Resource(new ResourceKey<object>("Test.Plain"), new object(), levelScoped: true));
        }
    }
}
