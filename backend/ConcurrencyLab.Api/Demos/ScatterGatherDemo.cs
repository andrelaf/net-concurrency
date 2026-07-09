namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Consultar N fontes em sequência soma as latências, e uma fonte que falha
/// derruba o agregado inteiro. O padrão scatter/gather dispara todas em paralelo,
/// isola cada ramo (timeout + try/catch) e agrega os resultados parciais.
/// </summary>
public sealed class ScatterGatherDemo : DemoBase
{
    private static readonly DemoParameter Sources =
        new("sources", "Fontes", 8, 3, 24, 1);
    private static readonly DemoParameter LatencyMs =
        new("latencyMs", "Latência por fonte (ms)", 60, 20, 300, 10);

    public override DemoInfo Info { get; } = new(
        Id: "scatter-gather",
        Title: "Consulta sequencial vs Scatter/Gather (fan-out/fan-in)",
        Category: "Pipelines e Padrões",
        Summary: "Dispare as fontes em paralelo, isole cada ramo e agregue resultados parciais.",
        AntipatternCode:
            """
            // ❌ Consulta as fontes uma a uma: o tempo total é a SOMA das
            // latências. E se uma fonte lança, a exceção derruba todo o
            // agregado — nada de resultado parcial.
            var results = new List<Data>();
            foreach (var src in sources)
                results.Add(await src.QueryAsync());   // soma latências; 1 erro quebra tudo
            return Aggregate(results);
            """,
        AntipatternExplanation:
            "Serializar as consultas soma as latências e acopla a saúde do agregado à pior fonte: uma " +
            "única exceção propaga e você perde até os resultados que já tinha. Sem timeout por ramo, " +
            "uma fonte lenta trava tudo.",
        PatternCode:
            """
            // ✅ Scatter: dispara todas em paralelo, cada ramo com timeout e
            // try/catch próprios. Gather: agrega quem respondeu — uma fonte
            // lenta/quebrada não afunda as demais.
            var tasks = sources.Select(async src =>
            {
                try { return await src.QueryAsync().WaitAsync(perSourceTimeout, ct); }
                catch { return null; }               // isola a falha do ramo
            });
            Data?[] all = await Task.WhenAll(tasks);   // tempo ≈ ramo mais lento
            return Aggregate(all.Where(x => x is not null));
            """,
        PatternExplanation:
            "Fan-out dispara as consultas juntas (tempo total ≈ a fonte mais lenta, não a soma). " +
            "Isolar cada ramo com `WaitAsync(timeout)` + `try/catch` transforma falhas locais em " +
            "resultado parcial, e o fan-in agrega o que chegou. É o padrão de composição de serviços " +
            "resiliente.",
        KeyTakeaways: new[]
        {
            "Fan-out paraleliza: tempo total ≈ fonte mais lenta, não a soma das latências.",
            "Isole cada ramo (timeout + try/catch) para obter resultado parcial resiliente.",
            "Uma fonte lenta ou quebrada não deve derrubar o agregado inteiro.",
        },
        SupportsRun: true,
        Parameters: new[] { Sources, LatencyMs })
    {
        Chapter = null, Since = ".NET 4.5",
        UseCases = new[]
        {
            "Agregadores (busca federada, comparador de preços) consultando N fontes.",
            "Compor uma resposta a partir de vários serviços tolerando a falha de alguns.",
            "Enriquecer um registro consultando várias APIs em paralelo com resultado parcial.",
        },
    };

    // A fonte 0 sempre falha; as demais respondem após sua latência.
    private static async Task<int> QuerySource(int index, int latencyMs, CancellationToken ct)
    {
        await Task.Delay(latencyMs, ct);
        if (index == 0) throw new InvalidOperationException($"fonte {index} indisponível");
        return index;
    }

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int sources = args.Get(Sources);
        int latency = args.Get(LatencyMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            int gathered = 0;
            bool broke = false;
            try
            {
                for (int i = 0; i < sources; i++)
                {
                    await QuerySource(i, latency, ct); // sequencial; a fonte 0 lança
                    gathered++;
                }
            }
            catch (Exception ex)
            {
                broke = true;
                rec.Log($"Uma fonte falhou e derrubou o agregado: {ex.Message}");
            }
            rec.Log($"Agregou {gathered}/{sources} fontes antes de quebrar");
            return new VariantOutcome(
                Ok: false,
                Headline: broke
                    ? $"Uma fonte quebrou o agregado inteiro — só {gathered}/{sources} antes de falhar"
                    : $"Consultou {gathered}/{sources} em sequência (soma das latências)",
                Metrics: new[]
                {
                    new MetricItem("Fontes ok", $"{gathered}/{sources}", "Falha derruba tudo"),
                    new MetricItem("Modo", "sequencial"),
                    new MetricItem("Tempo", "soma das latências"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int sources = args.Get(Sources);
        int latency = args.Get(LatencyMs);
        int timeout = latency * 3;
        return await MeasureAsync("pattern", async rec =>
        {
            var tasks = Enumerable.Range(0, sources).Select(async i =>
            {
                try
                {
                    int r = await QuerySource(i, latency, ct).WaitAsync(TimeSpan.FromMilliseconds(timeout), ct);
                    return (int?)r;
                }
                catch { return (int?)null; } // isola a falha do ramo
            });
            int?[] all = await Task.WhenAll(tasks);
            int gathered = all.Count(x => x is not null);

            rec.Log($"Fan-out de {sources} fontes; agregou {gathered} (fonte 0 falhou, isolada)");
            return new VariantOutcome(
                Ok: gathered == sources - 1,
                Headline: $"Agregou {gathered}/{sources} em paralelo — a fonte quebrada foi isolada",
                Metrics: new[]
                {
                    new MetricItem("Fontes ok", $"{gathered}/{sources}", "Parcial resiliente"),
                    new MetricItem("Modo", "fan-out/fan-in"),
                    new MetricItem("Tempo", "≈ fonte mais lenta"),
                });
        });
    }
}
