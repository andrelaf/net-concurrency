namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Um Dictionary comum mutado por várias threads corrompe seus buckets internos —
/// lançando exceção ou perdendo dados. ConcurrentDictionary foi feito para isso.
/// </summary>
public sealed class ConcurrentDictionaryDemo : DemoBase
{
    private static readonly DemoParameter Workers =
        new("workers", "Escritores concorrentes", 8, 2, 32, 1);
    private static readonly DemoParameter Keys =
        new("keys", "Chaves distintas por escritor", 5_000, 500, 50_000, 500);

    public override DemoInfo Info { get; } = new(
        Id: "dictionary-vs-concurrentdictionary",
        Title: "Dictionary vs ConcurrentDictionary sob contenção",
        Category: "Coleções e Mensageria",
        Summary: "Mutar uma coleção não thread-safe a partir de várias threads a corrompe.",
        AntipatternCode:
            """
            // ❌ Dictionary<TKey,TValue> não é thread-safe para escritas.
            // Add concorrente pode corromper o array de buckets — você recebe
            // IndexOutOfRangeException, Count errado, ou um loop infinito.
            var map = new Dictionary<int, int>();
            Parallel.For(0, workers, w =>
            {
                for (int k = 0; k < keys; k++)
                    map[w * keys + k] = k;     // escritas rasgadas / corrupção
            });
            """,
        AntipatternExplanation:
            "`Dictionary<,>` permite um escritor ou muitos leitores — nunca escritores concorrentes. " +
            "Sob contenção sua lógica de resize entra em corrida, lançando exceções ou corrompendo o " +
            "estado silenciosamente. Envolvê-lo em um `lock` simples funciona, mas serializa tudo.",
        PatternCode:
            """
            // ✅ ConcurrentDictionary usa locking listrado (striped) fino
            // (e leituras lock-free). Seguro para muitos escritores concorrentes.
            var map = new ConcurrentDictionary<int, int>();
            Parallel.For(0, workers, w =>
            {
                for (int k = 0; k < keys; k++)
                    map[w * keys + k] = k;     // thread-safe
            });
            // Ops compostas atômicas: AddOrUpdate / GetOrAdd
            map.AddOrUpdate(key, 1, (_, old) => old + 1);
            """,
        PatternExplanation:
            "`ConcurrentDictionary` fragmenta seus buckets entre vários locks, então escritores em " +
            "fragmentos diferentes não contendem, e as leituras são lock-free. Use " +
            "`AddOrUpdate`/`GetOrAdd` para ler-modificar-escrever atômico, em vez de um get-então-set " +
            "sujeito a corrida.",
        KeyTakeaways: new[]
        {
            "Dictionary/List/HashSet não são seguros para escritores concorrentes.",
            "Recorra aos tipos de System.Collections.Concurrent sob contenção.",
            "Use AddOrUpdate/GetOrAdd para atualizações compostas atômicas.",
        },
        SupportsRun: true,
        Parameters: new[] { Workers, Keys })
    {
        Chapter = "Cap. 9 · Coleções Concorrentes no .NET", Since = ".NET 4.0",
        UseCases = new[]
        {
            "Cache em memória compartilhado entre requisições (memoização, lookup).",
            "Contadores/agregações por chave sob concorrência (ex.: métricas por rota).",
            "Registro de conexões/sessões ativas atualizado por várias threads.",
        },
    };

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        int keys = args.Get(Keys);
        long expected = (long)workers * keys;
        return MeasureAsync("antipattern", rec =>
        {
            var map = new Dictionary<int, int>();
            int exceptions = 0;
            try
            {
                Parallel.For(0, workers, new ParallelOptions { CancellationToken = ct }, w =>
                {
                    for (int k = 0; k < keys; k++)
                    {
                        try { map[w * keys + k] = k; }
                        catch { Interlocked.Increment(ref exceptions); }
                    }
                });
            }
            catch (AggregateException) { Interlocked.Increment(ref exceptions); }

            int actual = map.Count;
            rec.Log($"Esperava {expected:N0} entradas");
            rec.Log($"Obteve {actual:N0} entradas; capturou {exceptions:N0} exceções");
            bool corrupted = actual != expected || exceptions > 0;
            return Task.FromResult(new VariantOutcome(
                Ok: !corrupted,
                Headline: corrupted
                    ? $"Corrupção detectada — {actual:N0}/{expected:N0} entradas, {exceptions:N0} exceções"
                    : "Sem corrupção nesta execução (não determinístico — aumente escritores/chaves)",
                Metrics: new[]
                {
                    new MetricItem("Esperado", expected.ToString("N0")),
                    new MetricItem("Real", actual.ToString("N0")),
                    new MetricItem("Exceções", exceptions.ToString("N0"), "Escritas rasgadas"),
                }));
        });
    }

    public override Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        int keys = args.Get(Keys);
        long expected = (long)workers * keys;
        return MeasureAsync("pattern", rec =>
        {
            var map = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
            Parallel.For(0, workers, new ParallelOptions { CancellationToken = ct }, w =>
            {
                for (int k = 0; k < keys; k++)
                    map[w * keys + k] = k;
            });

            int actual = map.Count;
            rec.Log($"Esperava {expected:N0} entradas");
            rec.Log($"Obteve {actual:N0} entradas; 0 exceções");
            return Task.FromResult(new VariantOutcome(
                Ok: actual == expected,
                Headline: $"Todas as {actual:N0} entradas escritas com segurança — sem corrupção",
                Metrics: new[]
                {
                    new MetricItem("Esperado", expected.ToString("N0")),
                    new MetricItem("Real", actual.ToString("N0")),
                    new MetricItem("Exceções", "0", "Thread-safe"),
                }));
        });
    }
}
