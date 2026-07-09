namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Trabalho que ignora seu CancellationToken continua rodando depois que o
/// chamador desiste (desperdiçando CPU e atrasando o shutdown). O cancelamento
/// cooperativo para mais cedo.
/// </summary>
public sealed class CancellationDemo : DemoBase
{
    private static readonly DemoParameter Batches =
        new("batches", "Lotes de trabalho", 40, 10, 100, 5);
    private static readonly DemoParameter CancelAfterMs =
        new("cancelAfterMs", "Cancelar após (ms)", 120, 20, 400, 10);

    public override DemoInfo Info { get; } = new(
        Id: "cooperative-cancellation",
        Title: "Ignorar vs honrar um CancellationToken",
        Category: "Coordenação Async",
        Summary: "O cancelamento no .NET é cooperativo — código que nunca checa o token não pode ser parado.",
        AntipatternCode:
            """
            // ❌ O token é aceito mas nunca observado. Quando o chamador cancela
            // (timeout, usuário sai da página), este loop continua queimando CPU
            // até o fim. O cancelamento é cooperativo — ignorar o token faz com
            // que ele não tenha efeito nenhum.
            async Task ProcessAsync(CancellationToken ct)
            {
                foreach (var batch in batches)
                    await CrunchAsync(batch);   // ct nunca passado ou checado
            }
            """,
        AntipatternExplanation:
            "Passar um `CancellationToken` adiante não basta — você precisa realmente observá-lo. " +
            "Código que nunca chama `ct.ThrowIfCancellationRequested()` (ou passa `ct` para APIs " +
            "canceláveis) roda até o fim, não importa o que o chamador queira.",
        PatternCode:
            """
            // ✅ Observe o token: passe-o para as chamadas async e/ou cheque-o.
            // O loop se desfaz prontamente via OperationCanceledException.
            async Task ProcessAsync(CancellationToken ct)
            {
                foreach (var batch in batches)
                {
                    ct.ThrowIfCancellationRequested();
                    await CrunchAsync(batch, ct);   // propaga o token
                }
            }
            """,
        PatternExplanation:
            "Passar o token pelas chamadas async e checá-lo entre unidades de trabalho deixa a " +
            "operação parar em milissegundos após um pedido de cancelamento, liberando a thread e a CPU.",
        KeyTakeaways: new[]
        {
            "O cancelamento no .NET é cooperativo — você precisa observar o token.",
            "Passe o CancellationToken para toda chamada cancelável; cheque-o entre unidades de trabalho.",
            "OperationCanceledException é o sinal esperado, não uma falha.",
        },
        SupportsRun: true,
        Parameters: new[] { Batches, CancelAfterMs })
    {
        Chapter = "Cap. 11 · Cancelando Trabalho Assíncrono", Since = ".NET 4.0",
        UseCases = new[]
        {
            "Cancelar trabalho quando o cliente desconecta (HttpContext.RequestAborted).",
            "Timeouts e botões 'cancelar' em operações longas.",
            "Encerrar tarefas de background de forma limpa no shutdown do app.",
        },
    };

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int batches = args.Get(Batches);
        int cancelAfter = args.Get(CancelAfterMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(cancelAfter);
            int done = 0;
            for (int i = 0; i < batches; i++)
            {
                await Task.Delay(15, ct); // ignora cts.Token de propósito
                done++;
            }
            rec.Log($"Pediu cancelamento após {cancelAfter} ms, mas rodou todos os {batches} lotes");
            return new VariantOutcome(
                Ok: false,
                Headline: $"Rodou todos os {done}/{batches} lotes — o cancelamento foi ignorado",
                Metrics: new[]
                {
                    new MetricItem("Lotes feitos", $"{done}/{batches}", "Trabalho desperdiçado pós-cancelamento"),
                    new MetricItem("Cancelado em", $"{cancelAfter} ms"),
                    new MetricItem("Parou cedo", "Não"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int batches = args.Get(Batches);
        int cancelAfter = args.Get(CancelAfterMs);
        return await MeasureAsync("pattern", async rec =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(cancelAfter);
            int done = 0;
            bool cancelled = false;
            try
            {
                for (int i = 0; i < batches; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await Task.Delay(15, cts.Token); // honra o token
                    done++;
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                rec.Log($"Cancelado cooperativamente após {done} lotes");
            }
            return new VariantOutcome(
                Ok: true,
                Headline: cancelled
                    ? $"Parou cedo após {done}/{batches} lotes no cancelamento"
                    : $"Concluiu todos os {done}/{batches} antes do prazo",
                Metrics: new[]
                {
                    new MetricItem("Lotes feitos", $"{done}/{batches}", "Parou prontamente"),
                    new MetricItem("Cancelado em", $"{cancelAfter} ms"),
                    new MetricItem("Parou cedo", cancelled ? "Sim" : "Não"),
                });
        });
    }
}
