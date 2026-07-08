namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Apenas ilustrativa: métodos 'async void' não podem receber await e suas
/// exceções escapam do try/catch, derrubando o processo. Mostrada como ficha
/// para não quebrarmos o servidor de propósito só para provar.
/// </summary>
public sealed class AsyncVoidDemo : DemoBase
{
    public override DemoInfo Info { get; } = new(
        Id: "async-void",
        Title: "async void",
        Category: "Riscos",
        Summary: "async void não pode receber await e lança exceções inobserváveis que derrubam o processo.",
        AntipatternCode:
            """
            // ❌ async void: o chamador não recebe Task, então não pode aguardar a
            // conclusão nem observar falhas. Uma exceção aqui é lançada no
            // SynchronizationContext / thread pool e tipicamente derruba o
            // processo — um try/catch em volta da CHAMADA não faz nada.
            async void ProcessAsync()          // <- void, não Task
            {
                await Task.Delay(100);
                throw new InvalidOperationException("boom");
            }

            try
            {
                ProcessAsync();                // retorna imediatamente
                // a exceção NÃO é capturada aqui; ela escapa depois e derruba
            }
            catch (Exception ex)
            {
                // nunca roda
            }
            """,
        AntipatternExplanation:
            "Um método `async void` devolve o controle antes de o trabalho async terminar e não " +
            "fornece Task para aguardar, então os chamadores não sabem quando ele completa nem se " +
            "falhou. Uma exceção não tratada é postada no context atual e geralmente encerra o " +
            "processo. O único uso legítimo são handlers de eventos de nível superior.",
        PatternCode:
            """
            // ✅ Retorne Task para que os chamadores possam aguardar e observar
            // as exceções.
            async Task ProcessAsync()          // <- Task
            {
                await Task.Delay(100);
                throw new InvalidOperationException("boom");
            }

            try
            {
                await ProcessAsync();          // agora aguardável
            }
            catch (Exception ex)
            {
                // capturada corretamente
            }

            // Única exceção: handlers de eventos precisam ser 'async void'.
            // Envolva todo o corpo em try/catch para nada escapar.
            private async void OnClick(object? s, EventArgs e)
            {
                try { await DoWorkAsync(); }
                catch (Exception ex) { Log(ex); }
            }
            """,
        PatternExplanation:
            "Retornar `Task` torna o método aguardável e suas exceções observáveis — elas aparecem no " +
            "`try/catch` que aguarda. Reserve o `async void` para handlers de eventos, e sempre " +
            "proteja o corpo deles com try/catch para que um throw não derrube a aplicação.",
        KeyTakeaways: new[]
        {
            "Retorne Task (ou Task<T>) de todo método async, exceto handlers de eventos.",
            "Exceções de async void ignoram o try/catch no ponto da chamada e derrubam o processo.",
            "Em handlers de eventos async void, envolva todo o corpo em try/catch.",
        },
        SupportsRun: false,
        Parameters: Array.Empty<DemoParameter>())
    { Chapter = "Cap. 5 · Programação Assíncrona com C#" };
}
