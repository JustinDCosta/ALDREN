using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace S6.Host;

/// <summary>ALDREN's side of the bridge: sends a command over the host's pipe and waits for the reply with the same id.</summary>
internal sealed class BridgeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _nextId;

    private BridgeClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe);
        _writer = new StreamWriter(pipe) { AutoFlush = true };
    }

    public static async Task<BridgeClient> ConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", Relay.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout, ct);
        return new BridgeClient(pipe);
    }

    public Task<(JsonElement Reply, double Milliseconds)> SendAsync(string cmd, object? args, CancellationToken ct)
    {
        var id = ++_nextId;
        return SendRawAsync(id, JsonSerializer.Serialize(new { id, cmd, args = args ?? new { } }), ct);
    }

    /// <summary>Sends a line as is (the selftest uses it for malformed commands) and waits up to 10 s for the reply to <paramref name="id"/>.</summary>
    public async Task<(JsonElement Reply, double Milliseconds)> SendRawAsync(int id, string line, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var clock = Stopwatch.StartNew();
        await _writer.WriteLineAsync(line.AsMemory(), timeout.Token);
        while (true)
        {
            var text = await _reader.ReadLineAsync(timeout.Token) ?? throw new IOException("The host closed the pipe.");
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("id", out var replyId) && replyId.GetInt32() == id)
            {
                return (doc.RootElement.Clone(), clock.Elapsed.TotalMilliseconds);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        _reader.Dispose();
        await _pipe.DisposeAsync();
    }
}
