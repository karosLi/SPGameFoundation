using Latios;
using Latios.Transforms;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Latios2022Lab
{
    // Keep the engine's required default World empty. Fixtures own their separate worlds.
    // No broad Unity injection or optional Myri/Kinemation/Calligraphics/Mimic installer.
    // Fixture-owned Latios worlds supply their two initialization ordering targets.
    [Preserve]
    public sealed class LabBootstrap : ICustomBootstrap
    {
        public bool Initialize(string defaultWorldName)
        {
            // DefaultWorldInitialization registers shutdown before calling this bootstrap.
            // Its World.DisposeAllWorlds path owns this empty world's exit/reload lifetime.
            World.DefaultGameObjectInjectionWorld = new World(defaultWorldName, WorldFlags.Game);
            return true;
        }
    }

    public static class LabWorld
    {
        public static LatiosWorld Create(string name, bool installTransforms = false)
        {
            var world = new LatiosWorld(name);
            try
            {
                world.zeroToleranceForExceptions = true;
                world.initializationSystemGroup.AddSystemToUpdateList(
                    world.GetOrCreateSystemManaged<BeginInitializationEntityCommandBufferSystem>());
                world.initializationSystemGroup.AddSystemToUpdateList(
                    world.GetOrCreateSystemManaged<Unity.Scenes.SceneSystemGroup>());
                world.ForceCreateNewSceneBlackboardEntityAndCallOnNewScene();
                if (installTransforms)
                    TransformsBootstrap.InstallTransforms(world, world.simulationSystemGroup);
                world.initializationSystemGroup.SortSystems();
                world.simulationSystemGroup.SortSystems();
                return world;
            }
            catch
            {
                world.Dispose();
                throw;
            }
        }
    }
}
