namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// <c>await Task.WhenAll(tasks)</c> relança apenas a *primeira* exceção — se várias
/// tasks falharem, as demais somem do seu catch. Guardar a task do WhenAll e ler
/// <c>Exception.InnerExceptions</c> expõe todas.
/// </summary>
public sealed class AggregateExceptionDemo : DemoBase
{
    private static readonly DemoParameter Tasks =
        new("tasks", "Tasks", 24, 6, 100, 2);
    private static readonly DemoParameter FailEvery =
        new("failEvery", "Falha a cada N tasks", 3, 2, 10, 1,
            "Menor = mais falhas");

    public override DemoInfo Info { get; } = new(
        Id: "whenall-exception-handling",
        Title: "Exceções em Task.WhenAll (só a 1ª vs todas)",
        Category: "Riscos",
        Summary: "await Task.WhenAll relança só uma exceção; várias falhas ficam escondidas.",
        AntipatternCode:
            """
            // ❌ 'await Task.WhenAll' relança apenas a PRIMEIRA exceção.
            // Se 7 tasks falharam, seu catch vê só 1 — as outras 6 somem.
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Log(ex);            // uma única falha; as demais ficam ocultas
            }
            """,
        AntipatternExplanation:
            "Uma `Task` que aguarda `WhenAll` guarda um `AggregateException`, mas o `await` desembrulha " +
            "e relança só a primeira. Com múltiplas falhas, seu `catch (Exception)` registra uma e " +
            "esconde o resto — diagnóstico incompleto e falhas silenciosas.",
        PatternCode:
            """
            // ✅ Guarde a task do WhenAll e leia todas as falhas do
            // AggregateException (ou inspecione cada task individualmente).
            var whenAll = Task.WhenAll(tasks);
            try
            {
                await whenAll;
            }
            catch
            {
                foreach (var e in whenAll.Exception!.InnerExceptions)
                    Log(e);         // TODAS as falhas
            }

            // Alternativa: percorrer as tasks e olhar t.Exception de cada uma.
            """,
        PatternExplanation:
            "A `Task` retornada por `Task.WhenAll` carrega um `AggregateException` com *todas* as " +
            "`InnerExceptions`. Guardando a referência (em vez de só dar `await`) você acessa cada " +
            "falha. Também dá para percorrer as tasks e checar `task.Exception` uma a uma.",
        KeyTakeaways: new[]
        {
            "await Task.WhenAll relança só a primeira exceção — as outras ficam ocultas.",
            "Guarde a task do WhenAll e leia Exception.InnerExceptions para ver todas.",
            "Ou inspecione cada task (task.IsFaulted / task.Exception) individualmente.",
        },
        SupportsRun: true,
        Parameters: new[] { Tasks, FailEvery })
    {
        Chapter = "Cap. 5 · Programação Assíncrona com C#", Since = ".NET 4.5",
        UseCases = new[]
        {
            "Operações em lote onde você precisa saber TODAS as falhas (ex.: importar N registros).",
            "Fan-out para vários serviços com relatório de erros/observabilidade completo.",
            "Retentar/compensar seletivamente só os itens que falharam.",
        },
    };

    private static Task Work(int i, int failEvery, CancellationToken ct) =>
        Task.Run(async () =>
        {
            await Task.Delay(5, ct);
            if (i % failEvery == 0)
                throw new InvalidOperationException($"falha na task {i}");
        }, ct);

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int n = args.Get(Tasks);
        int failEvery = args.Get(FailEvery);
        int actualFailures = Enumerable.Range(0, n).Count(i => i % failEvery == 0);
        return await MeasureAsync("antipattern", async rec =>
        {
            var tasks = Enumerable.Range(0, n).Select(i => Work(i, failEvery, ct)).ToArray();
            int seen = 0;
            try { await Task.WhenAll(tasks); }
            catch (Exception ex) { seen = 1; rec.Log($"catch viu 1 exceção: {ex.Message}"); }

            rec.Log($"Falhas reais: {actualFailures}; relatadas pelo catch: {seen}");
            return new VariantOutcome(
                Ok: seen == actualFailures,
                Headline: $"Relatou {seen} de {actualFailures} falhas — as demais ficaram ocultas",
                Metrics: new[]
                {
                    new MetricItem("Falhas reais", actualFailures.ToString()),
                    new MetricItem("Relatadas", seen.ToString(), "Só a 1ª"),
                    new MetricItem("Ocultas", (actualFailures - seen).ToString()),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int n = args.Get(Tasks);
        int failEvery = args.Get(FailEvery);
        int actualFailures = Enumerable.Range(0, n).Count(i => i % failEvery == 0);
        return await MeasureAsync("pattern", async rec =>
        {
            var tasks = Enumerable.Range(0, n).Select(i => Work(i, failEvery, ct)).ToArray();
            var whenAll = Task.WhenAll(tasks);
            int seen = 0;
            try { await whenAll; }
            catch { seen = whenAll.Exception?.InnerExceptions.Count ?? 0; }

            rec.Log($"Falhas reais: {actualFailures}; relatadas via InnerExceptions: {seen}");
            return new VariantOutcome(
                Ok: seen == actualFailures,
                Headline: $"Relatou todas as {seen} de {actualFailures} falhas via AggregateException",
                Metrics: new[]
                {
                    new MetricItem("Falhas reais", actualFailures.ToString()),
                    new MetricItem("Relatadas", seen.ToString(), "Todas"),
                    new MetricItem("Ocultas", (actualFailures - seen).ToString()),
                });
        });
    }
}
