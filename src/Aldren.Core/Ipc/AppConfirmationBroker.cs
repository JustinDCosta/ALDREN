using Aldren.Contracts;
using Aldren.Security.Gateway;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Aldren.Core.Ipc;

/// <summary>Asks the connected App. Declines at once when none is connected or it disconnects mid-question.</summary>
public sealed class AppConfirmationBroker(AppConnections connections, ILogger<AppConfirmationBroker> log) : IConfirmationBroker
{
    public async Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct)
    {
        if (connections.Current is not { } connection)
        {
            log.LogInformation("No App is connected; declining {Tool} ({RequestId})", request.Tool, request.RequestId);
            return false;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, connection.Disconnected);
        try
        {
            return await connection.App.ConfirmAsync(request, linked.Token).WaitAsync(linked.Token);
        }
        catch (Exception ex) when (ex is ConnectionLostException or ObjectDisposedException
                                       || (ex is OperationCanceledException && connection.Disconnected.IsCancellationRequested))
        {
            log.LogInformation("The App went away mid-question; declining {Tool} ({RequestId})", request.Tool, request.RequestId);
            return false;
        }
    }
}
