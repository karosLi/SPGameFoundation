using System;
using Unity.Collections;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace SPF.L2.Progression
{
    /// <summary>Experience curve: level n → n+1 costs Base × Growth^(n-1) (rounded).</summary>
    public struct LevelCurve
    {
        public float Base;
        public float Growth;
        public int MaxLevel;

        public int XpToNext(int level) => (int)math.round(Base * math.pow(Growth, math.max(level - 1, 0)));

        /// <summary>Applies gained XP: returns levels gained; <paramref name="level"/> / <paramref name="xp"/> are updated.</summary>
        public int AddXp(ref int level, ref int xp, int gained)
        {
            xp += math.max(gained, 0);
            int levels = 0;
            while (level < MaxLevel && xp >= XpToNext(level))
            {
                xp -= XpToNext(level);
                level++;
                levels++;
            }
            if (level >= MaxLevel) xp = 0;
            return levels;
        }
    }

    public struct LootEntry
    {
        public int Item;        // game-defined; negative = nothing
        public float Weight;
        public int MinCount;
        public int MaxCount;
    }

    /// <summary>Weighted loot rolls. Deterministic for a given random state; entries are baked from config.</summary>
    public static class LootTable
    {
        /// <summary>Index of the chosen entry (−1 when the table is empty or all weights are zero).</summary>
        public static int Pick(ReadOnlySpan<LootEntry> entries, ref Random random)
        {
            float total = 0f;
            for (int i = 0; i < entries.Length; i++) total += math.max(entries[i].Weight, 0f);
            if (total <= 0f) return -1;
            float r = random.NextFloat(total);
            for (int i = 0; i < entries.Length; i++)
            {
                r -= math.max(entries[i].Weight, 0f);
                if (r < 0f) return i;
            }
            return entries.Length - 1;
        }

        public static int Pick(NativeArray<LootEntry> entries, ref Random random)
        {
            float total = 0f;
            for (int i = 0; i < entries.Length; i++) total += math.max(entries[i].Weight, 0f);
            if (total <= 0f) return -1;
            float r = random.NextFloat(total);
            for (int i = 0; i < entries.Length; i++)
            {
                r -= math.max(entries[i].Weight, 0f);
                if (r < 0f) return i;
            }
            return entries.Length - 1;
        }

        public static int Count(in LootEntry entry, ref Random random) =>
            entry.MaxCount > entry.MinCount ? random.NextInt(entry.MinCount, entry.MaxCount + 1) : entry.MinCount;
    }
}
