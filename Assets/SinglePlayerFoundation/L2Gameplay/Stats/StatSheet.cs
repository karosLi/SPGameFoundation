using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SPF.L2.Stats
{
    /// <summary>
    /// Twelve game-defined stats stored inline (three float4): the game assigns meanings to indices
    /// (e.g. 0 = max health, 1 = attack, ...). Burst friendly, no allocation.
    /// </summary>
    public struct StatBlock
    {
        public const int Count = 12;
        public float4 A, B, C;

        public float this[int stat]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => stat < 4 ? A[stat] : stat < 8 ? B[stat - 4] : C[stat - 8];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { if (stat < 4) A[stat] = value; else if (stat < 8) B[stat - 4] = value; else C[stat - 8] = value; }
        }

        public static StatBlock operator +(StatBlock x, StatBlock y) => new StatBlock { A = x.A + y.A, B = x.B + y.B, C = x.C + y.C };
    }

    public enum ModifierOp : byte
    {
        /// <summary>Added to the base value.</summary>
        Add = 0,
        /// <summary>Multiplies (base + adds): 0.2 = +20%. Multipliers of one stat add up before applying.</summary>
        Multiply = 1,
    }

    public struct Modifier
    {
        public byte Source;       // 0 = empty slot; game-defined (buff kind, item slot, ...)
        public byte Stat;
        public ModifierOp Op;
        public float Value;
        public float Remaining;   // seconds; negative = permanent until removed
    }

    /// <summary>
    /// Fixed eight-slot set of stat modifiers stored inline in an entity column: timed buffs / debuffs and
    /// permanent ones (equipment). Applying the same (source, stat) refreshes it; a full set replaces the
    /// timed slot closest to expiring. The game-agnostic successor of the snake's BuffSet.
    /// </summary>
    public struct ModifierSet
    {
        public const int Capacity = 8;
        public Modifier M0, M1, M2, M3, M4, M5, M6, M7;

        public Modifier Get(int i) => i switch { 0 => M0, 1 => M1, 2 => M2, 3 => M3, 4 => M4, 5 => M5, 6 => M6, _ => M7 };

        public void Set(int i, in Modifier m)
        {
            switch (i)
            {
                case 0: M0 = m; break;
                case 1: M1 = m; break;
                case 2: M2 = m; break;
                case 3: M3 = m; break;
                case 4: M4 = m; break;
                case 5: M5 = m; break;
                case 6: M6 = m; break;
                default: M7 = m; break;
            }
        }

        /// <summary>Adds or refreshes a modifier; returns false when every slot holds a permanent one.</summary>
        public bool Apply(in Modifier modifier)
        {
            int target = -1;
            float lowest = float.MaxValue;
            for (int i = 0; i < Capacity; i++)
            {
                var m = Get(i);
                if (m.Source == modifier.Source && m.Stat == modifier.Stat && m.Op == modifier.Op) { target = i; break; }
                if (m.Source == 0) { if (lowest > -1f) { lowest = -1f; target = i; } continue; }
                if (m.Remaining >= 0f && m.Remaining < lowest) { lowest = m.Remaining; target = i; }
            }
            if (target < 0) return false;
            Set(target, modifier);
            return true;
        }

        public void RemoveSource(byte source)
        {
            for (int i = 0; i < Capacity; i++)
                if (Get(i).Source == source) Set(i, default);
        }

        public bool HasSource(byte source)
        {
            for (int i = 0; i < Capacity; i++)
                if (Get(i).Source == source) return true;
            return false;
        }

        /// <summary>Counts timed modifiers down and clears expired ones.</summary>
        public void Tick(float deltaTime)
        {
            for (int i = 0; i < Capacity; i++)
            {
                var m = Get(i);
                if (m.Source == 0 || m.Remaining < 0f) continue;
                m.Remaining -= deltaTime;
                Set(i, m.Remaining > 0f ? m : default);
            }
        }

        /// <summary>(base + adds) × (1 + sum of multipliers), per stat.</summary>
        public StatBlock Evaluate(in StatBlock baseStats)
        {
            var add = default(StatBlock);
            var mul = default(StatBlock);
            for (int i = 0; i < Capacity; i++)
            {
                var m = Get(i);
                if (m.Source == 0) continue;
                if (m.Op == ModifierOp.Add) add[m.Stat] += m.Value;
                else mul[m.Stat] += m.Value;
            }
            return new StatBlock
            {
                A = (baseStats.A + add.A) * (1f + mul.A),
                B = (baseStats.B + add.B) * (1f + mul.B),
                C = (baseStats.C + add.C) * (1f + mul.C),
            };
        }
    }
}
