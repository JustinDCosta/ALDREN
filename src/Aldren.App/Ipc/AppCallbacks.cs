using Aldren.Contracts;

namespace Aldren.App.Ipc;

/// <summary>What Core calls on the App. The confirmation, notification, island and cloud indicator UI is supplied by the shell.</summary>
public sealed class AppCallbacks(
    Func<ConfirmationRequest, CancellationToken, Task<bool>> confirm,
    Action<string, string> notify,
    Action<VoiceState, string> showVoice,
    Action<string?> showCloud,
    Action<string> showReplyProgress,
    Func<Task> yieldFocus) : IAldrenApp
{
    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken) =>
        confirm(request, cancellationToken);

    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken)
    {
        notify(title, message);
        return Task.CompletedTask;
    }

    public Task ShowVoiceAsync(VoiceState state, string text, CancellationToken cancellationToken)
    {
        showVoice(state, text);
        return Task.CompletedTask;
    }

    public Task ShowCloudAsync(string? activity, CancellationToken cancellationToken)
    {
        showCloud(activity);
        return Task.CompletedTask;
    }

    public Task ShowReplyProgressAsync(string text, CancellationToken cancellationToken)
    {
        showReplyProgress(text);
        return Task.CompletedTask;
    }

    public Task YieldFocusAsync(CancellationToken cancellationToken) => yieldFocus();
}
