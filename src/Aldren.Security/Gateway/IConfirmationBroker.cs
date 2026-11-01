using Aldren.Contracts;

namespace Aldren.Security.Gateway;

/// <summary>Asks you to confirm. True only for an explicit yes.</summary>
public interface IConfirmationBroker
{
    Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct);
}
