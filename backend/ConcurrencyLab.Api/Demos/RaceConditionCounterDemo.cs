namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// A condição de corrida clássica: várias threads fazendo <c>count++</c> em um
/// campo compartilhado perdem atualizações, porque ++ é ler-modificar-escrever,
/// não atômico. Interlocked resolve.
/// </summary>
public sealed class RaceConditionCounterDemo : DemoBase
{
    private static readonly DemoParameter Workers =
        new("workers", "Workers concorrentes", 8, 2, 32, 1, "Tasks incrementando em paralelo");
    private static readonly DemoParameter PerWorker =
        new("perWorker", "Incrementos por worker", 100_000, 10_000, 1_000_000, 10_000);

    public override DemoInfo Info { get; } = new(
        Id: "race-condition-counter",
        Title: "Condição de corrida em um contador compartilhado",
        Category: "Riscos",
        Summary: "count++ entre threads perde atualizações silenciosamente porque não é atômico.",
        AntipatternCode:
            """
            // ❌ '_count++' compila para: ler _count, somar 1, escrever _count.
            // Duas threads podem ler o mesmo valor e ambas escrevê-lo de volta,
            // então um incremento é perdido. Sem exceção — apenas um total errado.
            private long _count;

            void Increment() => _count++;

            await Parallel.ForEachAsync(workers, async (w, _) =>
            {
                for (int i = 0; i < perWorker; i++)
                    _count++;          // atualizações perdidas sob contenção
            });
            """,
        AntipatternExplanation:
            "`_count++` são três operações. Sem sincronização, os entrelaçamentos causam " +
            "atualizações perdidas, então o total final fica menor que workers × incrementos. O bug " +
            "é não determinístico: pode parecer OK sob baixa contenção e falhar sob carga.",
        PatternCode:
            """
            // ✅ Interlocked.Increment faz um ler-modificar-escrever atômico
            // no nível da CPU (LOCK XADD). Sem objeto de lock, sem perdas.
            private long _count;

            void Increment() => Interlocked.Increment(ref _count);

            await Parallel.ForEachAsync(workers, async (w, _) =>
            {
                for (int i = 0; i < perWorker; i++)
                    Interlocked.Increment(ref _count);
            });
            """,
        PatternExplanation:
            "`Interlocked.Increment` é uma única instrução atômica. É bem mais barato que um " +
            "`lock` para um contador único e garante que todo incremento seja observado.",
        KeyTakeaways: new[]
        {
            "`x++` em um campo compartilhado nunca é thread-safe.",
            "Prefira `Interlocked` para contadores/flags simples; use `lock` para invariantes de vários campos.",
            "Bugs de corrida são não determinísticos — 'funcionou na minha máquina' não prova nada.",
        },
        SupportsRun: true,
        Parameters: new[] { Workers, PerWorker })
    { Chapter = "Cap. 3 · Boas Práticas de Managed Threading", Since = ".NET 1.1" };

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        int perWorker = args.Get(PerWorker);
        long expected = (long)workers * perWorker;

        return await MeasureAsync("antipattern", async rec =>
        {
            long count = 0;
            await Parallel.ForEachAsync(Enumerable.Range(0, workers), ct, (w, _) =>
            {
                for (int i = 0; i < perWorker; i++)
                    count++; // deliberadamente sem sincronização
                return ValueTask.CompletedTask;
            });

            long lost = expected - count;
            rec.Log($"Total esperado: {expected:N0}");
            rec.Log($"Total real:     {count:N0}");
            rec.Log($"Perdidos:       {lost:N0} ({(double)lost / expected:P1})");
            return new VariantOutcome(
                Ok: count == expected,
                Headline: lost == 0
                    ? "Nenhuma atualização perdida desta vez (a corrida é não determinística — aumente os workers)"
                    : $"Perdeu {lost:N0} de {expected:N0} incrementos para a corrida",
                Metrics: new[]
                {
                    new MetricItem("Esperado", expected.ToString("N0")),
                    new MetricItem("Real", count.ToString("N0")),
                    new MetricItem("Perdidos", lost.ToString("N0"), "Perda de dados silenciosa"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        int perWorker = args.Get(PerWorker);
        long expected = (long)workers * perWorker;

        return await MeasureAsync("pattern", async rec =>
        {
            long count = 0;
            await Parallel.ForEachAsync(Enumerable.Range(0, workers), ct, (w, _) =>
            {
                for (int i = 0; i < perWorker; i++)
                    Interlocked.Increment(ref count);
                return ValueTask.CompletedTask;
            });

            rec.Log($"Total esperado: {expected:N0}");
            rec.Log($"Total real:     {count:N0}");
            rec.Log("Perdidos:       0 (incrementos atômicos)");
            return new VariantOutcome(
                Ok: count == expected,
                Headline: $"Todos os {expected:N0} incrementos registrados — zero perdidos",
                Metrics: new[]
                {
                    new MetricItem("Esperado", expected.ToString("N0")),
                    new MetricItem("Real", count.ToString("N0")),
                    new MetricItem("Perdidos", "0", "Atômico"),
                });
        });
    }
}
