using System.Threading.Channels;

namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Processar estágios em passadas sequenciais (materializar cada etapa antes da
/// próxima) não sobrepõe trabalho nem paraleliza. Encadear <c>Channel&lt;T&gt;</c>
/// entre estágios cria um pipeline em que as etapas rodam concorrentes, cada uma
/// com seu grau de paralelismo e back-pressure.
/// </summary>
public sealed class ChannelPipelineDemo : DemoBase
{
    private static readonly DemoParameter Items =
        new("items", "Itens", 6_000, 1_000, 20_000, 1_000);

    public override DemoInfo Info { get; } = new(
        Id: "sequential-stages-vs-channel-pipeline",
        Title: "Passadas sequenciais vs pipeline de Channels",
        Category: "Pipelines e Padrões",
        Summary: "Encadear Channels sobrepõe estágios e paraleliza cada um; passadas sequenciais não.",
        AntipatternCode:
            """
            // ❌ Cada estágio processa TODA a coleção antes do próximo começar.
            // Sem sobreposição entre estágios e sem paralelismo; o intermediário
            // inteiro fica na memória.
            var a = items.Select(Stage1).ToArray();   // estágio 1 inteiro
            var b = a.Select(Stage2).ToArray();       // depois o 2
            long result = b.Sum(Stage3);              // depois o 3
            """,
        AntipatternExplanation:
            "Rodar os estágios em passadas separadas serializa o pipeline e mantém cada resultado " +
            "intermediário inteiro em memória. Enquanto o estágio 2 espera, os núcleos ficam ociosos.",
        PatternCode:
            """
            // ✅ Um Channel por fronteira de estágio. Cada estágio é uma (ou mais)
            // task lendo do canal de entrada e escrevendo no de saída. As etapas
            // rodam ao mesmo tempo; canais limitados dão back-pressure.
            var ch1 = Channel.CreateBounded<int>(1024);
            var ch2 = Channel.CreateBounded<int>(1024);

            var s1 = RunStage(ch1.Reader, ch2.Writer, Stage1, dop);  // N workers
            var s2 = ConsumeStage(ch2.Reader, Stage2AndAggregate);

            foreach (var x in items) await ch1.Writer.WriteAsync(x);
            ch1.Writer.Complete();
            await s1; ch2.Writer.Complete();
            await s2;
            """,
        PatternExplanation:
            "Encadear `Channel<T>` entre estágios monta um pipeline em que produção e consumo de cada " +
            "etapa se sobrepõem. Cada estágio pode ter vários workers (paralelismo) e cada canal " +
            "limitado impõe back-pressure. É a composição \"na mão\" (mais leve) equivalente ao TPL " +
            "Dataflow.",
        KeyTakeaways: new[]
        {
            "Encadeie um Channel por fronteira de estágio para sobrepor e paralelizar etapas.",
            "Canais limitados dão back-pressure; Complete() encadeia a conclusão entre estágios.",
            "É a alternativa leve ao TPL Dataflow para o mesmo formato de pipeline.",
        },
        SupportsRun: true,
        Parameters: new[] { Items })
    { Chapter = "Cap. 7 · TPL e Dataflow", Since = ".NET Core 3.0" };

    private static int Stage1(int x) => (int)(Workloads.Spin(300) % 997) + x;
    private static int Stage2(int x) => (int)(Workloads.Spin(300) % 991) + x;
    private static long Stage3(int x) => (long)(Workloads.Spin(300)) + x;

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int items = args.Get(Items);
        return MeasureAsync("antipattern", rec =>
        {
            var a = new int[items];
            for (int i = 0; i < items; i++) a[i] = Stage1(i);
            var b = new int[items];
            for (int i = 0; i < items; i++) b[i] = Stage2(a[i]);
            long sum = 0;
            for (int i = 0; i < items; i++) sum += Stage3(b[i]);

            rec.Log($"Processou {items:N0} itens em 3 passadas sequenciais, 1 thread");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"{items:N0} itens em 3 passadas sequenciais, sem sobreposição",
                Metrics: new[]
                {
                    new MetricItem("Itens", items.ToString("N0")),
                    new MetricItem("Estágios", "sequenciais"),
                    new MetricItem("Paralelismo", "1"),
                }));
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int items = args.Get(Items);
        int dop = Environment.ProcessorCount;
        return await MeasureAsync("pattern", async rec =>
        {
            var ch1 = Channel.CreateBounded<int>(1024);
            var ch2 = Channel.CreateBounded<int>(1024);
            long sum = 0;

            // Estágio 1: dop workers lendo ch1, aplicando Stage1, escrevendo ch2.
            var stage1 = Enumerable.Range(0, dop).Select(_ => Task.Run(async () =>
            {
                await foreach (var x in ch1.Reader.ReadAllAsync(ct))
                    await ch2.Writer.WriteAsync(Stage1(x), ct);
            }, ct)).ToArray();

            // Estágio 2 + agregação: dop workers lendo ch2.
            var stage2 = Enumerable.Range(0, dop).Select(_ => Task.Run(async () =>
            {
                await foreach (var x in ch2.Reader.ReadAllAsync(ct))
                    Interlocked.Add(ref sum, Stage3(Stage2(x)));
            }, ct)).ToArray();

            for (int i = 0; i < items; i++)
                await ch1.Writer.WriteAsync(i, ct);
            ch1.Writer.Complete();
            await Task.WhenAll(stage1);
            ch2.Writer.Complete();
            await Task.WhenAll(stage2);

            rec.Log($"Processou {items:N0} itens num pipeline de Channels (DOP {dop} por estágio)");
            return new VariantOutcome(
                Ok: true,
                Headline: $"{items:N0} itens num pipeline de Channels sobreposto (DOP {dop})",
                Metrics: new[]
                {
                    new MetricItem("Itens", items.ToString("N0")),
                    new MetricItem("Estágios", "sobrepostos"),
                    new MetricItem("Paralelismo", dop.ToString()),
                });
        });
    }
}
