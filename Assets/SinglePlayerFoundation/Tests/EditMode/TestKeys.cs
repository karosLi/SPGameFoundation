using SPF.Contracts;
using SPF.Runtime.World;

namespace SPF.Tests.EditMode
{
    static class TestKeys
    {
        public static readonly TableKey Item = new TableKey("Test.Item");
        public static readonly ColumnKey<int> Value = new ColumnKey<int>(Item, "Value");
        public static readonly ColumnKey<float> Weight = new ColumnKey<float>(Item, "Weight");
        public static readonly ResourceKey<SnapshotBuffer<int>> Snapshot = new ResourceKey<SnapshotBuffer<int>>("Test.Snapshot");

        public static SimWorld CreateWorld(int capacity, int destroyQueueCapacity = 64)
        {
            var layout = new WorldLayout { DestroyQueueCapacity = destroyQueueCapacity };
            layout.Table(Item, capacity).Column(Value).Column(Weight);
            layout.Resource(Snapshot, new SnapshotBuffer<int>(capacity));
            return new SimWorld(layout, seed: 7);
        }
    }
}
