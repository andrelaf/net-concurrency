namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Um método async em caminho quente que quase sempre completa de forma síncrona
/// (cache hit) aloca uma <c>Task&lt;T&gt;</c> por chamada. <c>ValueTask&lt;T&gt;</c>
/// evita a alocação no caminho síncrono.
/// </summary>
public sealed class ValueTaskDemo : DemoBase
{
    private static readonly DemoParameter Calls =
        new("calls", "Chamadas (milhares)", 500, 50, 5000, 50,
            "Todas em cache hit (síncronas)");

    public override DemoInfo Info { get; } = new(
        Id: "task-vs-valuetask-hotpath",
        Title: "Task<T> vs ValueTask<T> em caminho quente",
        Category: "Fundamentos",
        Summary: "Em caminhos que quase sempre completam síncronos, ValueTask<T> evita alocar por chamada.",
        AntipatternCode:
            """
            // ❌ Retornar Task<T> aloca um objeto Task por chamada, mesmo quando
            // o valor já está em cache e não houve espera nenhuma. Em caminhos
            // muito quentes, é pressão de GC pura.
            Task<int> GetAsync(int key)
            {
                if (_cache.TryGetValue(key, out var v))
                    return Task.FromResult(v);   // aloca Task<int> a cada hit
                return LoadAsync(key);
            }
            """,
        AntipatternExplanation:
            "Quando a operação normalmente termina de forma síncrona (cache hit), devolver `Task<T>` " +
            "ainda aloca um objeto no heap por chamada. Em caminhos chamados milhões de vezes, isso " +
            "vira pressão de GC significativa sem nenhuma espera real acontecendo.",
        PatternCode:
            """
            // ✅ ValueTask<T> embrulha o resultado síncrono SEM alocar. Só cai
            // para uma Task quando realmente precisa aguardar (cache miss).
            ValueTask<int> GetAsync(int key)
            {
                if (_cache.TryGetValue(key, out var v))
                    return new ValueTask<int>(v);   // zero alocação no hit
                return new ValueTask<int>(LoadAsync(key));
            }

            // Regras: consuma uma ValueTask só uma vez, e não a aguarde
            // várias vezes nem em paralelo.
            """,
        PatternExplanation:
            "`ValueTask<T>` é um struct que carrega o resultado direto quando ele já existe, sem tocar " +
            "no heap. Ideal para APIs muito quentes com predominância de conclusão síncrona. O preço é " +
            "disciplina de uso: consuma-a uma única vez (nada de `await` repetido ou paralelo).",
        KeyTakeaways: new[]
        {
            "Em caminhos quentes com conclusão síncrona frequente, ValueTask<T> evita alocar.",
            "Não é um substituto geral de Task: consuma a ValueTask só uma vez.",
            "Para o caso assíncrono real, ValueTask apenas embrulha a Task subjacente.",
        },
        SupportsRun: true,
        Parameters: new[] { Calls })
    { Chapter = null, Since = ".NET Core 2.1" };

    private static readonly int[] Cache = Enumerable.Range(0, 1024).ToArray();

    private static Task<int> GetTask(int key) => Task.FromResult(Cache[key & 1023]);
    private static ValueTask<int> GetValueTask(int key) => new(Cache[key & 1023]);

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        long calls = (long)args.Get(Calls) * 1000;
        return MeasureAsync("antipattern", rec =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long sum = 0;
            for (long i = 0; i < calls; i++)
                sum += GetTask((int)i).Result; // Task já concluída; .Result não bloqueia
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            double perCall = (double)allocated / calls;
            rec.Log($"{calls:N0} chamadas Task<int> alocaram {allocated:N0} bytes (~{perCall:0} B/chamada)");
            return Task.FromResult(new VariantOutcome(
                Ok: false,
                Headline: $"Alocou {allocated / 1024:N0} KB em {calls:N0} chamadas (~{perCall:0} B cada)",
                Metrics: new[]
                {
                    new MetricItem("Alocado", $"{allocated / 1024:N0} KB", "Pressão de GC"),
                    new MetricItem("Por chamada", $"~{perCall:0} B"),
                    new MetricItem("Tipo", "Task<int>"),
                }));
        });
    }

    public override Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        long calls = (long)args.Get(Calls) * 1000;
        return MeasureAsync("pattern", rec =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long sum = 0;
            for (long i = 0; i < calls; i++)
                sum += GetValueTask((int)i).Result; // struct; sem alocação
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            double perCall = (double)allocated / calls;
            rec.Log($"{calls:N0} chamadas ValueTask<int> alocaram {allocated:N0} bytes (~{perCall:0.00} B/chamada)");
            return Task.FromResult(new VariantOutcome(
                Ok: true,
                Headline: $"Alocou {allocated / 1024:N0} KB em {calls:N0} chamadas — praticamente zero",
                Metrics: new[]
                {
                    new MetricItem("Alocado", $"{allocated / 1024:N0} KB", "Sem pressão"),
                    new MetricItem("Por chamada", $"~{perCall:0.00} B"),
                    new MetricItem("Tipo", "ValueTask<int>"),
                }));
        });
    }
}
