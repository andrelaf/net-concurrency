namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Duas threads pegam dois locks em ordem oposta e travam em deadlock. A correção
/// é uma ordem global consistente de aquisição de locks. Para manter o servidor
/// seguro, detectamos o deadlock com um timeout limitado em <c>Monitor.TryEnter</c>
/// em vez de travar de vez.
/// </summary>
public sealed class DeadlockDemo : DemoBase
{
    private static readonly DemoParameter HoldMs =
        new("holdMs", "Tempo segurando antes do 2º lock (ms)", 50, 10, 300, 10,
            "Valores maiores alargam a janela do deadlock");

    public override DemoInfo Info { get; } = new(
        Id: "deadlock-lock-ordering",
        Title: "Deadlock por ordem inconsistente de locks",
        Category: "Riscos",
        Summary: "Duas threads pegando locks A→B e B→A podem esperar uma pela outra para sempre.",
        AntipatternCode:
            """
            // ❌ Thread 1 trava A depois B; Thread 2 trava B depois A.
            // Se ambas pegam o primeiro lock e então esperam pelo segundo,
            // nenhuma avança — um deadlock clássico (trava, não quebra).
            void Transfer(Account from, Account to, decimal amount)
            {
                lock (from.Sync)
                {
                    Thread.Sleep(hold);          // alarga a janela da corrida
                    lock (to.Sync)               // pode travar para sempre
                    {
                        from.Balance -= amount;
                        to.Balance   += amount;
                    }
                }
            }
            // transfer(A, B) e transfer(B, A) ao mesmo tempo -> deadlock
            """,
        AntipatternExplanation:
            "Cada thread segura um lock e espera pelo outro. Não há timeout, não há exceção — as " +
            "threads ficam presas e as requisições nunca terminam. Em produção isso aparece como um " +
            "endpoint travado e a contagem de threads subindo.",
        PatternCode:
            """
            // ✅ Sempre adquira os locks em uma ordem global consistente.
            // Aqui ordenamos por um Id estável, então toda thread pega o
            // lock de menor Id primeiro e o ciclo nunca se forma.
            void Transfer(Account from, Account to, decimal amount)
            {
                var (first, second) = from.Id < to.Id ? (from, to) : (to, from);
                lock (first.Sync)
                lock (second.Sync)
                {
                    from.Balance -= amount;
                    to.Balance   += amount;
                }
            }
            """,
        PatternExplanation:
            "Impor uma ordem total na aquisição de locks remove a condição de espera circular, que é " +
            "uma das quatro condições de Coffman necessárias para o deadlock. Alternativas: um único " +
            "lock grosso, estruturas lock-free, ou `Monitor.TryEnter` com timeout.",
        KeyTakeaways: new[]
        {
            "Deadlock precisa de espera circular — quebre-a com ordem consistente de locks.",
            "Nunca segure um lock enquanto adquire outro em ordem imprevisível.",
            "Use `Monitor.TryEnter(timeout)` para que um ciclo de locks falhe rápido em vez de travar.",
        },
        SupportsRun: true,
        Parameters: new[] { HoldMs })
    {
        Chapter = "Cap. 3 · Boas Práticas de Managed Threading", Since = ".NET 1.0",
        UseCases = new[]
        {
            "Sempre que uma operação adquire 2+ locks (ex.: transferência entre contas).",
            "Ordenar a aquisição por um id estável em qualquer código com múltiplos locks.",
            "Diagnosticar um endpoint que 'trava' com contagem de threads subindo.",
        },
    };

    private sealed class Account(int id)
    {
        public int Id { get; } = id;
        public object Sync { get; } = new();
    }

    public override async Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int hold = args.Get(HoldMs);
        return await MeasureAsync("antipattern", async rec =>
        {
            var a = new Account(1);
            var b = new Account(2);
            const int timeoutMs = 1500;
            bool deadlocked = false;

            // TryEnter com timeout nos deixa *demonstrar* o ciclo sem travar a
            // thread do servidor: um timeout significa que o deadlock ocorreu.
            var t1 = Task.Run(() => TakeInOrder(a, b, hold, timeoutMs, rec, "T1", ref deadlocked), ct);
            var t2 = Task.Run(() => TakeInOrder(b, a, hold, timeoutMs, rec, "T2", ref deadlocked), ct);
            await Task.WhenAll(t1, t2);

            return new VariantOutcome(
                Ok: !deadlocked,
                Headline: deadlocked
                    ? "Deadlock reproduzido — uma thread estourou o timeout esperando o 2º lock"
                    : "Sem deadlock nesta execução (aumente o tempo de espera para reproduzir com confiança)",
                Metrics: new[]
                {
                    new MetricItem("Ordem dos locks", "A→B e B→A", "Inconsistente"),
                    new MetricItem("Resultado", deadlocked ? "Deadlock" : "Concluído"),
                    new MetricItem("Detecção", $"timeout TryEnter {timeoutMs} ms"),
                });
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int hold = args.Get(HoldMs);
        return await MeasureAsync("pattern", async rec =>
        {
            var a = new Account(1);
            var b = new Account(2);

            var t1 = Task.Run(() => TakeOrdered(a, b, hold, rec, "T1"), ct);
            var t2 = Task.Run(() => TakeOrdered(b, a, hold, rec, "T2"), ct);
            await Task.WhenAll(t1, t2);

            return new VariantOutcome(
                Ok: true,
                Headline: "Ambas as threads concluíram — a ordem consistente evita o ciclo",
                Metrics: new[]
                {
                    new MetricItem("Ordem dos locks", "menor Id primeiro", "Consistente"),
                    new MetricItem("Resultado", "Concluído"),
                    new MetricItem("Deadlocks", "0"),
                });
        });
    }

    // Auxiliar do antipadrão: trava na ordem dada; um timeout sinaliza deadlock.
    private static void TakeInOrder(Account first, Account second, int hold, int timeoutMs,
        Recorder rec, string name, ref bool deadlocked)
    {
        bool got1 = false, got2 = false;
        try
        {
            Monitor.TryEnter(first.Sync, timeoutMs, ref got1);
            if (!got1) { rec.Log($"{name}: não conseguiu pegar o lock {first.Id}"); Volatile.Write(ref deadlocked, true); return; }
            rec.Log($"{name}: segura o lock {first.Id}, esperando por {second.Id}");
            Thread.Sleep(hold);

            Monitor.TryEnter(second.Sync, timeoutMs, ref got2);
            if (!got2)
            {
                rec.Log($"{name}: TIMEOUT esperando o lock {second.Id} → deadlock");
                Volatile.Write(ref deadlocked, true);
                return;
            }
            rec.Log($"{name}: adquiriu os dois locks, concluído");
        }
        finally
        {
            if (got2) Monitor.Exit(second.Sync);
            if (got1) Monitor.Exit(first.Sync);
        }
    }

    // Auxiliar do padrão: sempre trava a conta de menor Id primeiro.
    private static void TakeOrdered(Account x, Account y, int hold, Recorder rec, string name)
    {
        var (first, second) = x.Id < y.Id ? (x, y) : (y, x);
        lock (first.Sync)
        {
            rec.Log($"{name}: segura o lock {first.Id} (ordenado), esperando por {second.Id}");
            Thread.Sleep(hold);
            lock (second.Sync)
            {
                rec.Log($"{name}: adquiriu os dois locks em ordem, concluído");
            }
        }
    }
}
