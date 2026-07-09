namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Uma query LINQ sobre trabalho CPU-bound roda em uma thread. AsParallel() deixa
/// o PLINQ particionar a sequência pelos núcleos — mesma forma de query, mais vazão.
/// </summary>
public sealed class PlinqDemo : DemoBase
{
    private static readonly DemoParameter Size =
        new("size", "Números a pontuar", 2_000_000, 200_000, 8_000_000, 200_000,
            "Cada elemento roda um pequeno kernel de CPU");

    public override DemoInfo Info { get; } = new(
        Id: "plinq-vs-linq",
        Title: "LINQ vs PLINQ (AsParallel)",
        Category: "Paralelismo de Dados",
        Summary: "Um pipeline LINQ CPU-bound pode ir para paralelo com um único AsParallel().",
        AntipatternCode:
            """
            // ❌ LINQ sequencial: o pipeline inteiro roda na thread chamadora.
            double total = Enumerable.Range(0, size)
                .Select(Score)          // Score() é CPU-bound
                .Where(s => s > 0)
                .Sum();
            """,
        AntipatternExplanation:
            "LINQ-to-Objects é preguiçoso, mas estritamente sequencial. Quando cada elemento exige " +
            "trabalho de CPU real, o pipeline fica gargalado em um único núcleo.",
        PatternCode:
            """
            // ✅ PLINQ particiona a fonte e roda estágios pelos núcleos,
            // depois faz o merge dos resultados. A query fica quase idêntica.
            double total = Enumerable.Range(0, size)
                .AsParallel()
                .Select(Score)
                .Where(s => s > 0)
                .Sum();
            """,
        PatternExplanation:
            "`AsParallel()` transforma a query em uma query PLINQ que fatia a fonte e executa os " +
            "operadores concorrentemente. Brilha para trabalho CPU-bound e sem efeitos colaterais. " +
            "Cuidado: para trabalho por elemento muito barato, o overhead de particionamento pode " +
            "deixar o PLINQ mais lento.",
        KeyTakeaways: new[]
        {
            "PLINQ paraleliza queries CPU-bound e sem efeitos colaterais com um único operador.",
            "Mantenha os seletores puros — ordem e efeitos colaterais não são garantidos.",
            "Meça: para trabalho trivial por elemento, o LINQ sequencial pode vencer.",
        },
        SupportsRun: true,
        Parameters: new[] { Size })
    {
        Chapter = "Cap. 8 · Estruturas de Dados Paralelas e PLINQ", Since = ".NET 4.0",
        UseCases = new[]
        {
            "Agregações/filtragens CPU-bound sobre grandes coleções em memória.",
            "Análise de dados (scoring, parsing, cálculo) paralela e sem efeitos colaterais.",
            "Transformar uma query LINQ pesada em paralela com mudança mínima de código.",
        },
    };

    // Kernel de CPU determinístico por elemento.
    private static double Score(int i) => Workloads.Spin(60) + (i % 7);

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int size = args.Get(Size);
        return MeasureAsync("antipattern", rec =>
        {
            double total = Enumerable.Range(0, size).Select(Score).Where(s => s > 0).Sum();
            rec.Log($"Pontuou {size:N0} elementos sequencialmente");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"Processou {size:N0} elementos em uma única thread",
                Metrics: new[]
                {
                    new MetricItem("Elementos", size.ToString("N0")),
                    new MetricItem("Modo", "LINQ sequencial"),
                    new MetricItem("Núcleos usados", "1"),
                }));
        });
    }

    public override Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int size = args.Get(Size);
        return MeasureAsync("pattern", rec =>
        {
            double total = Enumerable.Range(0, size).AsParallel().WithCancellation(ct)
                .Select(Score).Where(s => s > 0).Sum();
            rec.Log($"Pontuou {size:N0} elementos em {Environment.ProcessorCount} núcleos");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"Processou {size:N0} elementos em {Environment.ProcessorCount} núcleos",
                Metrics: new[]
                {
                    new MetricItem("Elementos", size.ToString("N0")),
                    new MetricItem("Modo", "PLINQ (AsParallel)"),
                    new MetricItem("Núcleos usados", Environment.ProcessorCount.ToString()),
                }));
        });
    }
}
