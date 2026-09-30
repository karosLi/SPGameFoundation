namespace SPF.Contracts
{
    /// <summary>
    /// Fixed order of work inside one simulation tick. Systems register into a phase and are
    /// ordered by their Order value inside it. See Docs/Architecture.md §2.3.
    /// </summary>
    public enum SimPhase : byte
    {
        /// <summary>Main thread, all jobs of the previous tick are complete. Structural changes happen here.</summary>
        ApplyCommands = 0,
        Input = 1,
        Decide = 2,
        Move = 3,
        Body = 4,
        SpatialBuild = 5,
        Collision = 6,
        Resolve = 7,
        Spawn = 8,
        Snapshot = 9,
    }

    public static class SimPhases
    {
        public const int Count = 10;
    }
}
