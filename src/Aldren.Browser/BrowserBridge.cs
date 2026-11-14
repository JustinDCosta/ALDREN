using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json.Nodes;

namespace Aldren.Browser;

/// <summary>
/// ALDREN's side of the browser bridge (ARCHITECTURE §12): one JSON command per line on the browser host's current-user
/// pipe, replies matched by id. The host exists only while Chrome runs with the ALDREN Companion extension.
/// </summary>
public sealed class BrowserBridge : IDisposable
{
    private const string PipeName = "aldren-companion";

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly SemaphoreSlim _connect = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private int _nextId;

    public bool Connected => _pipe?.IsConnected == true;

    public async Task<JsonNode?> SendAsync(string command, JsonObject? args, CancellationToken ct, TimeSpan? timeout = null)
    {
        await EnsureConnectedAsync(ct);
        var id = Interlocked.Increment(ref _nextId);
        var reply = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = reply;
        try
        {
            await _writer!.WriteLineAsync(new JsonObject { ["id"] = id, ["cmd"] = command, ["args"] = args ?? [] }.ToJsonString().AsMemory(), ct);
            return await reply.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(15), ct);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("The browser didn't answer in time.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Whether the Companion answers right now (a quick ping).</summary>
    public async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            await SendAsync("ping", null, ct, TimeSpan.FromSeconds(3));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (Connected)
        {
            return;
        }

        await _connect.WaitAsync(ct);
        try
        {
            if (Connected)
            {
                return;
            }

            _pipe?.Dispose();
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(800, ct);
            }
            catch (TimeoutException)
            {
                pipe.Dispose();
                throw new InvalidOperationException(
                    "The ALDREN Companion isn't connected. Open Chrome with the extension loaded (Settings > Browser explains how).");
            }

            _pipe = pipe;
            _writer = new StreamWriter(pipe) { AutoFlush = true };
            var reader = new StreamReader(pipe);
            _ = Task.Run(() => ReadRepliesAsync(reader), CancellationToken.None);
        }
        finally
        {
            _connect.Release();
        }
    }

    private async Task ReadRepliesAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                var node = JsonNode.Parse(line);
                var id = node?["id"]?.GetValue<int>() ?? 0;
                if (!_pending.TryGetValue(id, out var tcs))
                {
                    continue;
                }

                if (node?["ok"]?.GetValue<bool>() == true)
                {
                    tcs.TrySetResult(node["result"]);
                }
                else
                {
                    tcs.TrySetException(new InvalidOperationException(node?["error"]?.GetValue<string>() ?? "The browser refused that."));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Text.Json.JsonException)
        {
        }

        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new InvalidOperationException("The browser disconnected."));
        }
    }

    public void Dispose() => _pipe?.Dispose();
}
