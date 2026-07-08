using System.Collections.Concurrent;
using System.Diagnostics;

namespace ConcurrencyLab.Api.Demos;

/// <summary>A tunable numeric input that the UI renders as a slider.</summary>
public sealed record DemoParameter(
    string Name,
    string Label,
    int Default,
    int Min,
    int Max,
    int Step = 1,
    string? Hint = null);

/// <summary>Static metadata + the pattern/antipattern source shown in the UI.</summary>
public sealed record DemoInfo(
    string Id,
    string Title,
    string Category,
    string Summary,
    string AntipatternCode,
    string AntipatternExplanation,
    string PatternCode,
    string PatternExplanation,
    IReadOnlyList<string> KeyTakeaways,
    bool SupportsRun,
    IReadOnlyList<DemoParameter> Parameters)
{
    /// <summary>
    /// Reference chapter in the companion book, "Parallel Programming and
    /// Concurrency with C# 10 and .NET 6" (Ashcraft). The demos modernize those
    /// concepts to .NET 10.
    /// </summary>
    public string? Chapter { get; init; }
}

/// <summary>A single key metric surfaced after a run (e.g. "Lost updates: 4213").</summary>
public sealed record MetricItem(string Label, string Value, string? Hint = null);

/// <summary>The outcome of running one variant (antipattern or pattern).</summary>
public sealed record RunVariant(
    string Kind,
    bool Ok,
    long ElapsedMs,
    string Headline,
    IReadOnlyList<MetricItem> Metrics,
    IReadOnlyList<string> Log);

/// <summary>The response returned by POST /api/demos/{id}/run.</summary>
public sealed record RunResponse(
    string DemoId,
    RunVariant? Antipattern,
    RunVariant? Pattern,
    string Verdict);

/// <summary>Parsed, clamped run arguments coming from the client.</summary>
public sealed class RunArgs
{
    private readonly IReadOnlyDictionary<string, int> _values;
    public RunArgs(IReadOnlyDictionary<string, int>? values) =>
        _values = values ?? new Dictionary<string, int>();

    public int Get(DemoParameter p) =>
        _values.TryGetValue(p.Name, out var v) ? Math.Clamp(v, p.Min, p.Max) : p.Default;
}

/// <summary>Thread-safe log/metric collector passed into each variant body.</summary>
public sealed class Recorder
{
    private readonly ConcurrentQueue<string> _lines = new();
    public void Log(string line) => _lines.Enqueue(line);
    public IReadOnlyList<string> Lines => _lines.ToArray();
}

/// <summary>Tracks the maximum number of concurrently-executing sections.</summary>
public sealed class ConcurrencyMeter
{
    private int _current;
    private int _max;

    public IDisposable Enter()
    {
        var now = Interlocked.Increment(ref _current);
        int observed;
        while (now > (observed = Volatile.Read(ref _max)))
            Interlocked.CompareExchange(ref _max, now, observed);
        return new Scope(this);
    }

    public int Max => Volatile.Read(ref _max);

    private sealed class Scope(ConcurrencyMeter meter) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref meter._current);
    }
}

/// <summary>Small CPU-bound kernels so parallel demos show real speedups.</summary>
public static class Workloads
{
    /// <summary>Burns CPU deterministically; returns a value so the JIT cannot elide it.</summary>
    public static double Spin(int iterations)
    {
        double acc = 0;
        for (int i = 1; i <= iterations; i++)
            acc += Math.Sqrt(i) * Math.Sin(i);
        return acc;
    }

    /// <summary>Counts primes in [2, limit] the naive way — genuinely CPU-bound.</summary>
    public static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n % 2 == 0) return n == 2;
        for (int d = 3; (long)d * d <= n; d += 2)
            if (n % d == 0) return false;
        return true;
    }
}
