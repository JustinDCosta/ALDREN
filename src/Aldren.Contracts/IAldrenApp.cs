namespace Aldren.Contracts;

/// <summary>What the App offers Core, called back on the same connection.</summary>
public interface IAldrenApp
{
    /// <summary>Asks you to confirm an action. False when you decline or close the prompt.</summary>
    Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken);
}
