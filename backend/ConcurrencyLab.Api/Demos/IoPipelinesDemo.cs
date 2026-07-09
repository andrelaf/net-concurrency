using System.Buffers;
using System.IO.Pipelines;

namespace ConcurrencyLab.Api.Demos;

/// <summary>
/// Fazer parsing de mensagens delimitadas acumulando bytes em um buffer que cresce
/// e é compactado a cada extração custa muitas cópias (O(n²)). O
/// <c>System.IO.Pipelines</c> (<c>PipeReader</c>/<c>SequenceReader</c>) faz parsing
/// sem cópias, com back-pressure embutido.
/// </summary>
public sealed class IoPipelinesDemo : DemoBase
{
    private static readonly DemoParameter Messages =
        new("messages", "Mensagens", 12_000, 2_000, 30_000, 2_000);

    public override DemoInfo Info { get; } = new(
        Id: "buffer-parsing-vs-io-pipelines",
        Title: "Parsing com buffer manual vs System.IO.Pipelines",
        Category: "Pipelines e Padrões",
        Summary: "System.IO.Pipelines faz parsing de streams sem cópias e com back-pressure.",
        AntipatternCode:
            """
            // ❌ Acumula bytes num buffer que cresce e é compactado a cada
            // mensagem. Cada extração copia a mensagem e desloca o resto —
            // O(n²) de cópia, além de lidar com mensagens partidas na mão.
            var buffer = new List<byte>();
            foreach (var chunk in stream)
            {
                buffer.AddRange(chunk);
                int nl;
                while ((nl = buffer.IndexOf(NEWLINE)) >= 0)
                {
                    var msg = buffer.GetRange(0, nl);   // cópia
                    buffer.RemoveRange(0, nl + 1);      // desloca o resto (O(n))
                    Handle(msg);
                }
            }
            """,
        AntipatternExplanation:
            "Um buffer `List<byte>` que cresce e é compactado a cada mensagem copia e desloca dados " +
            "repetidamente, além de exigir lógica manual e frágil para mensagens que chegam partidas " +
            "entre leituras. É lento e propenso a bugs de fronteira.",
        PatternCode:
            """
            // ✅ PipeReader entrega um ReadOnlySequence<byte> sobre os buffers
            // recebidos; SequenceReader acha o delimitador sem copiar. AdvanceTo
            // devolve o que sobrou para a próxima leitura. Back-pressure embutido.
            while (true)
            {
                ReadResult result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;
                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                    Handle(line);                       // sem cópia
                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }

            static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ...)
            {
                var r = new SequenceReader<byte>(buffer);
                if (r.TryReadTo(out line, NEWLINE)) { buffer = buffer.Slice(r.Position); return true; }
                line = default; return false;
            }
            """,
        PatternExplanation:
            "`System.IO.Pipelines` separa quem escreve de quem lê por um buffer gerenciado com " +
            "back-pressure. O `PipeReader` expõe os bytes como `ReadOnlySequence<byte>`; o " +
            "`SequenceReader` localiza delimitadores sem copiar, e `AdvanceTo` marca o que foi " +
            "consumido e o que ainda falta — resolvendo mensagens partidas sem código manual.",
        KeyTakeaways: new[]
        {
            "System.IO.Pipelines faz parsing de I/O sem cópias e sem lógica manual de buffer.",
            "É o motor do Kestrel; ideal para protocolos delimitados/binários de alta vazão.",
            "SequenceReader + AdvanceTo tratam mensagens partidas entre leituras de graça.",
        },
        SupportsRun: true,
        Parameters: new[] { Messages })
    {
        Chapter = null, Since = ".NET Core 2.1",
        UseCases = new[]
        {
            "Parsers de protocolo de rede de alta vazão (HTTP, mensageria binária).",
            "Ler sockets/streams sem alocar, tratando mensagens que chegam partidas.",
            "Servidores/proxies onde cada cópia de buffer conta (base do Kestrel).",
        },
    };

    private const byte Newline = (byte)'\n';
    private const int ChunkSize = 4096;

    private static byte[] BuildStream(int messages)
    {
        var sb = new System.Text.StringBuilder(messages * 12);
        for (int i = 0; i < messages; i++) sb.Append("msg-").Append(i).Append('\n');
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }

    public override Task<RunVariant> RunAntipatternAsync(RunArgs args, CancellationToken ct)
    {
        int messages = args.Get(Messages);
        byte[] data = BuildStream(messages);
        return MeasureAsync("antipattern", rec =>
        {
            // Acumula tudo num buffer só e então extrai mensagem a mensagem,
            // deslocando o restante a cada RemoveRange -> O(n²) de cópia.
            var buffer = new List<byte>(data);
            int parsed = 0;
            long shifted = 0;
            int nl;
            while ((nl = buffer.IndexOf(Newline)) >= 0)
            {
                parsed++;
                buffer.RemoveRange(0, nl + 1); // desloca todo o restante
                shifted += buffer.Count;       // custo de deslocamento
            }
            rec.Log($"Parseou {parsed:N0} mensagens; ~{shifted / 1024 / 1024:N0} MB deslocados no buffer");
            return Task.FromResult(new VariantOutcome(
                Ok: parsed == messages,
                Headline: $"Parseou {parsed:N0} msgs deslocando ~{shifted / 1024 / 1024:N0} MB de buffer",
                Metrics: new[]
                {
                    new MetricItem("Mensagens", parsed.ToString("N0")),
                    new MetricItem("Bytes deslocados", $"~{shifted / 1024 / 1024:N0} MB", "Cópias O(n²)"),
                    new MetricItem("Estratégia", "List<byte> + compactação"),
                }));
        });
    }

    public override async Task<RunVariant> RunPatternAsync(RunArgs args, CancellationToken ct)
    {
        int messages = args.Get(Messages);
        byte[] data = BuildStream(messages);
        return await MeasureAsync("pattern", async rec =>
        {
            var pipe = new Pipe();
            int parsed = 0;

            var writing = Task.Run(async () =>
            {
                for (int off = 0; off < data.Length; off += ChunkSize)
                {
                    int len = Math.Min(ChunkSize, data.Length - off);
                    await pipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(data, off, len), ct);
                }
                await pipe.Writer.CompleteAsync();
            }, ct);

            while (true)
            {
                ReadResult result = await pipe.Reader.ReadAsync(ct);
                var buffer = result.Buffer;
                while (TryReadLine(ref buffer, out _)) parsed++;
                pipe.Reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
            await pipe.Reader.CompleteAsync();
            await writing;

            rec.Log($"Parseou {parsed:N0} mensagens com PipeReader — zero cópias de buffer");
            return new VariantOutcome(
                Ok: parsed == messages,
                Headline: $"Parseou {parsed:N0} mensagens sem cópias (zero-copy) via PipeReader",
                Metrics: new[]
                {
                    new MetricItem("Mensagens", parsed.ToString("N0")),
                    new MetricItem("Bytes deslocados", "0", "Zero-copy"),
                    new MetricItem("Estratégia", "PipeReader + SequenceReader"),
                });
        });
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (reader.TryReadTo(out line, Newline))
        {
            buffer = buffer.Slice(reader.Position);
            return true;
        }
        line = default;
        return false;
    }
}
