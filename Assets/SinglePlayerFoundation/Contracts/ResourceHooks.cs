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

namespace SPF.Contracts
{
    /// <summary>
    /// Marks a resource that jobs read or write (grids, queues, stores, maps). Systems must declare such
    /// resources: in editor / development builds, touching an undeclared one while a system schedules
    /// throws (see TickPipeline). Plain main-thread state (configuration, game flow) does not need it.
    /// </summary>
    public interface IJobData { }
}
