namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Apenas ilustrativa: bloquear em código async (.Result/.Wait()) arrisca
/// deadlocks sob um synchronization context e esgota o thread pool. Difícil de
/// reproduzir deterministicamente em um servidor, então é apresentada como ficha.
/// </summary>
public sealed class SyncOverAsyncDemo : DemoBase
{
    public override DemoInfo Info { get; } = new(
        Id: "sync-over-async",
        Title: "Sync-over-async (.Result / .Wait())",
        Category: "Riscos",
        Summary: "Bloquear em código async pode dar deadlock e esgota o thread pool.",
        AntipatternCode:
            """
            // ❌ Bloquear em uma Task com .Result ou .Wait().
            // 1) Sob um SynchronizationContext de thread única (ASP.NET clássico,
            //    WPF, WinForms) a continuação precisa da thread que você está
            //    bloqueando -> deadlock clássico.
            // 2) Mesmo sem context, cada chamada bloqueada prende uma thread do
            //    thread pool sem fazer nada, esgotando o pool sob carga.
            public string GetData()
            {
                return GetDataAsync().Result;      // pode dar deadlock / esgotar
            }

            public void Save()
            {
                SaveAsync().Wait();                // mesmo problema
            }
            """,
        AntipatternExplanation:
            "`.Result` e `.Wait()` bloqueiam a thread atual sincronamente até a Task terminar. Com um " +
            "SynchronizationContext capturado, a continuação async não pode rodar porque a thread dela " +
            "está bloqueada — um deadlock. Sem context, você ainda desperdiça uma thread do pool por " +
            "chamada, e o suficiente delas esgota o pool para que novo trabalho não possa começar.",
        PatternCode:
            """
            // ✅ Seja async até o fim: use await em vez de bloquear.
            public async Task<string> GetDataAsync()
            {
                return await FetchAsync();
            }

            // Se você PRECISA expor uma API síncrona sobre async (raro), não
            // finja com .Result. Opções, em ordem de preferência:
            //  - torne o chamador async,
            //  - use await + ConfigureAwait(false) em código de biblioteca,
            //  - em último caso, um sync context dedicado / nova thread.
            """,
        PatternExplanation:
            "A cura é tornar toda a cadeia de chamadas assíncrona, para que nenhuma thread fique " +
            "bloqueada esperando uma Task. Em código de biblioteca, adicione `ConfigureAwait(false)` " +
            "para as continuações não tentarem retomar em um context capturado. O ASP.NET Core não tem " +
            "esse context, mas sync-over-async ainda esgota o thread pool dele sob carga.",
        KeyTakeaways: new[]
        {
            "Nunca chame .Result/.Wait()/.GetAwaiter().GetResult() em trabalho async vivo.",
            "Async até o fim — uma única chamada bloqueante pode dar deadlock na cadeia.",
            "Em bibliotecas, ConfigureAwait(false) evita retomar em um context capturado.",
        },
        SupportsRun: false,
        Parameters: Array.Empty<DemoParameter>())
    { Chapter = "Cap. 5 · Programação Assíncrona com C#" };
}
