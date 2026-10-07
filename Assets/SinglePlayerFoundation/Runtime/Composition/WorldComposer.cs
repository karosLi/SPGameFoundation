using System;
using System.Collections.Generic;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;

namespace SPF.Runtime.Composition
{
    /// <summary>Composition root: modules → world layout → world, and modules → system pipeline.</summary>
    public static class WorldComposer
    {
        public static SimWorld BuildWorld(IReadOnlyList<IGameplayModule> modules, SessionSettings settings, uint seed)
        {
            settings.Validate();
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            var layout = new WorldLayout { DestroyQueueCapacity = settings.DestroyQueueCapacity };
            try
            {
                var ids = new HashSet<string>();
                foreach (var module in modules)
                {
                    if (module == null)
                        throw new ArgumentException("Mode contains an empty module slot.");
                    if (!ids.Add(module.Id))
                        throw new ArgumentException($"Module '{module.Id}' is listed twice.");
                    module.DeclareData(layout);
                }
                return new SimWorld(layout, seed);
            }
            catch (Exception failure)
            {
                CleanupErrors.Try(layout.Dispose, ref failure);
                throw;
            }
        }

        public static TickPipeline BuildPipeline(IReadOnlyList<IGameplayModule> modules, SimWorld world)
        {
            var registry = new SystemRegistry();
            foreach (var module in modules)
                module.RegisterSystems(registry);
            return new TickPipeline(world, registry.Systems);
        }
    }
}
