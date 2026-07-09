namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Criar uma thread de SO dedicada por pequeno item de trabalho é caro (~1 MB de
/// stack, escalonamento do kernel). O thread pool / Task reutiliza um conjunto
/// pequeno de threads.
/// </summary>
public sealed class ThreadVsTaskDemo : DemoBase
{
    private static readonly DemoParameter WorkItems =
        new("workItems", "Itens de trabalho", 1_000, 100, 5_000, 100,
            "Cada um faz um pouquinho de trabalho");

    public override DemoInfo Info { get; } = new(
        Id: "thread-per-item-vs-task",
        Title: "new Thread por item vs o Thread Pool (Task)",
        Category: "Fundamentos",
        Summary: "Threads são recursos pesados do SO; Tasks reutilizam um pool gerenciado.",
        AntipatternCode:
            """
            // ❌ Uma thread de SO por item de trabalho. Cada Thread reserva ~1 MB
            // de stack e precisa de escalonamento do kernel. Milhares delas
            // sobrecarregam o escalonador e podem esgotar a memória — para
            // trabalho que dura microssegundos.
            var threads = new List<Thread>();
            foreach (var item in items)
            {
                var t = new Thread(() => Process(item));
                t.Start();
                threads.Add(t);
            }
            foreach (var t in threads) t.Join();
            """,
        AntipatternExplanation:
            "Criar uma `Thread` é uma operação pesada. Para muitas tarefas curtas, o custo de criação " +
            "e troca de contexto engole o trabalho real, e a pegada de memória (stacks) pode ser " +
            "enorme. Threads cruas são para trabalho dedicado e de longa duração — não para fan-out geral.",
        PatternCode:
            """
            // ✅ Tasks rodam no thread pool, que mantém um pequeno conjunto de
            // threads aquecidas e as reutiliza. Ideal para muitos itens curtos.
            var tasks = items.Select(item => Task.Run(() => Process(item)));
            await Task.WhenAll(tasks);

            // Para paralelismo de dados CPU-bound, prefira Parallel.For /
            // Parallel.ForEachAsync, que também particionam eficientemente.
            """,
        PatternExplanation:
            "`Task.Run` enfileira trabalho no thread pool, que amortiza a criação de threads entre " +
            "muitos itens e se ajusta à máquina. Você ganha concorrência sem pagar por uma thread por " +
            "item. Use uma `Thread` de verdade só para trabalho de longa duração ou de prioridade especial.",
        KeyTakeaways: new[]
        {
            "Uma Thread é um recurso de SO de ~1 MB — não crie uma por tarefa pequena.",
            "Task/Task.Run usa o thread pool reutilizável e agrupado.",
            "Para trabalho de longa duração, mantenha-o fora do pool com TaskCreationOptions.LongRunning.",
        },
        SupportsRun: true,
        Parameters: new[] { WorkItems })
    {
        Chapter = "Cap. 1 · Conceitos de Managed Threading", Since = ".NET 4.5",
        UseCases = new[]
        {
            "Processar muitas tarefas curtas e independentes (ex.: redimensionar 1.000 imagens).",
            "Disparar trabalho em paralelo sem gerenciar threads na mão.",
            "Reserve uma Thread dedicada só para trabalho longo e contínuo (ex.: loop lendo uma porta serial).",
        },
    };

    // Pequena unidade de trabalho.
    private static void Process() => Workloads.Spin(200);

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int items = args.Get(WorkItems);
        return MeasureAsync("antipattern", rec =>
        {
            var threads = new Thread[items];
            for (int i = 0; i < items; i++)
            {
                threads[i] = new Thread(Process) { IsBackground = true };
                threads[i].Start();
            }
            foreach (var t in threads) t.Join();

            rec.Log($"Criou e aguardou {items:N0} threads de SO dedicadas");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"Criou {items:N0} threads de SO (~{items} MB de stack reservados)",
                Metrics: new[]
                {
                    new MetricItem("Itens de trabalho", items.ToString("N0")),
                    new MetricItem("Threads criadas", items.ToString("N0"), "Uma por item"),
                    new MetricItem("Modelo", "new Thread"),
                }));
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int items = args.Get(WorkItems);
        return await MeasureAsync("pattern", async rec =>
        {
            var before = ThreadPool.ThreadCount;
            var tasks = Enumerable.Range(0, items).Select(_ => Task.Run(Process, ct));
            await Task.WhenAll(tasks);
            var peak = ThreadPool.ThreadCount;

            rec.Log($"Rodou {items:N0} itens no thread pool (~{peak} threads agrupadas)");
            return new VariantOutcome(
                Ok: true,
                Headline: $"Rodou {items:N0} itens em ~{peak} threads reutilizadas do pool",
                Metrics: new[]
                {
                    new MetricItem("Itens de trabalho", items.ToString("N0")),
                    new MetricItem("Threads do pool", peak.ToString(), "Reutilizadas, não por item"),
                    new MetricItem("Modelo", "Task.Run (thread pool)"),
                });
        });
    }
}
