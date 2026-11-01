using Aldren.Contracts;

namespace Aldren.Core.Ipc;

/// <param name="Disconnected">Cancelled when the App goes away.</param>
public sealed record AppConnection(IAldrenApp App, int ProcessId, CancellationToken Disconnected);

/// <summary>The App currently connected over the pipe, shared by the pipe server, the supervisor and the broker.</summary>
public sealed class AppConnections
{
    private readonly Lock _lock = new();
    private TaskCompletionSource<AppConnection> _next = NewSource();

    public AppConnection? Current { get; private set; }

    public void Connected(AppConnection connection)
    {
        TaskCompletionSource<AppConnection> next;
        lock (_lock)
        {
            Current = connection;
            next = _next;
            _next = NewSource();
        }

        next.TrySetResult(connection);
    }

    public void Disconnected(AppConnection connection)
    {
        lock (_lock)
        {
            if (ReferenceEquals(Current, connection))
            {
                Current = null;
            }
        }
    }

    /// <summary>The current connection, or the next one.</summary>
    public Task<AppConnection> WaitAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return Current is { } current ? Task.FromResult(current) : _next.Task.WaitAsync(ct);
        }
    }

    private static TaskCompletionSource<AppConnection> NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
