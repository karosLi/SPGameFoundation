using System.Collections.Generic;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using UnityEngine;

namespace SPF.Runtime.Composition
{
    /// <summary>
    /// A self-contained piece of gameplay (movement, body, food, AI...). A game mode is a list of
    /// modules; the composition root asks each one for its data and systems. Config baking, collision
    /// rules and presentation adapters join this contract in later milestones.
    /// </summary>
    public interface IGameplayModule
    {
        string Id { get; }

        /// <summary>Declare tables, columns (including extension columns on other modules' tables) and resources.</summary>
        void DeclareData(WorldLayout layout);

        void RegisterSystems(SystemRegistry registry);
    }

    /// <summary>ScriptableObject base so modules can be configured and listed in a <see cref="ModeDefinition"/>.</summary>
    public abstract class GameplayModuleAsset : ScriptableObject, IGameplayModule
    {
        public virtual string Id => GetType().Name;
        public abstract void DeclareData(WorldLayout layout);
        public abstract void RegisterSystems(SystemRegistry registry);
    }

    public sealed class SystemRegistry
    {
        readonly List<ISimSystem> m_Systems = new List<ISimSystem>();

        public IReadOnlyList<ISimSystem> Systems => m_Systems;

        public SystemRegistry Add(ISimSystem system)
        {
            m_Systems.Add(system);
            return this;
        }
    }
}
