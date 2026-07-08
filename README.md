# ConcurrencyLab — padrões e antipadrões em .NET 10

Um lab interativo para explorar programação **assíncrona, paralela e
multithread** no .NET. Cada tópico é um par **padrão vs. antipadrão** que você
pode **executar ao vivo**: um backend ASP.NET Core (.NET 10) roda as duas
variantes em threads reais e devolve medições reais (tempo decorrido,
atualizações perdidas, pico de concorrência, núcleos usados…), e um front-end
React deixa você ler o código, as notas e comparar os resultados lado a lado.

```
┌────────────────────┐        /api/demos            ┌──────────────────────────┐
│  React + Vite (TS) │  ───────────────────────────▶│  ASP.NET Core (.NET 10)  │
│  UI interativa     │  ◀──── métricas reais ─────  │  roda cada demo ao vivo  │
└────────────────────┘   POST /api/demos/{id}/run   └──────────────────────────┘
```

## O que tem dentro

13 demos em 5 categorias (11 executáveis + 2 fichas de estudo):

| Categoria | Demo | Antipadrão → Padrão |
|---|---|---|
| Fundamentos | Thread por item vs Task | `new Thread` por item → thread pool (`Task.Run`) |
| Fundamentos | StartNew vs Run | `Task.Factory.StartNew(async …)` → `Task.Run` (unwrap automático) |
| Coordenação Async | await sequencial vs WhenAll | `await` em loop → `Task.WhenAll` |
| Coordenação Async | Throttling | fan-out ilimitado → `SemaphoreSlim` |
| Coordenação Async | Cancelamento | ignorar o token → cancelamento cooperativo |
| Paralelismo de Dados | Parallel.For | `for` sequencial → `Parallel.For` + local por thread |
| Paralelismo de Dados | PLINQ | `LINQ` → `AsParallel()` |
| Coleções e Mensageria | ConcurrentDictionary | `Dictionary` sob contenção → `ConcurrentDictionary` |
| Coleções e Mensageria | Channels | fila com lock + poll → `System.Threading.Channels` |
| Riscos | Condição de corrida | `count++` → `Interlocked.Increment` |
| Riscos | Deadlock | ordem de locks inconsistente → ordem consistente |
| Riscos | Sync-over-async *(ficha)* | `.Result`/`.Wait()` → async até o fim |
| Riscos | `async void` *(ficha)* | `async void` → `async Task` |

As *fichas de estudo* são riscos inseguros de executar em um servidor
compartilhado (podem dar deadlock ou derrubar o processo), então são
apresentados como código anotado em vez de serem rodados.

As demos e notas estão ancoradas na documentação oficial da Microsoft — veja
[`docs/notes.md`](docs/notes.md#referências), começando por
[Task-based asynchronous programming (TPL)](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-based-asynchronous-programming).

## Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/) (`dotnet --version` → `10.x`)
- [Node.js 20+](https://nodejs.org/) (`node --version`)

## Como rodar

Abra dois terminais.

**1) Backend** (http://localhost:5180)

```bash
cd backend/ConcurrencyLab.Api
dotnet run
```

**2) Frontend** (http://localhost:5173)

```bash
cd frontend
npm install      # só na primeira vez
npm run dev
```

Depois abra **http://localhost:5173**. O dev server do Vite faz proxy de `/api`
para o backend, então não há dor de cabeça com CORS ou URLs.

> Atalho: na raiz do repo, rode `./run.ps1` (Windows) ou `./run.sh` (bash) para
> subir os dois de uma vez.

## Estrutura do projeto

```
net-concurrency/
├─ backend/ConcurrencyLab.Api/     ASP.NET Core Minimal API (.NET 10)
│  ├─ Program.cs                   endpoints + CORS
│  └─ Demos/                       um arquivo por demo + infra compartilhada
│     ├─ IConcurrencyDemo.cs       contrato + classe base de timing/log
│     ├─ DemoModels.cs             records, RunArgs, ConcurrencyMeter, Workloads
│     ├─ DemoRegistry.cs           o catálogo
│     └─ *Demo.cs                  as 13 demos
├─ frontend/                       React 19 + Vite + TypeScript
│  └─ src/
│     ├─ api.ts                    cliente tipado espelhando os records C#
│     ├─ App.tsx                   layout de sidebar + detalhe
│     └─ components/               CodeBlock, DemoDetail, RunPanel, VariantCard
└─ docs/                           notas conceituais + livro de referência
```

## A API

| Método e rota | Propósito |
|---|---|
| `GET /api/demos` | Catálogo completo (metadados + código de cada demo) |
| `GET /api/demos/{id}` | Uma demo |
| `GET /api/categories` | Nomes das categorias na ordem de exibição |
| `POST /api/demos/{id}/run` | Executa uma demo ao vivo; body `{ "variant": "both", "parameters": { … } }` |

Cada execução tem um orçamento de 30 segundos, para que um conjunto de
parâmetros pesado não prenda o servidor.

## Adicionando uma demo

1. Crie `backend/ConcurrencyLab.Api/Demos/MinhaDemo.cs` derivando de `DemoBase`.
2. Preencha o `Info` (metadados + os dois trechos de código + notas) e implemente
   `RunAntipatternAsync` / `RunPatternAsync` usando `MeasureAsync`.
3. Registre-a em `DemoRegistry.cs`.

O front-end é totalmente orientado a dados — novas demos e seus sliders aparecem
automaticamente.

## Material de referência

O livro companheiro é **_Parallel Programming and Concurrency with C# 10 and
.NET 6_** (Alvin Ashcraft, Packt), com seus projetos de exemplo originais em
[`docs/`](docs/) (`chapter01`…`chapter12`). **Esses exemplos têm como alvo
.NET 6 / .NET 6-windows; este lab moderniza os mesmos conceitos para .NET 10** e
os embrulha em uma UI interativa e ao vivo. Cada demo mostra seu capítulo de
origem (o chip 📖), então o lab funciona como um companheiro do livro:

| Capítulo do livro | Demo(s) do lab |
|---|---|
| Cap. 1 — Managed Threading Concepts | Thread por item vs Task |
| Cap. 3 — Best Practices for Managed Threading | Condição de corrida, Deadlock |
| Cap. 5 — Asynchronous Programming with C# | WhenAll, StartNew vs Run, Sync-over-async, async void |
| Cap. 6 — Parallel Programming Concepts | Parallel.For |
| Cap. 7 — TPL and Dataflow | Channels produtor/consumidor |
| Cap. 8 — Parallel Data Structures and PLINQ | LINQ vs PLINQ |
| Cap. 9 — Concurrent Collections | Dictionary vs ConcurrentDictionary |
| Cap. 11 — Canceling Asynchronous Work | Cancelamento cooperativo |

Veja [`docs/notes.md`](docs/notes.md) para as notas conceituais e as referências
à documentação oficial por trás de cada demo.
