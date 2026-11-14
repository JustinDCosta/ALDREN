namespace Aldren.Contracts;

/// <summary>What the App offers Core, called back on the same connection.</summary>
public interface IAldrenApp
{
    /// <summary>Asks you to confirm an action. False when you decline or close the prompt.</summary>
    Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken);

    /// <summary>Shows a notification, for example a finished timer.</summary>
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken);

    /// <summary>The island follows the voice loop: listening, the transcript, thinking, the reply being spoken.</summary>
    Task ShowVoiceAsync(VoiceState state, string text, CancellationToken cancellationToken);

    /// <summary>The cloud indicator: what is going online right now, or null when nothing is.</summary>
    Task ShowCloudAsync(string? activity, CancellationToken cancellationToken);

    /// <summary>The reply to the typed request in flight, as far as it has been written (it arrives whole as the call's result).</summary>
    Task ShowReplyProgressAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Before ALDREN types into the window you were using: the island gives keyboard focus back to it (and stays up
    /// without focus). Returns once focus has been handed over.
    /// </summary>
    Task YieldFocusAsync(CancellationToken cancellationToken);
}
