using System.Runtime.CompilerServices;

namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Retornar <c>Task&lt;List&lt;T&gt;&gt;</c> bufferiza tudo antes de devolver: o
/// consumidor espera o último item e segura a coleção inteira na memória.
/// <c>IAsyncEnumerable&lt;T&gt;</c> transmite item a item — o consumo se sobrepõe à
/// produção e só um item fica em voo.
/// </summary>
public sealed class AsyncStreamsDemo : DemoBase
{
    private static readonly DemoParameter Items =
        new("items", "Itens", 40, 10, 200, 10);
    private static readonly DemoParameter PerItemMs =
        new("perItemMs", "Produção/consumo por item (ms)", 10, 1, 40, 1);

    public override DemoInfo Info { get; } = new(
        Id: "task-list-vs-iasyncenumerable",
        Title: "Task<List<T>> vs IAsyncEnumerable<T> (async streams)",
        Category: "Coordenação Async",
        Summary: "Streaming async processa conforme chega; bufferizar tudo atrasa e gasta memória.",
        AntipatternCode:
            """
            // ❌ Junta TODOS os itens numa lista antes de retornar. O consumidor
            // não vê nada até o último chegar, e a coleção inteira fica na
            // memória. Produção e consumo não se sobrepõem.
            async Task<List<Item>> FetchAllAsync()
            {
                var all = new List<Item>();
                foreach (var id in ids)
                    all.Add(await FetchAsync(id));   // espera todos
                return all;
            }

            var all = await FetchAllAsync();
            foreach (var item in all) Process(item);  // só então processa
            """,
        AntipatternExplanation:
            "Materializar `Task<List<T>>` obriga o chamador a esperar o item mais lento e a manter a " +
            "coleção toda em memória. Produção e consumo ficam em fases separadas, sem sobreposição.",
        PatternCode:
            """
            // ✅ Um iterador async transmite cada item assim que fica pronto.
            // O consumo com 'await foreach' se sobrepõe à produção; só um item
            // fica em voo. [EnumeratorCancellation] propaga o token.
            async IAsyncEnumerable<Item> StreamAsync(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                foreach (var id in ids)
                    yield return await FetchAsync(id, ct);
            }

            await foreach (var item in StreamAsync(ct))
                Process(item);                        // processa na hora
            """,
        PatternExplanation:
            "`IAsyncEnumerable<T>` com `yield return` produz sob demanda. O `await foreach` consome " +
            "cada item na chegada, sobrepondo produção e consumo e mantendo o pico de memória em um " +
            "item. `[EnumeratorCancellation]` liga o `CancellationToken` do `WithCancellation`.",
        KeyTakeaways: new[]
        {
            "IAsyncEnumerable<T> transmite; Task<List<T>> bufferiza tudo antes de entregar.",
            "Streaming sobrepõe produção e consumo — menor latência ao 1º item e menos memória.",
            "Use [EnumeratorCancellation] no parâmetro CancellationToken do iterador async.",
        },
        SupportsRun: true,
        Parameters: new[] { Items, PerItemMs })
    {
        Chapter = "Cap. 5 · Programação Assíncrona com C#", Since = ".NET Core 3.0",
        UseCases = new[]
        {
            "Paginação/streaming de BD ou API (entregar resultados conforme lê as páginas).",
            "Processar arquivos grandes linha a linha sem carregar tudo na memória.",
            "Server-Sent Events, gRPC streaming e leitura de tópicos (Kafka) contínua.",
        },
    };

    private static async Task<int> FetchAsync(int id, int ms, CancellationToken ct)
    {
        await Task.Delay(ms, ct);
        return id;
    }

    private static async IAsyncEnumerable<int> StreamAsync(
        int items, int ms, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int i = 0; i < items; i++)
            yield return await FetchAsync(i, ms, ct);
    }

    private static void Consume(int ms) => Workloads.Spin(ms * 8_000);

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int items = args.Get(Items);
        int ms = args.Get(PerItemMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var all = new List<int>(items);
            for (int i = 0; i < items; i++)
                all.Add(await FetchAsync(i, ms, ct)); // bufferiza tudo
            long firstMs = sw.ElapsedMilliseconds; // só agora dá pra processar o 1º
            foreach (var x in all) Consume(ms);

            rec.Log($"Bufferizou {items} itens; só então processou. 1º item aos {firstMs} ms");
            return new VariantOutcome(
                Ok: false,
                Headline: $"Esperou os {items} itens (1º processável só aos {firstMs} ms) e segurou tudo na memória",
                Metrics: new[]
                {
                    new MetricItem("1º item", $"{firstMs} ms", "Tarde"),
                    new MetricItem("Pico em memória", items.ToString(), "Coleção inteira"),
                    new MetricItem("Fases", "produz, depois consome"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int items = args.Get(Items);
        int ms = args.Get(PerItemMs);
        return await MeasureAsync("pattern", async rec =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long firstMs = -1;
            int processed = 0;
            await foreach (var x in StreamAsync(items, ms, ct).WithCancellation(ct))
            {
                if (firstMs < 0) firstMs = sw.ElapsedMilliseconds;
                Consume(ms); // consome sobrepondo à produção
                processed++;
            }
            rec.Log($"Transmitiu {processed} itens; 1º processado aos {firstMs} ms (streaming)");
            return new VariantOutcome(
                Ok: true,
                Headline: $"Processou o 1º item aos {firstMs} ms, sobrepondo produção e consumo",
                Metrics: new[]
                {
                    new MetricItem("1º item", $"{firstMs} ms", "Cedo"),
                    new MetricItem("Pico em memória", "1", "Um item em voo"),
                    new MetricItem("Fases", "produz e consome juntos"),
                });
        });
    }
}
