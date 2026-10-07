using System.Collections.Generic;
using UnityEngine;

namespace SPF.Runtime.Composition
{
    /// <summary>A playable mode: its modules plus session-wide simulation settings.</summary>
    [CreateAssetMenu(menuName = "SPF/Mode Definition", fileName = "ModeDefinition")]
    public sealed class ModeDefinition : ScriptableObject
    {
        [SerializeField] List<GameplayModuleAsset> m_Modules = new List<GameplayModuleAsset>();

        [Header("Simulation")]
        [SerializeField, Range(10, 60)] int m_TickRate = 30;
        [SerializeField, Range(1, 8)] int m_MaxTicksPerFrame = 3;
        [SerializeField, Min(64)] int m_DestroyQueueCapacity = 4096;

        public IReadOnlyList<GameplayModuleAsset> Modules => m_Modules;

        /// <summary>Builds a mode in code (bootstraps and tests).</summary>
        public static ModeDefinition Create(IEnumerable<GameplayModuleAsset> modules, SessionSettings settings)
        {
            var mode = CreateInstance<ModeDefinition>();
            mode.name = "ModeDefinition (runtime)";
            mode.m_Modules = new List<GameplayModuleAsset>(modules);
            mode.m_TickRate = settings.TickRate;
            mode.m_MaxTicksPerFrame = settings.MaxTicksPerFrame;
            mode.m_DestroyQueueCapacity = settings.DestroyQueueCapacity;
            return mode;
        }

        public SessionSettings Settings => new SessionSettings
        {
            TickRate = m_TickRate,
            MaxTicksPerFrame = m_MaxTicksPerFrame,
            DestroyQueueCapacity = m_DestroyQueueCapacity,
        };
    }

    public struct SessionSettings
    {
        public int TickRate;
        public int MaxTicksPerFrame;
        public int DestroyQueueCapacity;

        /// <summary>Runtime limits, independent of authoring UI ranges. A zero-capacity destroy
        /// queue is valid: every request is counted as overflow.</summary>
        public void Validate()
        {
            if (TickRate <= 0) throw new System.ArgumentOutOfRangeException(nameof(TickRate));
            if (MaxTicksPerFrame <= 0) throw new System.ArgumentOutOfRangeException(nameof(MaxTicksPerFrame));
            if (DestroyQueueCapacity < 0) throw new System.ArgumentOutOfRangeException(nameof(DestroyQueueCapacity));
        }

        public static SessionSettings Default => new SessionSettings
        {
            TickRate = 30,
            MaxTicksPerFrame = 3,
            DestroyQueueCapacity = 4096,
        };
    }
}
