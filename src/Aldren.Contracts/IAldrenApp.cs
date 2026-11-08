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
}
