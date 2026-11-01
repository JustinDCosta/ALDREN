using System.IO.Pipes;
using Aldren.Contracts;
using Aldren.Contracts.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Win32;

namespace Aldren.Core.Ipc;

/// <summary>Serves <see cref="CoreService"/> to one App at a time, and listens again after it disconnects.</summary>
public sealed class CoreRpcServer(CoreService core, AppConnections connections, ILogger<CoreRpcServer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = AldrenPipe.CreateServer();
                await pipe.WaitForConnectionAsync(stoppingToken);
                await ServeAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Pipe connection failed");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        var processId = ClientProcessId(pipe);
        using var rpc = AldrenRpc.Attach(pipe, core);
        using var disconnected = new CancellationTokenSource();
        var connection = new AppConnection(rpc.Attach<IAldrenApp>(), processId, disconnected.Token);
        rpc.StartListening();
        connections.Connected(connection);
        log.LogInformation("App connected (process {ProcessId})", processId);
        try
        {
            await rpc.Completion.WaitAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("App connection ended with an error: {Message}", ex.Message);
        }
        finally
        {
            connections.Disconnected(connection);
            await disconnected.CancelAsync();
            log.LogInformation("App disconnected (process {ProcessId})", processId);
        }
    }

    private static int ClientProcessId(NamedPipeServerStream pipe) =>
        PInvoke.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var id) ? (int)id : 0;
}
