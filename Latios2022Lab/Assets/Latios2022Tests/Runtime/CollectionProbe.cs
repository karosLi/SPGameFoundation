using System;
using Latios;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Latios2022Lab
{
    [DisableAutoCreation]
    public partial class LabWriterSystem : SubSystem
    {
        public Entity Owner;
        public int BatchSize;
        public bool ThrowAfterSchedule;
        protected override void OnUpdate()
        {
            var data = latiosWorldUnmanaged.GetCollectionComponent<LabCollection>(Owner, false);
            Dependency = new FillValuesJob { Values = data.Values }.Schedule(data.Values.Length, BatchSize, Dependency);
            if (ThrowAfterSchedule) throw new InvalidOperationException("Lab scheduled failure");
        }
    }

    [DisableAutoCreation, UpdateAfter(typeof(LabWriterSystem))]
    public partial class LabReaderSystem : SubSystem
    {
        public Entity Owner;
        public NativeArray<int> Output;
        protected override void OnUpdate()
        {
            var data = latiosWorldUnmanaged.GetCollectionComponent<LabCollection>(Owner, true);
            Dependency = new SumValuesJob { Values = data.Values, Output = Output }.Schedule(Dependency);
        }
    }

    public enum CollectionExit { Remove, DestroyEntity, DisposeWorld }

    public static class CollectionProbe
    {
        public const int Count = 257;
        public const int ExpectedSum = Count * 17 + Count * (Count - 1) / 2;

        // No completing the writer/reader before removal: the framework must own the chain.
        // Native safety checks plus the disposal job's last read witness validate that ordering.
        public static void Run(int batchSize, CollectionExit exit)
        {
            using var witness = new NativeArray<int>(2, Allocator.Persistent);
            using var output = new NativeArray<int>(1, Allocator.Persistent);
            var world = LabWorld.Create("S1a collection");
            try
            {
                var owner = world.EntityManager.CreateEntity();
                Add(world, owner, witness);
                var writer = world.GetOrCreateSystemManaged<LabWriterSystem>();
                writer.Owner = owner;
                writer.BatchSize = batchSize;
                var reader = world.GetOrCreateSystemManaged<LabReaderSystem>();
                reader.Owner = owner;
                reader.Output = output;
                world.simulationSystemGroup.AddSystemToUpdateList(writer);
                world.simulationSystemGroup.AddSystemToUpdateList(reader);
                world.simulationSystemGroup.SortSystems();
                world.simulationSystemGroup.Update();
                if (exit == CollectionExit.Remove)
                {
                    if (!world.latiosWorldUnmanaged.RemoveCollectionComponentAndDispose<LabCollection>(owner))
                        throw new InvalidOperationException("Collection was not removed.");
                    if (world.latiosWorldUnmanaged.HasCollectionComponent<LabCollection>(owner))
                        throw new InvalidOperationException("Removed collection remains present.");
                }
                else if (exit == CollectionExit.DestroyEntity)
                {
                    world.EntityManager.DestroyEntity(owner);
                    world.initializationSystemGroup.Update();
                    // Reactive cleanup may schedule disposal. World disposal below drains it.
                }
                world.Dispose();
                world = null;
                if (output[0] != ExpectedSum || witness[0] != 1 || witness[1] != 17)
                    throw new InvalidOperationException("Collection dependency/disposal witness mismatch.");
            }
            finally
            {
                world?.Dispose();
            }
        }

        public static void Add(LatiosWorld world, Entity owner, NativeArray<int> witness)
        {
            var values = new NativeArray<int>(Count, Allocator.Persistent);
            bool transferred = false;
            try
            {
                world.latiosWorldUnmanaged.AddOrSetCollectionComponentAndDisposeOld(owner,
                    new LabCollection { Values = values, DisposalWitness = witness });
                transferred = true;
                // This setup runs outside a tracked system and uses no asynchronous access.
                world.latiosWorldUnmanaged.UpdateCollectionComponentMainThreadAccess<LabCollection>(owner, false);
            }
            finally
            {
                if (!transferred) values.Dispose();
            }
        }
    }
}
