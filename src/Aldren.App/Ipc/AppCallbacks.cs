using Aldren.Contracts;

namespace Aldren.App.Ipc;

/// <summary>What Core calls on the App. The confirmation and notification UI is supplied by the shell.</summary>
public sealed class AppCallbacks(
    Func<ConfirmationRequest, CancellationToken, Task<bool>> confirm,
    Action<string, string> notify) : IAldrenApp
{
    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken) =>
        confirm(request, cancellationToken);

    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken)
    {
        notify(title, message);
        return Task.CompletedTask;
    }
}
