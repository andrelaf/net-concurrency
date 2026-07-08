# Concorrência no .NET — notas conceituais

Notas que dão base às demos interativas. Leia estas para construir o modelo
mental; rode as demos para *ver* os números.

## Os três tipos de "concorrência"

| Tipo | Limitado por | Ferramenta certa no .NET | Ferramenta errada |
|---|---|---|---|
| **Assincronia** (I/O) | espera (rede, disco, BD) | `async`/`await`, `Task.WhenAll` | `Parallel.For`, bloquear threads |
| **Paralelismo** (CPU) | computar entre núcleos | `Parallel.For/ForEachAsync`, PLINQ | uma grande chamada `async` |
| **Multithreading** (primitivas) | estado mutável compartilhado | `lock`, `Interlocked`, coleções concorrentes, `Channel<T>` | filas caseiras, `Thread.Abort` |

O erro mais comum é usar uma ferramenta de CPU para um problema de I/O (ou
vice-versa). Pergunte primeiro: *este código está esperando, ou computando?*

## Async / await

- `await` devolve a thread ao pool enquanto o I/O está em andamento — ele **não**
  bloqueia. É por isso que uma máquina pode atender milhares de requisições
  concorrentes com um punhado de threads.
- `await` dentro de um loop **serializa** chamadas independentes. Inicie as tasks
  primeiro, depois `await Task.WhenAll(...)`. → demo *await sequencial vs WhenAll*.
- Nunca bloqueie em código async com `.Result` / `.Wait()` /
  `.GetAwaiter().GetResult()`. Sob um `SynchronizationContext` capturado dá
  deadlock; em qualquer outro lugar prende uma thread do pool e, sob carga,
  esgota o pool. Seja **async até o fim**. → ficha *Sync-over-async*.
- Retorne `Task`, nunca `async void` (exceto handlers de eventos de UI, cujo
  corpo você envolve em try/catch). Exceções de `async void` ignoram o try/catch
  do chamador e derrubam o processo. → ficha *async void*.
- Em código de biblioteca, use `ConfigureAwait(false)` para as continuações não
  tentarem retomar em um context capturado.

## Paralelismo (CPU-bound)

- `Parallel.For` / `Parallel.ForEachAsync` particionam o trabalho pelo thread
  pool. Use um **acumulador local por thread** com um merge final para não
  contender em uma variável compartilhada a cada iteração. → demo *Parallel.For*.
- PLINQ (`AsParallel()`) paraleliza uma query com um operador, mas só compensa
  quando o trabalho por elemento é não trivial e sem efeitos colaterais. Meça —
  para elementos baratos o overhead de particionamento pode deixá-lo *mais lento*.
  → demo *PLINQ*.
- O ganho é limitado pela contagem de núcleos e pela lei de Amdahl (a fração
  serial).
- Limite a concorrência contra dependências externas com `SemaphoreSlim` ou
  `MaxDegreeOfParallelism`; fan-out ilimitado é um DoS autoinfligido.
  → demo *Throttling*.

## Threads vs o thread pool

- Uma `Thread` é um objeto pesado do SO (~1 MB de stack + escalonamento do
  kernel). Criar uma por tarefa curta sobrecarrega o escalonador e queima memória.
- `Task` / `Task.Run` usa o thread pool agrupado e auto-ajustável, e reutiliza
  threads. Use uma `Thread` crua só para trabalho de longa duração ou de
  prioridade especial. → demo *Thread por item vs Task*.

## Estado mutável compartilhado (os riscos)

- **Condição de corrida:** `x++` em um campo compartilhado é ler-modificar-
  escrever, então perde atualizações sob contenção — silenciosa e não
  deterministicamente. Use `Interlocked` para contadores/flags simples, `lock`
  para invariantes de vários campos. → demo *Condição de corrida*.
- **Coleções rasgadas:** `Dictionary`/`List`/`HashSet` não são seguros para
  escritores concorrentes — você recebe exceções ou corrupção. Use
  `System.Collections.Concurrent` (e `AddOrUpdate`/`GetOrAdd` para atualizações
  compostas atômicas). → demo *ConcurrentDictionary*.
- **Deadlock:** precisa de quatro condições (exclusão mútua, hold-and-wait, sem
  preempção, espera circular). Quebre a **espera circular** com uma ordem global
  consistente de locks, ou use `Monitor.TryEnter(timeout)` para falhar rápido em
  vez de travar. → demo *Deadlock*.
- "Funcionou na minha máquina" não prova nada sobre uma corrida — não
  determinismo significa que a ausência de falha não é evidência de correção.

## Produtor / consumidor

- Não faça na mão um loop com `lock` + `List` + polling: ele faz espera ocupada,
  `RemoveAt(0)` é O(n), e a sinalização de conclusão é sujeita a corrida.
- `System.Threading.Channels` é a primitiva feita para isso: caminhos rápidos
  lock-free, consumo com `await foreach`, `Writer.Complete()` para shutdown limpo,
  e capacidade **limitada** para back-pressure, de modo que produtores rápidos não
  estourem sua memória. → demo *Channels*.

## Cancelamento

- O cancelamento é **cooperativo**. Passar um `CancellationToken` adiante não faz
  nada, a menos que o código o observe (`ct.ThrowIfCancellationRequested()` e/ou
  passar `ct` para APIs canceláveis).
- `OperationCanceledException` é o sinal esperado de um cancelamento limpo, não um
  bug. → demo *Cancelamento*.

## Criação de tasks: `Task.Run` vs `Task.Factory.StartNew`

- `Task.Run` é a forma **recomendada** de criar e iniciar uma task quando você não
  precisa de controle extra. Ele sempre usa o scheduler **default** (thread pool)
  e faz o **unwrap** da `Task` interna de um delegate async para você.
- `Task.Factory.StartNew` é para cenários **avançados**: um `TaskScheduler`
  customizado, `TaskCreationOptions` extras (ex.: `LongRunning`), ou passar
  estado. Duas armadilhas: ele usa `TaskScheduler.Current` (não necessariamente o
  pool), e com um delegate async retorna `Task<Task>` — dar await nele termina no
  *primeiro await interno*, antes de o trabalho real completar. Corrija com
  `.Unwrap()`. → demo *StartNew vs Run*.

## Regras de bolso

1. I/O → async; CPU → paralelo; estado compartilhado → a primitiva certa.
2. Async até o fim. Nunca bloqueie em código async.
3. Prefira `Interlocked` / coleções concorrentes / `Channel<T>` a locks improvisados.
4. Limite sua concorrência contra qualquer coisa externa.
5. Sempre observe seu `CancellationToken`.
6. Meça. A intuição sobre concorrência é pouco confiável; as demos existem para
   checá-la.

## Referências

- Microsoft Learn — [Task-based asynchronous programming (TPL)](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-based-asynchronous-programming):
  Tasks, `Task.Run` vs `Task.Factory.StartNew`, continuações, `WhenAll`/`WhenAny`,
  tasks-filhas anexadas/desanexadas, cancelamento e `AggregateException`.
- Microsoft Learn — [Asynchronous programming with async and await](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/).
- Microsoft Learn — [Parallel programming in .NET](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/) (TPL, PLINQ, `Parallel.For`).
- Microsoft Learn — [System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).
- Livro de referência: *Parallel Programming and Concurrency with C# 10 and .NET 6*
  (Alvin Ashcraft, Packt) — exemplos originais em `docs/chapter01`…`chapter12`
  (net6.0); este lab moderniza os conceitos para .NET 10.
