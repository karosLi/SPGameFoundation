namespace SPF.Contracts
{
    /// <summary>Resource hook called at the end of every tick, after all jobs completed (main thread).</summary>
    public interface ISyncResource
    {
        void OnSync();
    }

    /// <summary>Resource hook called when a session restarts; return to the freshly-created state without reallocating.</summary>
    public interface IResettableResource
    {
        void OnReset();
    }
}
