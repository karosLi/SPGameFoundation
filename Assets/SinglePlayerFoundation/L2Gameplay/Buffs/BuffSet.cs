using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SPF.L2.Buffs
{
    /// <summary>What a buff changes. Game modes map their buff kinds onto these stats in config.</summary>
    public enum BuffStat : byte
    {
        None = 0,
        SpeedMultiplier = 1,
        TurnMultiplier = 2,
        MagnetRadius = 3,
        Shield = 4,
        BoostCostMultiplier = 5,
    }

    /// <summary>Static definition of a buff kind (baked from config).</summary>
    public struct BuffDefinition
    {
        public BuffStat Stat;
        public float Value;
        public float Duration;
    }

    public struct BuffSlot
    {
        public byte Kind;       // 0 = empty; index into the definitions table otherwise
        public float Remaining;
    }

    /// <summary>Resulting stats after all active buffs, consumed read-only by movement / collision.</summary>
    public struct EffectiveStats
    {
        public float SpeedMultiplier;
        public float TurnMultiplier;
        public float MagnetRadius;
        public float BoostCostMultiplier;
        public bool Shield;

        public static EffectiveStats Neutral => new EffectiveStats
        {
            SpeedMultiplier = 1f,
            TurnMultiplier = 1f,
            MagnetRadius = 0f,
            BoostCostMultiplier = 1f,
            Shield = false,
        };
    }

    /// <summary>
    /// Fixed four-slot buff container stored inline in an entity column (no allocation, Burst friendly).
    /// Re-applying a kind refreshes it; a full set replaces the slot closest to expiring.
    /// </summary>
    public struct BuffSet
    {
        public const int Capacity = 4;

        public BuffSlot S0, S1, S2, S3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BuffSlot Get(int i) => i == 0 ? S0 : i == 1 ? S1 : i == 2 ? S2 : S3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int i, BuffSlot slot)
        {
            switch (i)
            {
                case 0: S0 = slot; break;
                case 1: S1 = slot; break;
                case 2: S2 = slot; break;
                default: S3 = slot; break;
            }
        }

        public bool Has(byte kind)
        {
            for (int i = 0; i < Capacity; i++)
                if (Get(i).Kind == kind) return true;
            return false;
        }

        public void Apply(byte kind, float duration)
        {
            int target = -1;
            float lowest = float.MaxValue;
            for (int i = 0; i < Capacity; i++)
            {
                var slot = Get(i);
                if (slot.Kind == kind) { target = i; break; }
                float remaining = slot.Kind == 0 ? -1f : slot.Remaining;
                if (remaining < lowest) { lowest = remaining; target = i; }
            }
            Set(target, new BuffSlot { Kind = kind, Remaining = duration });
        }

        public void Remove(byte kind)
        {
            for (int i = 0; i < Capacity; i++)
                if (Get(i).Kind == kind) Set(i, default);
        }

        /// <summary>Counts down and clears expired slots.</summary>
        public void Tick(float deltaTime)
        {
            for (int i = 0; i < Capacity; i++)
            {
                var slot = Get(i);
                if (slot.Kind == 0) continue;
                slot.Remaining -= deltaTime;
                Set(i, slot.Remaining > 0f ? slot : default);
            }
        }

        /// <summary>Folds active buffs into stats. <paramref name="definitions"/> is indexed by kind.</summary>
        public EffectiveStats Evaluate<TDefs>(in TDefs definitions) where TDefs : struct, IBuffDefinitions
        {
            var stats = EffectiveStats.Neutral;
            for (int i = 0; i < Capacity; i++)
            {
                var slot = Get(i);
                if (slot.Kind == 0) continue;
                var def = definitions.Get(slot.Kind);
                switch (def.Stat)
                {
                    case BuffStat.SpeedMultiplier: stats.SpeedMultiplier *= def.Value; break;
                    case BuffStat.TurnMultiplier: stats.TurnMultiplier *= def.Value; break;
                    case BuffStat.MagnetRadius: stats.MagnetRadius = math.max(stats.MagnetRadius, def.Value); break;
                    case BuffStat.BoostCostMultiplier: stats.BoostCostMultiplier *= def.Value; break;
                    case BuffStat.Shield: stats.Shield = true; break;
                }
            }
            return stats;
        }
    }

    /// <summary>Lookup of buff definitions by kind, implemented by the game mode's baked config.</summary>
    public interface IBuffDefinitions
    {
        BuffDefinition Get(byte kind);
    }
}
