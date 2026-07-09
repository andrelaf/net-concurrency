namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Tentar réplicas em cadeia de fallback faz você esperar a primeira (mesmo lenta)
/// antes de tentar a próxima. A execução especulativa dispara todas as réplicas em
/// paralelo, usa a primeira que responder e cancela o resto.
/// </summary>
public sealed class SpeculativeDemo : DemoBase
{
    private static readonly DemoParameter Replicas =
        new("replicas", "Réplicas", 5, 2, 12, 1);
    private static readonly DemoParameter BaseLatencyMs =
        new("baseLatencyMs", "Latência base (ms)", 40, 10, 150, 10,
            "A réplica mais rápida; a 1ª da lista é a mais lenta");

    public override DemoInfo Info { get; } = new(
        Id: "speculative-first-success",
        Title: "Fallback em cadeia vs execução especulativa",
        Category: "Pipelines e Padrões",
        Summary: "Dispare réplicas em paralelo e use a 1ª resposta, em vez de tentar uma de cada vez.",
        AntipatternCode:
            """
            // ❌ Tenta as réplicas em ordem. Se a primeira é lenta (mas acaba
            // respondendo), você paga a latência inteira dela antes de sequer
            // considerar uma réplica mais rápida.
            foreach (var replica in replicas)
            {
                try { return await replica.CallAsync(); }  // espera a lenta
                catch { /* tenta a próxima */ }
            }
            """,
        AntipatternExplanation:
            "A cadeia de fallback é sequencial: a latência é ditada pela ordem, não pela réplica mais " +
            "rápida. Se a primeira demora (ou só falha após um timeout longo), todas as outras esperam.",
        PatternCode:
            """
            // ✅ Especulativa (hedging): dispara todas as réplicas ao mesmo
            // tempo, retorna a PRIMEIRA que responder e cancela as demais.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var tasks = replicas.Select(r => r.CallAsync(cts.Token)).ToList();
            while (tasks.Count > 0)
            {
                var done = await Task.WhenAny(tasks);
                tasks.Remove(done);
                if (done.IsCompletedSuccessfully)
                {
                    cts.Cancel();               // cancela as réplicas restantes
                    return await done;          // latência ≈ a mais rápida
                }
            }
            """,
        PatternExplanation:
            "Disparar réplicas redundantes em paralelo e aceitar a primeira resposta corta a latência " +
            "para a da réplica mais rápida (útil contra caudas de latência). Cancelar as restantes com " +
            "um `CancellationTokenSource` ligado evita trabalho e recursos desperdiçados.",
        KeyTakeaways: new[]
        {
            "Fallback em cadeia paga a latência na ordem da lista, não a da réplica mais rápida.",
            "Execução especulativa dispara em paralelo e usa a 1ª resposta (hedging).",
            "Cancele as réplicas restantes com um CancellationTokenSource ligado.",
        },
        SupportsRun: true,
        Parameters: new[] { Replicas, BaseLatencyMs })
    {
        Chapter = null, Since = ".NET 4.5",
        UseCases = new[]
        {
            "Reduzir cauda de latência (p99) consultando réplicas/regiões redundantes.",
            "Failover rápido: usar o primeiro mirror/CDN que responder.",
            "Serviços com latência imprevisível onde uma 2ª tentativa paralela compensa.",
        },
    };

    // A réplica i tem latência decrescente: a de índice 0 é a mais lenta.
    private static async Task<int> CallReplica(int i, int replicas, int baseMs, CancellationToken ct)
    {
        await Task.Delay(baseMs * (replicas - i), ct);
        return i;
    }

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int replicas = args.Get(Replicas);
        int baseMs = args.Get(BaseLatencyMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            int winner = -1;
            for (int i = 0; i < replicas; i++)
            {
                try { winner = await CallReplica(i, replicas, baseMs, ct); break; }
                catch { /* próxima */ }
            }
            rec.Log($"Tentou em ordem; venceu a réplica {winner} após esperar a mais lenta");
            return new VariantOutcome(
                Ok: false,
                Headline: $"Esperou a réplica {winner} em ordem (~{baseMs * replicas} ms, a mais lenta)",
                Metrics: new[]
                {
                    new MetricItem("Réplica usada", winner.ToString()),
                    new MetricItem("Latência", $"~{baseMs * replicas} ms", "A da 1ª da lista"),
                    new MetricItem("Modo", "fallback em cadeia"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int replicas = args.Get(Replicas);
        int baseMs = args.Get(BaseLatencyMs);
        return await MeasureAsync("pattern", async rec =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var tasks = Enumerable.Range(0, replicas)
                .Select(i => CallReplica(i, replicas, baseMs, cts.Token)).ToList();
            int winner = -1;
            while (tasks.Count > 0)
            {
                var done = await Task.WhenAny(tasks);
                tasks.Remove(done);
                if (done.IsCompletedSuccessfully) { winner = await done; cts.Cancel(); break; }
            }
            rec.Log($"Disparou {replicas} réplicas; venceu a mais rápida ({winner}) e cancelou o resto");
            return new VariantOutcome(
                Ok: true,
                Headline: $"Usou a réplica mais rápida ({winner}) em ~{baseMs} ms e cancelou as demais",
                Metrics: new[]
                {
                    new MetricItem("Réplica usada", winner.ToString()),
                    new MetricItem("Latência", $"~{baseMs} ms", "A mais rápida"),
                    new MetricItem("Modo", "especulativa (hedging)"),
                });
        });
    }
}
