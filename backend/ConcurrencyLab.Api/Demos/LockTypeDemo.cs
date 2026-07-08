namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Travar em um <c>object</c> qualquer usa Monitor e permite erros (travar em
/// <c>this</c>/strings, Pulse acidental). O tipo dedicado <c>System.Threading.Lock</c>
/// (.NET 9) é mais rápido, seguro por design e integrado ao keyword <c>lock</c>.
/// </summary>
public sealed class LockTypeDemo : DemoBase
{
    private static readonly DemoParameter Workers =
        new("workers", "Threads concorrentes", 8, 2, 32, 1);
    private static readonly DemoParameter PerWorker =
        new("perWorker", "Seções críticas por thread", 500_000, 50_000, 3_000_000, 50_000);

    public override DemoInfo Info { get; } = new(
        Id: "object-lock-vs-lock-type",
        Title: "lock(object) vs System.Threading.Lock",
        Category: "Fundamentos",
        Summary: "O novo tipo Lock (.NET 9) é o primitivo dedicado — mais seguro e geralmente mais rápido.",
        AntipatternCode:
            """
            // ❌ Travar em um 'object' cru usa Monitor. Funciona, mas:
            //  - dá pra travar em 'this', em uma string internada ou em algo
            //    público por engano (deadlocks difíceis),
            //  - nada impede um Monitor.Pulse/Wait acidental no mesmo objeto,
            //  - o JIT não sabe que o objeto é "só um lock".
            private readonly object _sync = new();

            void Add(int x)
            {
                lock (_sync) { _total += x; }
            }
            """,
        AntipatternExplanation:
            "`lock (objeto)` compila para `Monitor.Enter/Exit`. É a forma histórica e continua " +
            "correta, mas o objeto é um `object` genérico: nada comunica a intenção nem impede usos " +
            "indevidos (travar em `this`, em uma string, chamar `Monitor.Pulse`).",
        PatternCode:
            """
            // ✅ System.Threading.Lock (.NET 9) é um tipo dedicado a locking.
            // O keyword 'lock' o reconhece (C# 13) e usa o caminho rápido dele;
            // ou use EnterScope() explicitamente. Mais rápido e à prova de erros.
            private readonly Lock _gate = new();

            void Add(int x)
            {
                lock (_gate) { _total += x; }       // usa Lock.EnterScope()
            }

            // equivalente explícito:
            // using (_gate.EnterScope()) { _total += x; }
            """,
        PatternExplanation:
            "`System.Threading.Lock` é o primitivo de exclusão mútua de primeira classe do .NET 9. O " +
            "compilador C# 13 reconhece o tipo e emite `EnterScope()` em vez de `Monitor`, com um " +
            "caminho rápido mais eficiente. Como é um tipo próprio, você não trava por engano em algo " +
            "público nem confunde com sinalização.",
        KeyTakeaways: new[]
        {
            "Prefira System.Threading.Lock (.NET 9) a travar em um object cru.",
            "O keyword 'lock' reconhece o tipo Lock e usa EnterScope() — mais rápido.",
            "Nunca trave em 'this', em tipos públicos ou em strings internadas.",
        },
        SupportsRun: true,
        Parameters: new[] { Workers, PerWorker })
    { Chapter = "Cap. 3 · Boas Práticas de Managed Threading", Since = ".NET 9" };

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        int perWorker = args.Get(PerWorker);
        return await MeasureAsync("antipattern", async rec =>
        {
            object sync = new();
            long total = 0;
            await Parallel.ForEachAsync(Enumerable.Range(0, workers), ct, (w, _) =>
            {
                for (int i = 0; i < perWorker; i++)
                    lock (sync) total += 1;
                return ValueTask.CompletedTask;
            });
            long expected = (long)workers * perWorker;
            rec.Log($"Total {total:N0} (esperado {expected:N0}) via lock(object)/Monitor");
            return new VariantOutcome(
                Ok: total == expected,
                Headline: $"{total:N0} seções críticas via lock(object) — Monitor, genérico",
                Metrics: new[]
                {
                    new MetricItem("Total", total.ToString("N0")),
                    new MetricItem("Primitivo", "object + Monitor", "Genérico"),
                    new MetricItem("Seguro por tipo", "Não"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        int perWorker = args.Get(PerWorker);
        return await MeasureAsync("pattern", async rec =>
        {
            var gate = new Lock();
            long total = 0;
            await Parallel.ForEachAsync(Enumerable.Range(0, workers), ct, (w, _) =>
            {
                for (int i = 0; i < perWorker; i++)
                    lock (gate) total += 1; // usa Lock.EnterScope()
                return ValueTask.CompletedTask;
            });
            long expected = (long)workers * perWorker;
            rec.Log($"Total {total:N0} (esperado {expected:N0}) via System.Threading.Lock");
            return new VariantOutcome(
                Ok: total == expected,
                Headline: $"{total:N0} seções críticas via System.Threading.Lock — dedicado",
                Metrics: new[]
                {
                    new MetricItem("Total", total.ToString("N0")),
                    new MetricItem("Primitivo", "Lock (.NET 9)", "Dedicado"),
                    new MetricItem("Seguro por tipo", "Sim"),
                });
        });
    }
}
