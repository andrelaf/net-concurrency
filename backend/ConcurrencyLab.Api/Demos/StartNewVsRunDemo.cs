namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Task.Factory.StartNew com um delegate async retorna Task&lt;Task&gt;, então dar
/// await nele completa no primeiro await — antes de o corpo async terminar.
/// Task.Run faz o unwrap automático e é a API recomendada pela documentação.
/// Referência: Microsoft "Task-based asynchronous programming" (TPL).
/// </summary>
public sealed class StartNewVsRunDemo : DemoBase
{
    private static readonly DemoParameter Tasks =
        new("tasks", "Tasks async", 200, 20, 1000, 20);
    private static readonly DemoParameter DelayMs =
        new("delayMs", "Trabalho por task (ms)", 60, 20, 200, 10,
            "Delay async dentro de cada task");

    public override DemoInfo Info { get; } = new(
        Id: "startnew-vs-run",
        Title: "Task.Factory.StartNew vs Task.Run (delegates async)",
        Category: "Fundamentos",
        Summary: "StartNew com um lambda async retorna Task<Task> — dar await nele termina cedo demais.",
        AntipatternCode:
            """
            // ❌ StartNew com um delegate async retorna Task<Task>.
            // Dar await na task EXTERNA completa no primeiro 'await' dentro do
            // lambda — o trabalho async interno NÃO terminou ainda.
            // Armadilha extra: StartNew usa TaskScheduler.Current, não o Default.
            var tasks = items.Select(x =>
                Task.Factory.StartNew(async () =>
                {
                    await ProcessAsync(x);      // roda DEPOIS que a task externa 'completa'
                }));

            await Task.WhenAll(tasks);          // retorna antes de ProcessAsync terminar!
            // ...o código aqui roda enquanto o trabalho ainda está em voo

            // Se você PRECISA usar StartNew, tem que fazer Unwrap():
            //   Task.Factory.StartNew(() => ProcessAsync(x)).Unwrap()
            """,
        AntipatternExplanation:
            "`Task.Factory.StartNew(Func<Task>)` te devolve uma `Task<Task>`: a task externa representa " +
            "*iniciar* o método async, que retorna no seu primeiro `await`. Dar await nela portanto " +
            "completa bem antes de o trabalho real terminar — um bug de correção sutil. StartNew também " +
            "escalona no `TaskScheduler.Current`, que pode não ser o thread pool.",
        PatternCode:
            """
            // ✅ Task.Run faz o unwrap da Task interna automaticamente e sempre
            // usa o scheduler default (thread pool). É a forma recomendada pela
            // documentação para iniciar uma task quando não precisa de opções
            // avançadas.
            var tasks = items.Select(x =>
                Task.Run(async () =>
                {
                    await ProcessAsync(x);
                }));

            await Task.WhenAll(tasks);          // aguarda TODO o trabalho terminar
            // ...o código aqui roda só depois que cada ProcessAsync completou
            """,
        PatternExplanation:
            "`Task.Run(Func<Task>)` retorna um proxy que completa apenas quando o trabalho async " +
            "interno completa, e sempre escalona no `TaskScheduler.Default`. Pela documentação da TPL " +
            "é a API recomendada; reserve o `Task.Factory.StartNew` para casos avançados (scheduler " +
            "customizado, `TaskCreationOptions` como `LongRunning`), e chame `.Unwrap()` se o delegate " +
            "for async.",
        KeyTakeaways: new[]
        {
            "Task.Run é a forma recomendada de iniciar uma task (documentação da TPL).",
            "StartNew(async …) retorna Task<Task> — o await termina no primeiro await interno.",
            "Use StartNew só para opções avançadas; então faça Unwrap() dos delegates async.",
        },
        SupportsRun: true,
        Parameters: new[] { Tasks, DelayMs })
    {
        Chapter = "Cap. 5 · Programação Assíncrona com C#", Since = ".NET 4.5",
        UseCases = new[]
        {
            "Iniciar trabalho em background a partir de um serviço/handler — sempre Task.Run.",
            "Descarregar cálculo CPU-bound do thread de requisição sem bloquear.",
            "Só recorra a StartNew para scheduler customizado ou TaskCreationOptions.LongRunning (com Unwrap).",
        },
    };

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int n = args.Get(Tasks);
        int delay = args.Get(DelayMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            int completed = 0;
            // StartNew(Func<Task>) retorna Task<Task>; tipada como Task é a task EXTERNA.
            var outer = Enumerable.Range(0, n).Select(_ =>
                (Task)Task.Factory.StartNew(async () =>
                {
                    await Task.Delay(delay, ct);
                    Interlocked.Increment(ref completed);
                })).ToArray();

            await Task.WhenAll(outer); // só aguarda as tasks externas
            int doneWhenWeThoughtWeWereDone = Volatile.Read(ref completed);

            rec.Log($"await Task.WhenAll retornou; {doneWhenWeThoughtWeWereDone}/{n} corpos async tinham terminado");
            rec.Log("A Task<Task> externa completou no primeiro await interno — trabalho ainda em voo.");
            return new VariantOutcome(
                Ok: doneWhenWeThoughtWeWereDone == n,
                Headline: $"WhenAll retornou com só {doneWhenWeThoughtWeWereDone}/{n} tasks realmente terminadas",
                Metrics: new[]
                {
                    new MetricItem("Prontas no await", $"{doneWhenWeThoughtWeWereDone}/{n}", "Cedo demais!"),
                    new MetricItem("Retorna", "Task<Task>", "Sem unwrap"),
                    new MetricItem("Scheduler", "Current"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int n = args.Get(Tasks);
        int delay = args.Get(DelayMs);
        return await MeasureAsync("pattern", async rec =>
        {
            int completed = 0;
            var tasks = Enumerable.Range(0, n).Select(_ =>
                Task.Run(async () =>
                {
                    await Task.Delay(delay, ct);
                    Interlocked.Increment(ref completed);
                })).ToArray();

            await Task.WhenAll(tasks); // com unwrap: aguarda o trabalho real
            int done = Volatile.Read(ref completed);

            rec.Log($"await Task.WhenAll retornou; {done}/{n} corpos async tinham terminado");
            return new VariantOutcome(
                Ok: done == n,
                Headline: $"WhenAll aguardou todas as {done}/{n} tasks realmente terminarem",
                Metrics: new[]
                {
                    new MetricItem("Prontas no await", $"{done}/{n}", "Correto"),
                    new MetricItem("Retorna", "Task", "Unwrap automático"),
                    new MetricItem("Scheduler", "Default"),
                });
        });
    }
}
