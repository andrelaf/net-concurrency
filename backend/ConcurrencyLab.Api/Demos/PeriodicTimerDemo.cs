namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Um loop <c>while</c> + <c>Task.Delay(intervalo)</c> soma o tempo de trabalho ao
/// intervalo, então os ticks derivam (drift). <c>PeriodicTimer</c> (.NET 6) mantém
/// a cadência fixa enquanto o trabalho couber no intervalo.
/// </summary>
public sealed class PeriodicTimerDemo : DemoBase
{
    private static readonly DemoParameter Ticks =
        new("ticks", "Número de ticks", 12, 4, 40, 1);
    private static readonly DemoParameter IntervalMs =
        new("intervalMs", "Intervalo alvo (ms)", 50, 20, 200, 10);
    private static readonly DemoParameter WorkMs =
        new("workMs", "Trabalho por tick (ms)", 20, 0, 100, 5);

    public override DemoInfo Info { get; } = new(
        Id: "delay-loop-vs-periodictimer",
        Title: "Loop com Task.Delay vs PeriodicTimer",
        Category: "Coordenação Async",
        Summary: "PeriodicTimer (.NET 6) mantém a cadência; while+Delay soma o tempo de trabalho e deriva.",
        AntipatternCode:
            """
            // ❌ while + Task.Delay: o período real é intervalo + tempo de
            // trabalho. Cada tick "atrasa" o próximo, acumulando drift.
            while (running)
            {
                await Task.Delay(interval);   // espera FIXA
                await DoWorkAsync();          // soma ao período
            }
            // período efetivo ≈ interval + duração do trabalho
            """,
        AntipatternExplanation:
            "Dormir por um intervalo fixo e só então trabalhar faz cada ciclo durar `intervalo + " +
            "trabalho`. Ao longo de muitos ticks isso acumula desvio (drift): um \"a cada 50 ms\" vira " +
            "\"a cada 70 ms\" se o trabalho leva 20 ms.",
        PatternCode:
            """
            // ✅ PeriodicTimer sinaliza em cadência fixa. WaitForNextTickAsync
            // desconta o tempo de trabalho já decorrido, mantendo o intervalo
            // (enquanto o trabalho for menor que ele).
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await DoWorkAsync();
            }
            // período efetivo ≈ interval (sem drift acumulado)
            """,
        PatternExplanation:
            "`PeriodicTimer` mede o intervalo entre ticks, não entre o fim de um trabalho e o começo " +
            "do próximo. `WaitForNextTickAsync` retorna assim que o próximo tick chega, absorvendo o " +
            "tempo que o trabalho consumiu. É a forma moderna, async e sem drift de agendar trabalho " +
            "periódico.",
        KeyTakeaways: new[]
        {
            "while+Task.Delay acumula drift: período = intervalo + tempo de trabalho.",
            "PeriodicTimer (.NET 6) mantém a cadência enquanto o trabalho couber no intervalo.",
            "WaitForNextTickAsync integra com CancellationToken para parada limpa.",
        },
        SupportsRun: true,
        Parameters: new[] { Ticks, IntervalMs, WorkMs })
    {
        Chapter = "Cap. 5 · Programação Assíncrona com C#", Since = ".NET 6",
        UseCases = new[]
        {
            "Polling periódico (health check, ler uma fila a cada N segundos) sem drift.",
            "BackgroundService que executa em cadência fixa (ex.: flush de métricas/cache).",
            "Heartbeats e tarefas agendadas leves dentro do processo.",
        },
    };

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int ticks = args.Get(Ticks);
        int interval = args.Get(IntervalMs);
        int work = args.Get(WorkMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            for (int i = 0; i < ticks; i++)
            {
                await Task.Delay(interval, ct); // espera fixa
                if (work > 0) await Task.Delay(work, ct); // trabalho soma ao período
            }
            long ideal = (long)ticks * interval;
            rec.Log($"{ticks} ticks; período efetivo ≈ {interval}+{work} ms cada");
            return new VariantOutcome(
                Ok: false,
                Headline: $"{ticks} ticks derivaram para ~{interval + work} ms cada (alvo {interval} ms)",
                Metrics: new[]
                {
                    new MetricItem("Ticks", ticks.ToString()),
                    new MetricItem("Período efetivo", $"~{interval + work} ms", "Com drift"),
                    new MetricItem("Ideal total", $"~{ideal} ms"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int ticks = args.Get(Ticks);
        int interval = args.Get(IntervalMs);
        int work = args.Get(WorkMs);
        return await MeasureAsync("pattern", async rec =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
            int done = 0;
            while (done < ticks && await timer.WaitForNextTickAsync(ct))
            {
                if (work > 0) await Task.Delay(work, ct); // absorvido pelo próximo intervalo
                done++;
            }
            long ideal = (long)ticks * interval;
            rec.Log($"{done} ticks mantendo a cadência de {interval} ms (trabalho de {work} ms absorvido)");
            return new VariantOutcome(
                Ok: true,
                Headline: $"{done} ticks mantiveram ~{interval} ms cada — sem drift acumulado",
                Metrics: new[]
                {
                    new MetricItem("Ticks", done.ToString()),
                    new MetricItem("Período efetivo", $"~{interval} ms", "Sem drift"),
                    new MetricItem("Ideal total", $"~{ideal} ms"),
                });
        });
    }
}
