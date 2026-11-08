namespace Aldren.Core.Orchestration;

/// <summary>
/// The Quick Brain's cloud fallback (ARCHITECTURE §8.4): used only when the local model is unavailable, the request carries
/// nothing sensitive and you enabled it. Phase 6 implements it through the Cloud Policy Layer; until then ALDREN is local only.
/// </summary>
public interface ICloudFallback
{
    /// <summary>Null when the fallback can't or may not answer.</summary>
    Task<string?> TryAnswerAsync(string utterance, CancellationToken ct);
}

public sealed class NoCloudFallback : ICloudFallback
{
    public Task<string?> TryAnswerAsync(string utterance, CancellationToken ct) => Task.FromResult<string?>(null);
}
