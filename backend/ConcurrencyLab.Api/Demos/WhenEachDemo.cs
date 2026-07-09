namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Processar tasks conforme completam com um loop de <c>Task.WhenAny</c> é O(n²)
/// (cada WhenAny re-registra continuações em todas as tasks restantes). O
/// <c>Task.WhenEach</c> (.NET 9) faz isso em O(n) com <c>await foreach</c>.
/// </summary>
public sealed class WhenEachDemo : DemoBase
{
    private static readonly DemoParameter Tasks =
        new("tasks", "Número de tasks", 3000, 500, 5000, 100);
    private static readonly DemoParameter MaxDelayMs =
        new("maxDelayMs", "Atraso máx. por task (ms)", 8, 2, 50, 1);

    public override DemoInfo Info { get; } = new(
        Id: "whenany-loop-vs-wheneach",
        Title: "Loop de Task.WhenAny vs Task.WhenEach",
        Category: "Coordenação Async",
        Summary: "Consumir tasks conforme completam: o loop de WhenAny é O(n²); WhenEach é O(n).",
        AntipatternCode:
            """
            // ❌ Loop de WhenAny para processar na ordem de conclusão.
            // A cada iteração, WhenAny registra uma continuação em TODAS as
            // tasks pendentes, e Remove é O(n) -> custo total O(n²).
            var pending = new List<Task<int>>(tasks);
            while (pending.Count > 0)
            {
                Task<int> done = await Task.WhenAny(pending);
                pending.Remove(done);            // O(n)
                Process(await done);
            }
            """,
        AntipatternExplanation:
            "O idioma clássico de \"processar conforme chega\" com `Task.WhenAny` em loop é " +
            "quadrático: para n tasks são n iterações, e cada `WhenAny` anexa uma continuação a " +
            "cada task pendente. Com muitas tasks, esse overhead domina.",
        PatternCode:
            """
            // ✅ Task.WhenEach (.NET 9) entrega cada task conforme ela completa,
            // como um IAsyncEnumerable<Task<T>>. Linear e legível.
            await foreach (Task<int> completed in Task.WhenEach(tasks))
            {
                Process(await completed);        // já está concluída
            }
            """,
        PatternExplanation:
            "`Task.WhenEach` transmite as tasks na ordem de conclusão em O(n) total, sem re-registrar " +
            "continuações nem manter uma lista. É a forma moderna e recomendada de consumir resultados " +
            "à medida que ficam prontos.",
        KeyTakeaways: new[]
        {
            "Loop de Task.WhenAny para consumir conclusões é O(n²) — evite em coleções grandes.",
            "Task.WhenEach (.NET 9) entrega tasks conforme completam em O(n), via await foreach.",
            "Cada item do WhenEach já está concluído; o await só extrai o resultado/erro.",
        },
        SupportsRun: true,
        Parameters: new[] { Tasks, MaxDelayMs })
    {
        Chapter = "Cap. 5 · Programação Assíncrona com C#", Since = ".NET 9",
        UseCases = new[]
        {
            "Mostrar resultados conforme chegam (ex.: respostas de N provedores à medida que respondem).",
            "Processar conclusões de muitas tasks sem esperar a última (menor latência percebida).",
            "Atualizar UI/relatório incrementalmente enquanto o fan-out ainda roda.",
        },
    };

    // Cria tasks com atrasos variados e determinísticos.
    private static Task<int>[] Build(int n, int maxDelay, CancellationToken ct) =>
        Enumerable.Range(0, n)
            .Select(i => Task.Delay(1 + (i * 7) % maxDelay, ct).ContinueWith(_ => i, ct))
            .ToArray();

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int n = args.Get(Tasks);
        int maxDelay = args.Get(MaxDelayMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            var pending = new List<Task<int>>(Build(n, maxDelay, ct));
            int processed = 0;
            while (pending.Count > 0)
            {
                Task<int> done = await Task.WhenAny(pending);
                pending.Remove(done);
                await done;
                processed++;
            }
            rec.Log($"Processou {processed:N0} tasks com loop de WhenAny (O(n²))");
            return new VariantOutcome(
                Ok: true,
                Headline: $"Processou {processed:N0} tasks, mas com custo O(n²) de re-registro",
                Metrics: new[]
                {
                    new MetricItem("Tasks", n.ToString("N0")),
                    new MetricItem("Algoritmo", "O(n²)", "WhenAny + Remove em loop"),
                    new MetricItem("API", "Task.WhenAny"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int n = args.Get(Tasks);
        int maxDelay = args.Get(MaxDelayMs);
        return await MeasureAsync("pattern", async rec =>
        {
            var tasks = Build(n, maxDelay, ct);
            int processed = 0;
            await foreach (Task<int> completed in Task.WhenEach(tasks).WithCancellation(ct))
            {
                await completed;
                processed++;
            }
            rec.Log($"Processou {processed:N0} tasks com Task.WhenEach (O(n))");
            return new VariantOutcome(
                Ok: true,
                Headline: $"Processou {processed:N0} tasks em O(n) com await foreach",
                Metrics: new[]
                {
                    new MetricItem("Tasks", n.ToString("N0")),
                    new MetricItem("Algoritmo", "O(n)", "Streaming"),
                    new MetricItem("API", "Task.WhenEach"),
                });
        });
    }
}
