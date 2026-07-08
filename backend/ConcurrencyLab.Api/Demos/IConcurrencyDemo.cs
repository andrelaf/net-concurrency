using System.Diagnostics;

namespace ConcurrencyLab.Api.Demos;

/// <summary>Contract every demo implements: static info + two runnable variants.</summary>
public interface IConcurrencyDemo
{
    DemoInfo Info { get; }
    Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct);
    Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct);
    string BuildVerdict(RunVariant? antipattern, RunVariant? pattern);
}

/// <summary>Base class handling timing, log capture and a sensible default verdict.</summary>
public abstract class DemoBase : IConcurrencyDemo
{
    public abstract DemoInfo Info { get; }

    public virtual Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct) =>
        throw new NotSupportedException($"A demo '{Info.Id}' é apenas ilustrativa.");

    public virtual Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct) =>
        throw new NotSupportedException($"A demo '{Info.Id}' é apenas ilustrativa.");

    public virtual string BuildVerdict(RunVariant? antipattern, RunVariant? pattern)
    {
        if (antipattern is null || pattern is null) return string.Empty;
        if (!antipattern.Ok && pattern.Ok)
            return "O antipadrão produziu um resultado incorreto ou inseguro; o padrão está correto.";
        if (antipattern.Ok && pattern.Ok && antipattern.ElapsedMs > pattern.ElapsedMs)
        {
            var speedup = antipattern.ElapsedMs / Math.Max(1.0, pattern.ElapsedMs);
            return $"Ambos estão corretos, mas o padrão rodou ~{speedup:0.0}× mais rápido.";
        }
        return "Compare as métricas abaixo para ver a diferença.";
    }

    /// <summary>Runs a variant body while measuring elapsed time and capturing logs.</summary>
    protected static async Task<RunVariant> MeasureAsync(
        string kind,
        Func<Recorder, Task<VariantOutcome>> body)
    {
        var rec = new Recorder();
        var sw = Stopwatch.StartNew();
        VariantOutcome outcome;
        try
        {
            outcome = await body(rec);
        }
        catch (Exception ex)
        {
            sw.Stop();
            rec.Log($"Exceção não tratada: {ex.GetType().Name}: {ex.Message}");
            return new RunVariant(kind, false, sw.ElapsedMilliseconds,
                $"Falhou com {ex.GetType().Name}", Array.Empty<MetricItem>(), rec.Lines);
        }
        sw.Stop();
        return new RunVariant(kind, outcome.Ok, sw.ElapsedMilliseconds,
            outcome.Headline, outcome.Metrics, rec.Lines);
    }
}

/// <summary>Domain result a variant body returns; timing/logging is added by the base.</summary>
public sealed record VariantOutcome(
    bool Ok,
    string Headline,
    IReadOnlyList<MetricItem> Metrics);
