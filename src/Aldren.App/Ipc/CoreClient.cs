using Aldren.Contracts;
using Aldren.Contracts.Rpc;
using Microsoft.Extensions.Logging;

namespace Aldren.App.Ipc;

/// <summary>
/// Keeps the App connected to Core: connects with backoff (250 ms up to 5 s), serves <see cref="AppCallbacks"/>, and asks
/// the watchdog whether to relaunch Core when the connection drops. Events are raised on a background thread.
/// </summary>
public sealed class CoreClient(AppCallbacks callbacks, CoreWatchdog watchdog, ILogger<CoreClient> log) : IAsyncDisposable
{
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public IAldrenCore? Core { get; private set; }

    public event Action<IAldrenCore>? Connected;

    public event Action? Disconnected;

    public void Start() => _loop = Task.Run(() => RunAsync(_stop.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        var retry = FirstRetry;
        while (!ct.IsCancellationRequested)
        {
            var pipe = AldrenPipe.CreateClient();
            try
            {
                await pipe.ConnectAsync(1_000, ct);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                await pipe.DisposeAsync();
                await DelayAsync(retry, ct);
                retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, MaxRetry.Ticks));
                continue;
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                return;
            }

            retry = FirstRetry;
            await using (pipe)
            {
                using var rpc = AldrenRpc.Attach(pipe, callbacks);
                var core = rpc.Attach<IAldrenCore>();
                rpc.StartListening();
                Core = core;
                watchdog.MarkConnected();
                log.LogInformation("Connected to Core");
                Connected?.Invoke(core);
                try
                {
                    await rpc.Completion.WaitAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    log.LogWarning("Connection to Core ended with an error: {Message}", ex.Message);
                }
                finally
                {
                    Core = null;
                }
            }

            log.LogInformation("Disconnected from Core");
            Disconnected?.Invoke();
            try
            {
                await watchdog.OnCoreLostAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        _stop.Dispose();
    }
}
