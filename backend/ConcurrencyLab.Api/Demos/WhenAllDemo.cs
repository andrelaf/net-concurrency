namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Aguardar chamadas de I/O independentes uma de cada vez serializa suas latências.
/// Task.WhenAll as inicia juntas, então o tempo total ≈ a chamada mais lenta.
/// </summary>
public sealed class WhenAllDemo : DemoBase
{
    private static readonly DemoParameter Calls =
        new("calls", "Número de chamadas de I/O", 10, 2, 40, 1);
    private static readonly DemoParameter LatencyMs =
        new("latencyMs", "Latência por chamada (ms)", 100, 20, 500, 10);

    public override DemoInfo Info { get; } = new(
        Id: "whenall-vs-sequential-await",
        Title: "await sequencial vs Task.WhenAll (I/O-bound)",
        Category: "Coordenação Async",
        Summary: "Chamadas async independentes devem se sobrepor, não rodar em sequência.",
        AntipatternCode:
            """
            // ❌ 'await' dentro do loop inicia a chamada N+1 só depois que N retorna.
            // Tempo total ≈ chamadas × latência, mesmo sendo independentes.
            var results = new List<string>();
            foreach (var url in urls)
            {
                var r = await httpClient.GetStringAsync(url);  // espera por completo
                results.Add(r);
            }
            """,
        AntipatternExplanation:
            "Cada `await` suspende até aquela chamada terminar antes de a próxima começar. As " +
            "chamadas não dependem umas das outras, então essa serialização é puro tempo de relógio " +
            "desperdiçado.",
        PatternCode:
            """
            // ✅ Dispare todas as chamadas primeiro, depois aguarde-as juntas.
            // Tempo total ≈ a única chamada mais lenta (elas se sobrepõem).
            var tasks = urls.Select(url => httpClient.GetStringAsync(url));
            string[] results = await Task.WhenAll(tasks);
            """,
        PatternExplanation:
            "Iniciar as tasks antes de aguardar deixa suas latências se sobreporem. `Task.WhenAll` " +
            "completa quando a última termina. Para milhares de chamadas, adicione um `SemaphoreSlim` " +
            "para limitar a concorrência (veja a demo de throttling).",
        KeyTakeaways: new[]
        {
            "`await` em um loop serializa I/O independente — um bug de latência muito comum.",
            "Inicie as tasks, depois `await Task.WhenAll` para sobrepô-las.",
            "WhenAll expõe a primeira exceção; inspecione Task.Exception para todas.",
        },
        SupportsRun: true,
        Parameters: new[] { Calls, LatencyMs })
    { Chapter = "Cap. 5 · Programação Assíncrona com C#" };

    // Chamada de I/O assíncrona simulada.
    private static Task<int> CallAsync(int latencyMs, CancellationToken ct) =>
        Task.Delay(latencyMs, ct).ContinueWith(_ => 1, ct);

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int calls = args.Get(Calls);
        int latency = args.Get(LatencyMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            int total = 0;
            for (int i = 0; i < calls; i++)
            {
                total += await CallAsync(latency, ct); // aguardadas uma a uma
                rec.Log($"Chamada {i + 1}/{calls} concluída (esperou {latency} ms)");
            }
            return new VariantOutcome(
                Ok: true,
                Headline: $"{calls} chamadas em sequência ≈ {calls}×{latency} ms",
                Metrics: new[]
                {
                    new MetricItem("Chamadas", calls.ToString()),
                    new MetricItem("Modo", "await sequencial"),
                    new MetricItem("Tempo ideal", $"~{calls * latency} ms"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int calls = args.Get(Calls);
        int latency = args.Get(LatencyMs);
        return await MeasureAsync("pattern", async rec =>
        {
            var tasks = Enumerable.Range(0, calls).Select(_ => CallAsync(latency, ct)).ToArray();
            rec.Log($"Iniciou {calls} chamadas concorrentemente");
            int[] results = await Task.WhenAll(tasks);
            rec.Log($"Todas as {calls} chamadas concluídas");
            return new VariantOutcome(
                Ok: true,
                Headline: $"{calls} chamadas sobrepostas ≈ uma chamada de {latency} ms",
                Metrics: new[]
                {
                    new MetricItem("Chamadas", calls.ToString()),
                    new MetricItem("Modo", "Task.WhenAll"),
                    new MetricItem("Tempo ideal", $"~{latency} ms"),
                });
        });
    }
}
