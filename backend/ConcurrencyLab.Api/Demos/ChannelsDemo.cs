namespace ConcurrencyLab.Api.Demos;

using System.Threading.Channels;

/// <summary>
/// Uma List compartilhada protegida por lock para o hand-off produtor/consumidor
/// é sujeita a corrida e não tem back-pressure. System.Threading.Channels é a
/// ferramenta feita para isso.
/// </summary>
public sealed class ChannelsDemo : DemoBase
{
    private static readonly DemoParameter Producers =
        new("producers", "Produtores", 4, 1, 16, 1);
    private static readonly DemoParameter Items =
        new("items", "Itens por produtor", 25_000, 1_000, 200_000, 1_000);

    public override DemoInfo Info { get; } = new(
        Id: "channels-producer-consumer",
        Title: "Fila com lock vs System.Threading.Channels",
        Category: "Coleções e Mensageria",
        Summary: "Channels dão hand-off produtor/consumidor seguro, async e com back-pressure.",
        AntipatternCode:
            """
            // ❌ Fila improvisada: uma List + lock + loop de polling.
            // Fácil de errar: o busy-wait queima CPU, não há back-pressure,
            // e sinalizar a conclusão é chato e sujeito a corrida.
            var buffer = new List<int>();
            var done = false;

            // produtor
            lock (buffer) { buffer.Add(item); }

            // consumidor
            while (!done || HasItems(buffer))
            {
                int? item = null;
                lock (buffer)
                    if (buffer.Count > 0) { item = buffer[0]; buffer.RemoveAt(0); }
                if (item is null) Thread.Sleep(1);   // espera ocupada
                else Process(item.Value);
            }
            """,
        AntipatternExplanation:
            "Fazer uma fila concorrente na mão significa reinventar sinalização e back-pressure. " +
            "`List.RemoveAt(0)` é O(n), o loop de polling desperdiça CPU, e o 'já terminamos?' é uma " +
            "fonte notória de corridas e itens perdidos.",
        PatternCode:
            """
            // ✅ Channels: uma fila async e thread-safe com limites opcionais
            // (back-pressure) e sinalização de conclusão de primeira classe.
            var channel = Channel.CreateBounded<int>(capacity: 1000);

            // produtores
            await channel.Writer.WriteAsync(item);      // aguarda se estiver cheio
            channel.Writer.Complete();                  // sinaliza conclusão

            // consumidor(es)
            await foreach (var item in channel.Reader.ReadAllAsync())
                Process(item);
            """,
        PatternExplanation:
            "`Channel<T>` é a primitiva moderna de produtor/consumidor: caminhos rápidos lock-free, " +
            "consumo com `await foreach`, `Complete()` para shutdown limpo, e capacidade limitada " +
            "para back-pressure, de modo que produtores rápidos não esgotem a memória.",
        KeyTakeaways: new[]
        {
            "Não faça filas concorrentes na mão — use Channels.",
            "Channels limitados dão back-pressure; os ilimitados podem estourar a memória.",
            "Writer.Complete() + ReadAllAsync() tornam a conclusão livre de corrida.",
        },
        SupportsRun: true,
        Parameters: new[] { Producers, Items })
    { Chapter = "Cap. 7 · TPL e Dataflow", Since = ".NET Core 3.0" };

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int producers = args.Get(Producers);
        int items = args.Get(Items);
        long expected = (long)producers * items;
        return await MeasureAsync("antipattern", async rec =>
        {
            var buffer = new Queue<int>();
            int producedDone = 0;
            long consumed = 0;
            int peakBuffer = 0; // ilimitado: cresce conforme produtores superam o consumidor

            var produceTasks = Enumerable.Range(0, producers).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < items; i++)
                    lock (buffer)
                    {
                        buffer.Enqueue(i);
                        if (buffer.Count > peakBuffer) peakBuffer = buffer.Count; // sem teto
                    }
                Interlocked.Increment(ref producedDone);
            }, ct)).ToArray();

            var consumer = Task.Run(() =>
            {
                while (Volatile.Read(ref producedDone) < producers || CountLocked(buffer) > 0)
                {
                    int? item = null;
                    lock (buffer)
                        if (buffer.Count > 0) item = buffer.Dequeue();
                    if (item is null) Thread.Sleep(1); // polling ocupado
                    else consumed++;
                }
            }, ct);

            await Task.WhenAll(produceTasks);
            await consumer;

            rec.Log($"Produziu {expected:N0}, consumiu {consumed:N0} via lock+poll");
            rec.Log($"A fila chegou ao pico de {peakBuffer:N0} itens — sem back-pressure para frear produtores");
            return new VariantOutcome(
                Ok: consumed == expected,
                Headline: $"Consumiu {consumed:N0}/{expected:N0}, mas a fila ilimitada inflou para {peakBuffer:N0} itens",
                Metrics: new[]
                {
                    new MetricItem("Consumidos", consumed.ToString("N0")),
                    new MetricItem("Pico da fila", peakBuffer.ToString("N0"), "Ilimitada — risco de memória"),
                    new MetricItem("Sincronização", "lock + polling Thread.Sleep", "Espera ocupada"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int producers = args.Get(Producers);
        int items = args.Get(Items);
        long expected = (long)producers * items;
        return await MeasureAsync("pattern", async rec =>
        {
            var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1000)
            {
                SingleReader = true,
                SingleWriter = false,
            });
            long consumed = 0;

            var produceTasks = Enumerable.Range(0, producers).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < items; i++)
                    await channel.Writer.WriteAsync(i, ct);
            }, ct)).ToArray();

            var completion = Task.Run(async () =>
            {
                await Task.WhenAll(produceTasks);
                channel.Writer.Complete();
            }, ct);

            await foreach (var _ in channel.Reader.ReadAllAsync(ct))
                consumed++;
            await completion;

            rec.Log($"Produziu {expected:N0}, consumiu {consumed:N0} via Channel limitado");
            rec.Log("Fila limitada a 1000 — WriteAsync aguarda quando cheia (back-pressure)");
            return new VariantOutcome(
                Ok: consumed == expected,
                Headline: $"Consumiu {consumed:N0}/{expected:N0} com a fila limitada a 1000 por back-pressure",
                Metrics: new[]
                {
                    new MetricItem("Consumidos", consumed.ToString("N0")),
                    new MetricItem("Pico da fila", "≤ 1.000", "Limitada"),
                    new MetricItem("Sincronização", "Channel (limitado 1000)", "Back-pressure"),
                });
        });
    }

    private static int CountLocked(Queue<int> q)
    {
        lock (q) return q.Count;
    }
}
