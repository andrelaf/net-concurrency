namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Aplicar timeout a uma task com <c>Task.WhenAny(work, Task.Delay(timeout))</c>
/// deixa um timer pendurado e não cancela o trabalho. <c>Task.WaitAsync(timeout)</c>
/// (.NET 6) faz isso de forma limpa, lançando <c>TimeoutException</c>.
/// </summary>
public sealed class WaitAsyncTimeoutDemo : DemoBase
{
    private static readonly DemoParameter WorkMs =
        new("workMs", "Duração do trabalho (ms)", 300, 50, 800, 10);
    private static readonly DemoParameter TimeoutMs =
        new("timeoutMs", "Timeout (ms)", 120, 20, 500, 10);

    public override DemoInfo Info { get; } = new(
        Id: "whenany-delay-vs-waitasync",
        Title: "Timeout manual (WhenAny+Delay) vs Task.WaitAsync",
        Category: "Coordenação Async",
        Summary: "Task.WaitAsync (.NET 6) aplica timeout sem vazar timer nem deixar o trabalho solto.",
        AntipatternCode:
            """
            // ❌ Timeout "na mão" correndo a task contra um Task.Delay.
            // Problemas: se o trabalho vence, o timer do Delay continua vivo
            // (vaza até disparar); e o trabalho não é cancelado no timeout —
            // ele segue rodando em background, ignorado.
            var delay = Task.Delay(timeout);
            var winner = await Task.WhenAny(work, delay);
            if (winner == delay)
                throw new TimeoutException();   // work continua rodando!
            return await work;
            """,
        AntipatternExplanation:
            "`Task.WhenAny(work, Task.Delay(timeout))` funciona, mas é desperdício: o `Task.Delay` " +
            "aloca um timer que não é cancelado quando o trabalho termina antes, e no caso de timeout " +
            "a task de trabalho fica órfã, consumindo recursos até terminar sozinha.",
        PatternCode:
            """
            // ✅ Task.WaitAsync (.NET 6) aplica o timeout diretamente. Lança
            // TimeoutException no estouro e limpa o timer interno. Combine com
            // um CancellationToken para também parar o trabalho.
            using var cts = new CancellationTokenSource();
            var work = DoWorkAsync(cts.Token);
            try
            {
                return await work.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                cts.Cancel();                    // para o trabalho de verdade
                throw;
            }
            """,
        PatternExplanation:
            "`WaitAsync(TimeSpan)` completa a task quando ela termina ou lança `TimeoutException` no " +
            "prazo, gerenciando o timer internamente (sem vazamento). Passar um `CancellationToken` " +
            "para o trabalho permite realmente interrompê-lo no timeout, em vez de deixá-lo órfão.",
        KeyTakeaways: new[]
        {
            "Use Task.WaitAsync(timeout) (.NET 6) em vez de correr contra Task.Delay.",
            "WhenAny+Delay vaza o timer e deixa o trabalho rodando após o timeout.",
            "Combine WaitAsync com um CancellationToken para cancelar o trabalho de fato.",
        },
        SupportsRun: true,
        Parameters: new[] { WorkMs, TimeoutMs })
    { Chapter = "Cap. 11 · Cancelando Trabalho Assíncrono", Since = ".NET 6" };

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int workMs = args.Get(WorkMs);
        int timeoutMs = args.Get(TimeoutMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            var workDone = new TaskCompletionSource();
            var work = Task.Run(async () =>
            {
                await Task.Delay(workMs, ct); // não observa timeout
                workDone.TrySetResult();
            }, ct);

            var delay = Task.Delay(timeoutMs, ct);
            var winner = await Task.WhenAny(work, delay);
            bool timedOut = winner == delay;
            // No timeout, 'work' continua rodando: comprovamos que ainda não terminou.
            bool leaked = timedOut && !workDone.Task.IsCompleted;

            rec.Log(timedOut
                ? $"Timeout em {timeoutMs} ms; a task de trabalho continua rodando (órfã)"
                : $"Trabalho terminou em ~{workMs} ms antes do timeout");
            if (timedOut) rec.Log("O timer do Task.Delay também fica pendurado até disparar.");
            return new VariantOutcome(
                Ok: !leaked,
                Headline: timedOut
                    ? "Timeout detectado — mas o trabalho ficou órfão e o timer vazou"
                    : "Trabalho concluído antes do timeout",
                Metrics: new[]
                {
                    new MetricItem("Resultado", timedOut ? "Timeout" : "Concluído"),
                    new MetricItem("Trabalho órfão", leaked ? "Sim" : "Não", "Segue rodando"),
                    new MetricItem("API", "WhenAny + Delay"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int workMs = args.Get(WorkMs);
        int timeoutMs = args.Get(TimeoutMs);
        return await MeasureAsync("pattern", async rec =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var work = Task.Run(async () =>
            {
                await Task.Delay(workMs, cts.Token);
            }, cts.Token);

            bool timedOut = false;
            try
            {
                await work.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
            }
            catch (TimeoutException)
            {
                timedOut = true;
                cts.Cancel(); // cancela o trabalho de verdade
                rec.Log($"TimeoutException em {timeoutMs} ms; trabalho cancelado via token");
            }
            catch (OperationCanceledException) { /* trabalho cancelado */ }

            if (!timedOut) rec.Log($"Trabalho concluído em ~{workMs} ms (sem timer vazado)");
            return new VariantOutcome(
                Ok: true,
                Headline: timedOut
                    ? "TimeoutException limpo — trabalho cancelado, sem timer vazado"
                    : "Trabalho concluído antes do timeout, timer gerenciado internamente",
                Metrics: new[]
                {
                    new MetricItem("Resultado", timedOut ? "Timeout" : "Concluído"),
                    new MetricItem("Trabalho órfão", "Não", "Cancelado"),
                    new MetricItem("API", "Task.WaitAsync"),
                });
        });
    }
}
