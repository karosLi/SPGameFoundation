using Unity.Mathematics;

namespace SPF.L2.Progression
{
    /// <summary>One group of a wave: <see cref="Count"/> units of <see cref="Kind"/> from <see cref="Start"/>, every <see cref="Interval"/> seconds.</summary>
    [System.Serializable]
    public struct WaveGroup
    {
        public float Start;
        public int Kind;
        public int Count;
        public float Interval;

        public float End => Start + math.max(Count - 1, 0) * Interval;
    }

    /// <summary>
    /// Timed spawns (tower defense, horde modes, scripted encounters): how many units of a group fall due in
    /// a time window. Pure and stateless, so it is deterministic at any tick rate and survives snapshots
    /// (only the elapsed time is state).
    /// </summary>
    public static class WaveSchedule
    {
        /// <summary>Spawns of <paramref name="group"/> at times in (from, to].</summary>
        public static int Due(in WaveGroup group, float from, float to)
        {
            if (group.Count <= 0 || to <= from) return 0;
            return Spawned(group, to) - Spawned(group, from);
        }

        /// <summary>Spawns of <paramref name="group"/> at times ≤ <paramref name="time"/>.</summary>
        public static int Spawned(in WaveGroup group, float time)
        {
            if (time < group.Start) return 0;
            if (group.Interval <= 0f) return group.Count;
            return math.min(group.Count, (int)math.floor((time - group.Start) / group.Interval + 1e-4f) + 1);
        }
    }
}
