namespace SPF.Contracts
{
    /// <summary>Time of the tick being simulated. DeltaTime is always the fixed step.</summary>
    public readonly struct TickTime
    {
        public readonly uint Tick;
        public readonly float DeltaTime;
        public readonly double ElapsedTime;

        public TickTime(uint tick, float deltaTime, double elapsedTime)
        {
            Tick = tick;
            DeltaTime = deltaTime;
            ElapsedTime = elapsedTime;
        }
    }
}
