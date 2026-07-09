namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Disparar todas as chamadas async de uma vez pode sobrecarregar uma dependência
/// com limite de taxa. SemaphoreSlim limita quantas rodam ao mesmo tempo, ainda
/// sobrepondo-as.
/// </summary>
public sealed class SemaphoreThrottlingDemo : DemoBase
{
    private static readonly DemoParameter Jobs =
        new("jobs", "Total de jobs", 60, 10, 200, 10);
    private static readonly DemoParameter Limit =
        new("limit", "Concorrência máx. (padrão)", 8, 1, 32, 1,
            "A contagem de permissões do semáforo");

    public override DemoInfo Info { get; } = new(
        Id: "semaphore-throttling",
        Title: "Concorrência ilimitada vs throttling com SemaphoreSlim",
        Category: "Coordenação Async",
        Summary: "Limite o trabalho em voo para não derreter uma dependência downstream.",
        AntipatternCode:
            """
            // ❌ Task.WhenAll sobre tudo lança TODOS os jobs de uma vez.
            // 10.000 itens => 10.000 conexões/alocações simultâneas.
            // O serviço remoto retorna 429 ou você esgota os sockets.
            var tasks = items.Select(item => CallServiceAsync(item));
            await Task.WhenAll(tasks);   // sem teto de concorrência
            """,
        AntipatternExplanation:
            "`WhenAll` sobrepõe as chamadas — bom — mas sem teto. Contra uma API com limite de taxa " +
            "ou um recurso com pool de conexões, o fan-out ilimitado causa throttling, timeouts ou " +
            "esgotamento de sockets.",
        PatternCode:
            """
            // ✅ Um SemaphoreSlim limita quantas rodam ao mesmo tempo. Os jobs
            // ainda se sobrepõem até a contagem de permissões; o resto aguarda.
            using var gate = new SemaphoreSlim(maxConcurrency);
            var tasks = items.Select(async item =>
            {
                await gate.WaitAsync();
                try { return await CallServiceAsync(item); }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks);
            """,
        PatternExplanation:
            "`SemaphoreSlim(maxConcurrency)` distribui um número fixo de permissões. A concorrência " +
            "de pico nunca ultrapassa o limite, protegendo a dependência e mantendo boa vazão. " +
            "`Parallel.ForEachAsync` com `MaxDegreeOfParallelism` é uma alternativa de mais alto nível.",
        KeyTakeaways: new[]
        {
            "Sobreposição é bom; sobreposição ilimitada é um DoS autoinfligido.",
            "SemaphoreSlim (ou Parallel.ForEachAsync) limita o trabalho em voo.",
            "Sempre faça Release em um finally para uma falha não vazar uma permissão.",
        },
        SupportsRun: true,
        Parameters: new[] { Jobs, Limit })
    {
        Chapter = "Cap. 5 · Programação Assíncrona com C#", Since = ".NET 4.0",
        UseCases = new[]
        {
            "Limitar chamadas simultâneas a uma API com rate limit ou a um BD (pool de conexões).",
            "Baixar/processar milhares de arquivos sem abrir tudo de uma vez.",
            "Proteger um recurso caro (memória, CPU de GPU) de picos de concorrência.",
        },
    };

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int jobs = args.Get(Jobs);
        return await MeasureAsync("antipattern", async rec =>
        {
            var meter = new ConcurrencyMeter();
            var tasks = Enumerable.Range(0, jobs).Select(async _ =>
            {
                using (meter.Enter())
                    await Task.Delay(40, ct);
            });
            await Task.WhenAll(tasks);

            rec.Log($"Lançou {jobs} jobs sem teto de concorrência");
            rec.Log($"Pico de jobs simultâneos: {meter.Max}");
            return new VariantOutcome(
                Ok: false, // "funciona", mas martela a dependência
                Headline: $"Pico de {meter.Max} jobs simultâneos — sem back-pressure",
                Metrics: new[]
                {
                    new MetricItem("Jobs", jobs.ToString()),
                    new MetricItem("Pico de concorrência", meter.Max.ToString(), "Descontrolado"),
                    new MetricItem("Limite", "nenhum"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int jobs = args.Get(Jobs);
        int limit = args.Get(Limit);
        return await MeasureAsync("pattern", async rec =>
        {
            var meter = new ConcurrencyMeter();
            using var gate = new SemaphoreSlim(limit);
            var tasks = Enumerable.Range(0, jobs).Select(async _ =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    using (meter.Enter())
                        await Task.Delay(40, ct);
                }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks);

            rec.Log($"Rodou {jobs} jobs com um gate SemaphoreSlim({limit})");
            rec.Log($"Pico de jobs simultâneos: {meter.Max} (≤ {limit})");
            return new VariantOutcome(
                Ok: meter.Max <= limit,
                Headline: $"Limitado a {meter.Max} jobs simultâneos (limite {limit})",
                Metrics: new[]
                {
                    new MetricItem("Jobs", jobs.ToString()),
                    new MetricItem("Pico de concorrência", meter.Max.ToString(), "Limitado"),
                    new MetricItem("Limite", limit.ToString()),
                });
        });
    }
}
