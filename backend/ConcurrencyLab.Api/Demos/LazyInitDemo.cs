namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Inicialização preguiçosa "na mão" (checar-nulo-e-criar sem sincronização) deixa
/// várias threads criarem a instância ao mesmo tempo — não é mais singleton.
/// <c>Lazy&lt;T&gt;</c> garante exatamente uma criação, thread-safe.
/// </summary>
public sealed class LazyInitDemo : DemoBase
{
    private static readonly DemoParameter Workers =
        new("workers", "Threads no 1º acesso", 16, 2, 64, 1);
    private static readonly DemoParameter BuildMicros =
        new("buildMicros", "Custo de construção (µs)", 50, 0, 500, 10,
            "Alarga a janela da corrida");

    public override DemoInfo Info { get; } = new(
        Id: "lazy-vs-double-checked-locking",
        Title: "Inicialização preguiçosa: check-and-create vs Lazy<T>",
        Category: "Riscos",
        Summary: "Lazy init sem sincronização cria a instância várias vezes; Lazy<T> garante uma só.",
        AntipatternCode:
            """
            // ❌ 'if nulo então cria' sem sincronização. Sob concorrência, várias
            // threads passam do if antes de qualquer atribuição e TODAS criam a
            // instância. Deixa de ser singleton (e pode publicar estado
            // parcialmente construído). Double-checked locking "na mão" sem
            // volatile/barreira tem o mesmo problema.
            private Config? _instance;

            Config Get()
            {
                if (_instance == null)          // corrida aqui
                    _instance = new Config();   // criada N vezes
                return _instance;
            }
            """,
        AntipatternExplanation:
            "O `if (_instance == null)` não é atômico com a atribuição. Várias threads leem `null` " +
            "antes de qualquer uma escrever, e todas constroem a instância — quebrando o singleton e " +
            "arriscando publicar um objeto meio-construído. Double-checked locking feito à mão sem " +
            "`volatile` e barreiras corretas é igualmente frágil.",
        PatternCode:
            """
            // ✅ Lazy<T> faz a inicialização preguiçosa thread-safe. Por padrão
            // (ExecutionAndPublication) a fábrica roda UMA vez; as demais
            // threads esperam e recebem a mesma instância.
            private readonly Lazy<Config> _lazy = new(() => new Config());

            Config Get() => _lazy.Value;

            // Para campos estáticos, um static readonly ou LazyInitializer
            // também resolvem.
            """,
        PatternExplanation:
            "`Lazy<T>` (modo padrão `ExecutionAndPublication`) serializa a primeira criação: a fábrica " +
            "executa uma única vez e todas as threads recebem a mesma instância, publicada com as " +
            "barreiras de memória corretas. Simples e à prova de corrida.",
        KeyTakeaways: new[]
        {
            "Check-and-create sem lock cria a instância várias vezes sob concorrência.",
            "Lazy<T> garante uma única criação thread-safe (modo padrão).",
            "Evite double-checked locking na mão; prefira Lazy<T>/static readonly/LazyInitializer.",
        },
        SupportsRun: true,
        Parameters: new[] { Workers, BuildMicros })
    {
        Chapter = "Cap. 3 · Boas Práticas de Managed Threading", Since = ".NET 4.0",
        UseCases = new[]
        {
            "Inicialização cara e sob demanda de singletons (config, conexão, cliente HTTP).",
            "Recursos que só devem ser criados no primeiro uso, de forma thread-safe.",
            "Cache preguiçoso de valores calculados uma única vez.",
        },
    };

    // Roda 'body' em N threads de SO reais, todas liberadas juntas por uma barreira,
    // para maximizar a corrida sem depender da injeção lenta do thread pool.
    private static void RaceOnThreads(int workers, Action body, CancellationToken ct)
    {
        using var barrier = new Barrier(workers);
        var threads = new Thread[workers];
        for (int i = 0; i < workers; i++)
            threads[i] = new Thread(() => { barrier.SignalAndWait(ct); body(); }) { IsBackground = true };
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
    }

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        int micros = args.Get(BuildMicros);
        return MeasureAsync("antipattern", rec =>
        {
            int created = 0;
            object? instance = null;
            void Access()
            {
                if (instance == null) // corrida deliberada
                {
                    Workloads.Spin(micros * 40); // alarga a janela
                    instance = new object();
                    Interlocked.Increment(ref created);
                }
            }

            RaceOnThreads(workers, Access, ct);

            rec.Log($"{workers} threads no 1º acesso criaram a instância {created} vez(es)");
            return Task.FromResult(new VariantOutcome(
                Ok: created == 1,
                Headline: created > 1
                    ? $"Criou {created} instâncias — a corrida quebrou o singleton"
                    : "Uma instância nesta execução (não determinístico — aumente threads/custo)",
                Metrics: new[]
                {
                    new MetricItem("Instâncias", created.ToString(), "Deveria ser 1"),
                    new MetricItem("Threads", workers.ToString()),
                    new MetricItem("Sincronização", "nenhuma"),
                }));
        });
    }

    public override Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int workers = args.Get(Workers);
        int micros = args.Get(BuildMicros);
        return MeasureAsync("pattern", rec =>
        {
            int created = 0;
            var lazy = new Lazy<object>(() =>
            {
                Workloads.Spin(micros * 40);
                Interlocked.Increment(ref created);
                return new object();
            });

            RaceOnThreads(workers, () => GC.KeepAlive(lazy.Value), ct); // fábrica roda uma vez

            rec.Log($"{workers} threads no 1º acesso criaram a instância {created} vez(es)");
            return Task.FromResult(new VariantOutcome(
                Ok: created == 1,
                Headline: $"Criou exatamente {created} instância com {workers} threads concorrentes",
                Metrics: new[]
                {
                    new MetricItem("Instâncias", created.ToString(), "Exatamente 1"),
                    new MetricItem("Threads", workers.ToString()),
                    new MetricItem("Sincronização", "Lazy<T>"),
                }));
        });
    }
}
