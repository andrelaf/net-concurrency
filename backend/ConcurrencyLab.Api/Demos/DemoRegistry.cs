namespace ConcurrencyLab.Api.Demos;

/// <summary>Central catalog of all demos, indexed by id and preserving order.</summary>
public sealed class DemoRegistry
{
    private readonly IReadOnlyList<IConcurrencyDemo> _demos;
    private readonly IReadOnlyDictionary<string, IConcurrencyDemo> _byId;

    public DemoRegistry()
    {
        _demos = new IConcurrencyDemo[]
        {
            // Fundamentals
            new ThreadVsTaskDemo(),
            new StartNewVsRunDemo(),
            new LockTypeDemo(),
            // Async Coordination
            new WhenAllDemo(),
            new WhenEachDemo(),
            new SemaphoreThrottlingDemo(),
            new WaitAsyncTimeoutDemo(),
            new PeriodicTimerDemo(),
            new CancellationDemo(),
            // Data Parallelism
            new ParallelForDemo(),
            new PlinqDemo(),
            new FalseSharingDemo(),
            // Collections & Messaging
            new ConcurrentDictionaryDemo(),
            new ChannelsDemo(),
            new DataflowDemo(),
            // Hazards
            new RaceConditionCounterDemo(),
            new DeadlockDemo(),
            new SyncOverAsyncDemo(),
            new AsyncVoidDemo(),
        };
        _byId = _demos.ToDictionary(d => d.Info.Id);
    }

    public IReadOnlyList<DemoInfo> List() => _demos.Select(d => d.Info).ToList();

    public IConcurrencyDemo? Find(string id) =>
        _byId.TryGetValue(id, out var demo) ? demo : null;

    /// <summary>Distinct categories in first-seen order (for UI grouping).</summary>
    public IReadOnlyList<string> Categories() =>
        _demos.Select(d => d.Info.Category).Distinct().ToList();
}
