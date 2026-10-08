using Latios;
using Latios.Transforms;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Latios2022Lab
{
    // Suppress default game-world injection. Every fixture owns and disposes its world.
    // No broad Unity injection or optional Myri/Kinemation/Calligraphics/Mimic installer.
    // The owned world explicitly supplies its two standard initialization ordering targets.
    [Preserve]
    public sealed class LabBootstrap : ICustomBootstrap
    {
        public bool Initialize(string defaultWorldName) => true;
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
