using Aldren.Contracts;

namespace Aldren.App.Ipc;

/// <summary>What Core calls on the App. The confirmation UI is supplied by the shell.</summary>
public sealed class AppCallbacks(Func<ConfirmationRequest, CancellationToken, Task<bool>> confirm) : IAldrenApp
{
    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken) =>
        confirm(request, cancellationToken);
}
