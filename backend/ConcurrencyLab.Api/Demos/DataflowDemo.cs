namespace ConcurrencyLab.Api.Demos;

using System.Threading.Tasks.Dataflow;

/// <summary>
/// Um pipeline de múltiplos estágios feito na mão roda os estágios em série e sem
/// paralelismo. O TPL Dataflow (<c>TransformBlock</c>→<c>ActionBlock</c>) sobrepõe
/// os estágios e paraleliza cada um, com back-pressure embutido.
/// </summary>
public sealed class DataflowDemo : DemoBase
{
    private static readonly DemoParameter Items =
        new("items", "Itens no pipeline", 8_000, 1_000, 30_000, 1_000);

    public override DemoInfo Info { get; } = new(
        Id: "manual-pipeline-vs-dataflow",
        Title: "Pipeline manual vs TPL Dataflow",
        Category: "Coleções e Mensageria",
        Summary: "Dataflow monta pipelines com estágios sobrepostos, paralelos e com back-pressure.",
        AntipatternCode:
            """
            // ❌ Pipeline "na mão": cada estágio processa TODA a coleção antes
            // do próximo começar. Sem sobreposição entre estágios, sem
            // paralelismo, e o resultado intermediário inteiro vai pra memória.
            var stage1 = new List<int>(items.Count);
            foreach (var x in items)
                stage1.Add(Transform(x));       // estágio 1 inteiro

            foreach (var y in stage1)
                Consume(y);                      // só então o estágio 2
            """,
        AntipatternExplanation:
            "Materializar cada estágio por completo antes do próximo serializa o pipeline e mantém o " +
            "resultado intermediário todo na memória. Coordenar estágios sobrepostos e paralelos na " +
            "mão (com filas e locks) é justamente o que dá errado.",
        PatternCode:
            """
            // ✅ TPL Dataflow: blocos ligados formam o pipeline. Cada bloco tem
            // seu próprio grau de paralelismo e capacidade (back-pressure); os
            // estágios rodam sobrepostos conforme os itens fluem.
            var transform = new TransformBlock<int, int>(
                Transform,
                new() { MaxDegreeOfParallelism = Environment.ProcessorCount,
                        BoundedCapacity = 1024 });

            var consume = new ActionBlock<int>(
                Consume,
                new() { MaxDegreeOfParallelism = Environment.ProcessorCount,
                        BoundedCapacity = 1024 });

            transform.LinkTo(consume, new() { PropagateCompletion = true });

            foreach (var x in items) await transform.SendAsync(x);
            transform.Complete();
            await consume.Completion;
            """,
        PatternExplanation:
            "`TransformBlock`/`ActionBlock` formam um grafo de fluxo de dados: cada bloco processa em " +
            "paralelo (`MaxDegreeOfParallelism`), limita o buffer (`BoundedCapacity` = back-pressure) e " +
            "propaga conclusão. Os estágios se sobrepõem — enquanto o estágio 1 transforma o item N, o " +
            "estágio 2 já consome o N-1. É a ferramenta certa para pipelines de processamento.",
        KeyTakeaways: new[]
        {
            "Dataflow monta pipelines: blocos ligados com paralelismo e back-pressure por estágio.",
            "PropagateCompletion encadeia a conclusão do primeiro bloco até o último.",
            "Estágios se sobrepõem — melhor throughput e menos memória que materializar cada etapa.",
        },
        SupportsRun: true,
        Parameters: new[] { Items })
    { Chapter = "Cap. 7 · TPL e Dataflow", Since = ".NET (Dataflow)" };

    // Estágio 1: transformação CPU-bound. Estágio 2: consumo CPU-bound.
    // O trabalho por item precisa ser não trivial para o pipeline paralelo
    // compensar o overhead de passagem de mensagens dos blocos.
    private static int Transform(int x) => (int)(Workloads.Spin(500) % 1000) + x;
    private static long Consume(int y) => (long)(Workloads.Spin(500)) + y;

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int items = args.Get(Items);
        return MeasureAsync("antipattern", rec =>
        {
            var mid = new int[items];
            for (int i = 0; i < items; i++) mid[i] = Transform(i); // estágio 1 inteiro
            long sink = 0;
            for (int i = 0; i < items; i++) sink += Consume(mid[i]); // só então o estágio 2

            rec.Log($"Processou {items:N0} itens em 2 estágios sequenciais, 1 thread");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"{items:N0} itens em estágios sequenciais e single-thread",
                Metrics: new[]
                {
                    new MetricItem("Itens", items.ToString("N0")),
                    new MetricItem("Estágios", "sequenciais", "Sem sobreposição"),
                    new MetricItem("Paralelismo", "1"),
                }));
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int items = args.Get(Items);
        return await MeasureAsync("pattern", async rec =>
        {
            long sink = 0;
            int dop = Environment.ProcessorCount;
            var transform = new TransformBlock<int, int>(
                Transform,
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = dop,
                    BoundedCapacity = 1024,
                    CancellationToken = ct,
                });
            var consume = new ActionBlock<int>(
                y => { Interlocked.Add(ref sink, Consume(y)); },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = dop,
                    BoundedCapacity = 1024,
                    CancellationToken = ct,
                });
            transform.LinkTo(consume, new DataflowLinkOptions { PropagateCompletion = true });

            for (int i = 0; i < items; i++)
                await transform.SendAsync(i, ct); // aguarda se o buffer encher (back-pressure)
            transform.Complete();
            await consume.Completion;

            rec.Log($"Processou {items:N0} itens num pipeline Dataflow paralelo (DOP {dop})");
            return new VariantOutcome(
                Ok: true,
                Headline: $"{items:N0} itens num pipeline sobreposto e paralelo (DOP {dop})",
                Metrics: new[]
                {
                    new MetricItem("Itens", items.ToString("N0")),
                    new MetricItem("Estágios", "sobrepostos", "Pipeline"),
                    new MetricItem("Paralelismo", dop.ToString()),
                });
        });
    }
}
