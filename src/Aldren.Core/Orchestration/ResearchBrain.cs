using System.Text.Json.Nodes;
using Aldren.Cloud;
using Aldren.Core.Ipc;
using Aldren.Core.Settings;
using Aldren.Inference;
using Aldren.Research;
using Microsoft.Extensions.Logging;

namespace Aldren.Core.Orchestration;

/// <summary>
/// The Research Brain (ARCHITECTURE §6, §9): runs only on your explicit request. Search queries are planned locally; the
/// pipeline searches, reads and synthesises with citations online; the reply lists its sources and says it came from the
/// cloud. Without the gateway, ALDREN says why and carries on locally.
/// </summary>
public sealed class ResearchBrain(ResearchPipeline pipeline, CloudPolicy cloud, ResidencyManager residency, ILogger<ResearchBrain> log)
{
    public const string SourcesMarker = "\n\nSources:\n";

    public async Task<(string Reply, string Route)> RunAsync(string question, CancellationToken ct)
    {
        if (cloud.Unavailable(CloudRole.Research) is { } reason)
        {
            return ($"I can't research online right now: {reason}.", "research (unavailable)");
        }

        try
        {
            var result = await pipeline.RunAsync(question, await PlanAsync(question, ct), ct);
            var sources = string.Join("\n", result.Sources.Select(s => $"[{s.Number}] {s.Title} — {s.Url}"));
            return ($"{result.Answer}{SourcesMarker}{sources}\n(Researched online via {result.Provider} in {result.Elapsed.TotalSeconds:F0} s.)", "research brain");
        }
        catch (CloudUnavailableException ex)
        {
            return ($"I couldn't finish that research: {ex.Message}.", "research (failed)");
        }
    }

    /// <summary>Up to three search queries, planned by the local model (nothing leaves the PC for this step).</summary>
    private async Task<IReadOnlyList<string>> PlanAsync(string question, CancellationToken ct)
    {
        try
        {
            var (client, _) = await residency.AcquireAsync(ct);
            var reply = await client.CompleteAsync(
            [
                new JsonObject { ["role"] = "system", ["content"] = "You turn questions into web search queries. Reply with one to three short queries, one per line, nothing else." },
                new JsonObject { ["role"] = "user", ["content"] = question },
            ], null, 60, ct);
            return reply.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(q => q.TrimStart('-', '*', '•', ' ', '1', '2', '3', '.', ')').Trim('"'))
                .Where(q => q.Length > 2).Take(3).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogInformation("Query planning fell back to the question itself: {Reason}", ex.Message);
            return [question];
        }
    }
}

/// <summary>
/// The Quick Brain's cloud fallback (ARCHITECTURE §8.4): off unless you switch it on; then used only when no local model can
/// answer, and only with the utterance and the persona.
/// </summary>
public sealed class CloudFallback(CloudPolicy cloud, SettingsStore settings, ILogger<CloudFallback> log) : ICloudFallback
{
    public async Task<string?> TryAnswerAsync(string utterance, CancellationToken ct)
    {
        if (!settings.Current.Cloud.QuickFallback || cloud.Unavailable(CloudRole.Fast) is not null)
        {
            return null;
        }

        try
        {
            var answer = await cloud.AskAsync(CloudRole.Fast, "Quick answer", new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = Persona.SystemPrompt + "\nYou are answering from the cloud because the local model is unavailable; you have no tools." },
                new JsonObject { ["role"] = "user", ["content"] = utterance },
            }, PrivacyClass.Personal, utterance, 300, null, ct);
            return answer.Content + " (answered online)";
        }
        catch (CloudUnavailableException ex)
        {
            log.LogInformation("Cloud fallback unavailable: {Reason}", ex.Message);
            return null;
        }
    }
}

/// <summary>The cloud indicator, shown by the App (tray tooltip and the island's researching state).</summary>
public sealed class AppCloudIndicator(AppConnections connections) : ICloudIndicator
{
    public void Show(string? activity)
    {
        if (connections.Current is { } app)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await app.App.ShowCloudAsync(activity, app.Disconnected);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // The App is gone; the audit log still records the request.
                }
            });
        }
    }
}
