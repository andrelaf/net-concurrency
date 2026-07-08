namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Cada thread incrementa seu próprio contador, mas se os contadores forem
/// adjacentes na memória eles caem na mesma cache line: os núcleos disputam a
/// posse da linha (false sharing) e o desempenho despenca. Espaçar cada contador
/// para sua própria cache line elimina a disputa.
/// </summary>
public sealed class FalseSharingDemo : DemoBase
{
    private static readonly DemoParameter Workers =
        new("workers", "Threads", 8, 2, 32, 1);
    private static readonly DemoParameter Iterations =
        new("iterations", "Incrementos por thread (milhões)", 10, 2, 50, 1,
            "Cada incremento toca a memória");

    public override DemoInfo Info { get; } = new(
        Id: "false-sharing",
        Title: "False sharing vs padding de cache line",
        Category: "Paralelismo de Dados",
        Summary: "Contadores por thread adjacentes na mesma cache line se sabotam; espaçá-los resolve.",
        AntipatternCode:
            """
            // ❌ Contadores por thread lado a lado num array de long (8 bytes).
            // Vários cabem na MESMA cache line (~64 bytes). Mesmo sem
            // compartilhar dados, escrever no seu contador invalida a cache
            // line dos vizinhos: os núcleos ficam trocando a posse da linha.
            long[] counters = new long[workers];
            Parallel.For(0, workers, w =>
            {
                for (long i = 0; i < iterations; i++)
                    counters[w]++;          // false sharing com os vizinhos
            });
            """,
        AntipatternExplanation:
            "Não há condição de corrida — cada thread escreve só na sua posição. Mas a coerência de " +
            "cache opera por linha (~64 bytes), então contadores adjacentes compartilham a linha. Cada " +
            "escrita invalida a cópia dos outros núcleos, gerando um ping-pong de cache que serializa o " +
            "que deveria ser paralelo.",
        PatternCode:
            """
            // ✅ Espace cada contador para sua própria cache line (padding).
            // Aqui, stride de 16 longs = 128 bytes garante que os contadores de
            // threads diferentes nunca dividam uma linha.
            const int stride = 16;               // 16 * 8 = 128 bytes
            long[] counters = new long[workers * stride];
            Parallel.For(0, workers, w =>
            {
                for (long i = 0; i < iterations; i++)
                    counters[w * stride]++;     // cache line exclusiva
            });
            // (ou um struct com [StructLayout(Size = 64)] por contador)
            """,
        PatternExplanation:
            "Ao dar padding para que cada contador ocupe sua própria cache line, as escritas de núcleos " +
            "diferentes deixam de invalidar umas às outras e o loop escala de verdade. A mesma ideia " +
            "vale para campos quentes de objetos diferentes tocados por threads distintas.",
        KeyTakeaways: new[]
        {
            "False sharing: dados não compartilhados na mesma cache line matam a escalabilidade.",
            "A coerência de cache opera por linha (~64 bytes), não por variável.",
            "Dê padding a contadores/campos quentes por thread (stride ou [StructLayout(Size=64)]).",
        },
        SupportsRun: true,
        Parameters: new[] { Workers, Iterations })
    { Chapter = "Cap. 6 · Conceitos de Programação Paralela" };

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        long iters = (long)args.Get(Iterations) * 1_000_000;
        return MeasureAsync("antipattern", rec =>
        {
            long[] counters = new long[workers]; // adjacentes -> false sharing
            Parallel.For(0, workers, new ParallelOptions { CancellationToken = ct }, w =>
            {
                for (long i = 0; i < iters; i++)
                    Volatile.Write(ref counters[w], Volatile.Read(ref counters[w]) + 1);
            });
            rec.Log($"{workers} threads × {iters:N0} incrementos com contadores adjacentes");
            rec.Log("Contadores na mesma cache line — ping-pong de coerência entre núcleos");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"{workers} threads disputando cache lines (false sharing)",
                Metrics: new[]
                {
                    new MetricItem("Threads", workers.ToString()),
                    new MetricItem("Layout", "adjacente", "Mesma cache line"),
                    new MetricItem("False sharing", "Sim"),
                }));
        });
    }

    public override Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        long iters = (long)args.Get(Iterations) * 1_000_000;
        return MeasureAsync("pattern", rec =>
        {
            const int stride = 16; // 128 bytes entre contadores
            long[] counters = new long[workers * stride];
            Parallel.For(0, workers, new ParallelOptions { CancellationToken = ct }, w =>
            {
                int idx = w * stride;
                for (long i = 0; i < iters; i++)
                    Volatile.Write(ref counters[idx], Volatile.Read(ref counters[idx]) + 1);
            });
            rec.Log($"{workers} threads × {iters:N0} incrementos com padding (stride {stride})");
            rec.Log("Cada contador em sua própria cache line — sem disputa de coerência");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"{workers} threads com padding de cache line — escala de verdade",
                Metrics: new[]
                {
                    new MetricItem("Threads", workers.ToString()),
                    new MetricItem("Layout", $"stride {stride}", "Cache line exclusiva"),
                    new MetricItem("False sharing", "Não"),
                }));
        });
    }
}
