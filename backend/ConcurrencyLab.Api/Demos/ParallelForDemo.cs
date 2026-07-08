namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Trabalho CPU-bound em um loop sequencial usa um núcleo. Parallel.For distribui
/// iterações independentes por todos os núcleos, com ganho quase linear.
/// </summary>
public sealed class ParallelForDemo : DemoBase
{
    private static readonly DemoParameter Limit =
        new("limit", "Contar primos até", 800_000, 100_000, 3_000_000, 100_000,
            "Maior = mais trabalho de CPU");

    public override DemoInfo Info { get; } = new(
        Id: "parallel-for-cpu-bound",
        Title: "Loop sequencial vs Parallel.For (CPU-bound)",
        Category: "Paralelismo de Dados",
        Summary: "Iterações CPU-bound independentes devem rodar em todos os núcleos, não em um só.",
        AntipatternCode:
            """
            // ❌ Um loop sequencial prende o trabalho CPU-bound a um único núcleo.
            // Em uma máquina de 8 núcleos, você deixa ~87% da CPU ociosa.
            int count = 0;
            for (int n = 2; n <= limit; n++)
            {
                if (IsPrime(n))
                    count++;
            }
            """,
        AntipatternExplanation:
            "As iterações são independentes e CPU-bound — o caso ideal para paralelismo de dados — " +
            "mas um loop `for` as executa uma após a outra em uma única thread.",
        PatternCode:
            """
            // ✅ Parallel.For particiona o intervalo pelo thread pool.
            // Cada thread mantém um subtotal local (sem contenção no contador
            // compartilhado) e faz o merge uma vez no final.
            int count = 0;
            Parallel.For(2, limit + 1,
                () => 0,                              // local por thread
                (n, _, local) => IsPrime(n) ? local + 1 : local,
                local => Interlocked.Add(ref count, local)); // merge
            """,
        PatternExplanation:
            "`Parallel.For` com um acumulador local por thread evita compartilhar o contador no " +
            "caminho quente e só sincroniza uma vez por partição. O ganho escala com a contagem de " +
            "núcleos para trabalho CPU-bound. (Para trabalho I/O-bound, use async, não Parallel.For.)",
        KeyTakeaways: new[]
        {
            "Paralelize iterações CPU-bound e independentes pelos núcleos.",
            "Use estado local por thread + merge final para evitar contenção por iteração.",
            "Parallel.For é para trabalho de CPU; use async/await para trabalho de I/O.",
        },
        SupportsRun: true,
        Parameters: new[] { Limit })
    { Chapter = "Cap. 6 · Conceitos de Programação Paralela" };

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int limit = args.Get(Limit);
        return MeasureAsync("antipattern", rec =>
        {
            int count = 0;
            for (int n = 2; n <= limit; n++)
                if (Workloads.IsPrime(n)) count++;

            rec.Log($"Contou {count:N0} primos até {limit:N0} em 1 thread");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"{count:N0} primos encontrados usando um único núcleo",
                Metrics: new[]
                {
                    new MetricItem("Primos", count.ToString("N0")),
                    new MetricItem("Núcleos usados", "1"),
                    new MetricItem("Núcleos da máquina", Environment.ProcessorCount.ToString()),
                }));
        });
    }

    public override Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int limit = args.Get(Limit);
        return MeasureAsync("pattern", rec =>
        {
            int count = 0;
            Parallel.For(2, limit + 1,
                new ParallelOptions { CancellationToken = ct },
                () => 0,
                (n, _, local) => Workloads.IsPrime(n) ? local + 1 : local,
                local => Interlocked.Add(ref count, local));

            rec.Log($"Contou {count:N0} primos até {limit:N0} em {Environment.ProcessorCount} núcleos");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"{count:N0} primos encontrados usando todos os {Environment.ProcessorCount} núcleos",
                Metrics: new[]
                {
                    new MetricItem("Primos", count.ToString("N0")),
                    new MetricItem("Núcleos usados", Environment.ProcessorCount.ToString()),
                    new MetricItem("Núcleos da máquina", Environment.ProcessorCount.ToString()),
                }));
        });
    }
}
