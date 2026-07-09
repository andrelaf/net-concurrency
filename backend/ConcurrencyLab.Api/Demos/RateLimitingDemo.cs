using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Um <c>SemaphoreSlim</c> limita a <em>concorrência</em>, não a <em>taxa</em>: se
/// cada operação é rápida, você dispara muito mais que X por segundo (rajadas). O
/// <c>System.Threading.RateLimiting</c> (.NET 7) impõe uma taxa real (ex.: token
/// bucket) com fila.
/// </summary>
public sealed class RateLimitingDemo : DemoBase
{
    private static readonly DemoParameter Requests =
        new("requests", "Requisições", 200, 40, 1000, 20);
    private static readonly DemoParameter PerSecond =
        new("perSecond", "Limite (req/s)", 50, 10, 200, 10);

    public override DemoInfo Info { get; } = new(
        Id: "semaphore-vs-ratelimiting",
        Title: "SemaphoreSlim (concorrência) vs RateLimiting (taxa)",
        Category: "Pipelines e Padrões",
        Summary: "SemaphoreSlim limita quantos rodam juntos; RateLimiter limita quantos por segundo.",
        AntipatternCode:
            """
            // ❌ SemaphoreSlim limita a CONCORRÊNCIA, não a taxa. Se cada
            // chamada dura microssegundos, você faz milhares por segundo com
            // um limite de 8 "simultâneas" — rajadas que estouram o rate limit
            // do serviço remoto.
            using var gate = new SemaphoreSlim(8);
            await gate.WaitAsync();
            try { await CallApiAsync(); }   // rápido -> rajada de req/s
            finally { gate.Release(); }
            """,
        AntipatternExplanation:
            "Concorrência e taxa são coisas diferentes. Um semáforo garante \"no máximo N ao mesmo " +
            "tempo\", mas se cada operação termina rápido, o número de operações *por segundo* dispara. " +
            "Contra uma API com limite de req/s, isso vira 429.",
        PatternCode:
            """
            // ✅ RateLimiter impõe uma taxa real. TokenBucket repõe X tokens por
            // período; sem token, a requisição espera na fila. A vazão fica
            // limitada a X/s independentemente de quão rápida é cada chamada.
            using var limiter = new TokenBucketRateLimiter(new()
            {
                TokenLimit = perSecond,
                TokensPerPeriod = perSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = int.MaxValue,
                AutoReplenishment = true,
            });

            using RateLimitLease lease = await limiter.AcquireAsync(1, ct);
            await CallApiAsync();
            """,
        PatternExplanation:
            "`System.Threading.RateLimiting` traz limitadores prontos (token bucket, fixed/sliding " +
            "window, concurrency) com política de fila. O token bucket repõe X permissões por período, " +
            "então a taxa sustentada fica em X/s com rajada controlada pelo tamanho do balde — o que um " +
            "semáforo de concorrência não faz.",
        KeyTakeaways: new[]
        {
            "Concorrência ≠ taxa: SemaphoreSlim limita simultâneos, não req/s.",
            "System.Threading.RateLimiting (.NET 7) impõe taxa real com fila.",
            "Token bucket: X tokens/período; rajada limitada pelo tamanho do balde.",
        },
        SupportsRun: true,
        Parameters: new[] { Requests, PerSecond })
    { Chapter = null, Since = ".NET 7" };

    // Mede o pico de requisições iniciadas em qualquer janela de 1s (bins de 100ms).
    private static int PeakPerSecond(ConcurrentBag<long> startsMs)
    {
        var bins = new Dictionary<long, int>();
        foreach (var t in startsMs)
        {
            long bin = t / 100;
            bins[bin] = bins.GetValueOrDefault(bin) + 1;
        }
        if (bins.Count == 0) return 0;
        // soma de 10 bins consecutivos (=1s) máxima
        long min = bins.Keys.Min(), max = bins.Keys.Max();
        int peak = 0;
        for (long b = min; b <= max; b++)
        {
            int windowSum = 0;
            for (long k = b; k < b + 10; k++) windowSum += bins.GetValueOrDefault(k);
            if (windowSum > peak) peak = windowSum;
        }
        return peak;
    }

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int requests = args.Get(Requests);
        return await MeasureAsync("antipattern", async rec =>
        {
            var starts = new ConcurrentBag<long>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var gate = new SemaphoreSlim(8);
            await Task.WhenAll(Enumerable.Range(0, requests).Select(async _ =>
            {
                await gate.WaitAsync(ct);
                try { starts.Add(sw.ElapsedMilliseconds); await Task.Delay(1, ct); }
                finally { gate.Release(); }
            }));
            int peak = PeakPerSecond(starts);
            rec.Log($"{requests} req com SemaphoreSlim(8); pico observado ~{peak} req/s");
            return new VariantOutcome(
                Ok: false,
                Headline: $"Pico de ~{peak} req/s — o semáforo não conteve a taxa (rajada)",
                Metrics: new[]
                {
                    new MetricItem("Requisições", requests.ToString()),
                    new MetricItem("Pico req/s", $"~{peak}", "Descontrolado"),
                    new MetricItem("Controle", "SemaphoreSlim (concorrência)"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int requests = args.Get(Requests);
        int perSecond = args.Get(PerSecond);
        return await MeasureAsync("pattern", async rec =>
        {
            var starts = new ConcurrentBag<long>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = perSecond,
                TokensPerPeriod = perSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = int.MaxValue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
            await Task.WhenAll(Enumerable.Range(0, requests).Select(async _ =>
            {
                using RateLimitLease lease = await limiter.AcquireAsync(1, ct);
                starts.Add(sw.ElapsedMilliseconds);
                await Task.Delay(1, ct);
            }));
            int peak = PeakPerSecond(starts);
            rec.Log($"{requests} req com TokenBucket({perSecond}/s); pico observado ~{peak} req/s");
            return new VariantOutcome(
                Ok: peak <= perSecond * 2,
                Headline: $"Pico de ~{peak} req/s — mantido perto do limite de {perSecond}/s",
                Metrics: new[]
                {
                    new MetricItem("Requisições", requests.ToString()),
                    new MetricItem("Pico req/s", $"~{peak}", $"Limite {perSecond}/s"),
                    new MetricItem("Controle", "TokenBucketRateLimiter"),
                });
        });
    }
}
