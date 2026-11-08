using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aldren.Contracts;
using Aldren.Memory;
using Aldren.Security.Audit;
using Aldren.Security.Gateway;
using Aldren.Security.Tools;
using Microsoft.Extensions.Logging;

namespace Aldren.Cloud;

/// <summary>SECURITY.md's egress classes.</summary>
public enum PrivacyClass
{
    Public,
    Personal,
    Sensitive,
}

public sealed record CloudAnswer(string Content, string Provider, string Model, TimeSpan Elapsed);

public sealed record SearchHit(string Title, string Url, string Content);

public sealed class CloudUnavailableException(string message) : Exception(message);

/// <summary>Shows that something is going to the cloud (ARCHITECTURE §8.3: no cloud call without an indicator).</summary>
public interface ICloudIndicator
{
    void Show(string? activity);
}

/// <summary>
/// The Cloud Policy Layer (ARCHITECTURE §8.3, ADR-0012). The only way anything leaves the PC: a role maps to its curated
/// <c>auto:aldren-*</c> chain (never a bare model name); secrets never leave; sensitive content needs your yes with a
/// preview; first-token deadlines cut hung providers short; <c>X-Routed-Via</c> must name a provider the chain allows, or
/// the chain is blocked and you're told; every request is audited (purpose and provider, never the prompt).
/// </summary>
public sealed class CloudPolicy(
    GatewaySupervisor gateway,
    IConfirmationBroker confirmations,
    IAuditLog audit,
    ICloudIndicator indicator,
    Func<bool> enabled,
    ILogger<CloudPolicy> log)
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static TimeSpan FirstTokenDeadline(CloudRole role) => role switch
    {
        CloudRole.Fast => TimeSpan.FromSeconds(2.5),
        CloudRole.Reason => TimeSpan.FromSeconds(10),
        CloudRole.Vision => TimeSpan.FromSeconds(15),
        _ => TimeSpan.FromSeconds(20),
    };

    /// <summary>Why the role can't be used right now, or null when it can.</summary>
    public string? Unavailable(CloudRole role)
    {
        if (!enabled())
        {
            return "cloud access is switched off in Settings > Cloud";
        }

        var status = gateway.Status;
        if (!status.Running || gateway.BaseUri is null)
        {
            return $"the cloud gateway isn't running ({status.Problem ?? "starting"})";
        }

        if (status.Drift is not { } drift)
        {
            return "the gateway's configuration hasn't been checked yet";
        }

        if (!drift.CloudAllowed)
        {
            return $"the gateway has keys enabled for providers ALDREN doesn't allow ({string.Join(", ", drift.KeysOutsideAllowlist)}); switch them off in its dashboard";
        }

        var chain = drift.For(role);
        return chain.State == ChainState.Ok ? null : $"the {chain.Name} chain isn't ready: {chain.Problem}";
    }

    public async Task<CloudAnswer> AskAsync(
        CloudRole role, string purpose, JsonArray messages, PrivacyClass privacy, string preview, int maxTokens,
        Action<string>? onText, CancellationToken ct)
    {
        if (Unavailable(role) is { } reason)
        {
            throw new CloudUnavailableException(reason);
        }

        // Secrets never leave the PC, whatever the class (SECURITY.md).
        if (SensitiveDetector.Check(messages.ToJsonString()) is { } secret)
        {
            throw new CloudUnavailableException($"the request contains {secret}, which never leaves this PC");
        }

        if (privacy == PrivacyClass.Sensitive)
        {
            var request = new ConfirmationRequest(Guid.NewGuid().ToString("N"), $"cloud:{GatewayDatabase.ChainName(role)}",
                RiskTier.Impactful.ToString(), $"Send to the cloud ({GatewayDatabase.ChainName(role)}): {preview}", ["cloud"], 60);
            if (!await confirmations.ConfirmAsync(request, ct))
            {
                throw new CloudUnavailableException("you declined sending it to the cloud");
            }
        }

        var chain = GatewayDatabase.ChainName(role);
        var body = new JsonObject
        {
            ["model"] = $"auto:{chain}",
            ["messages"] = messages.DeepClone(),
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0.3,
            ["stream"] = true,
        };

        var clock = Stopwatch.StartNew();
        indicator.Show($"{purpose} (online)");
        string provider = "", model = "";
        var status = ToolOutcomeStatus.Failed;
        string detail = "";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(gateway.BaseUri!, "v1/chat/completions"))
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key());
            request.Headers.Host = $"localhost:{gateway.Status.Port}";

            using var firstToken = CancellationTokenSource.CreateLinkedTokenSource(ct);
            firstToken.CancelAfter(FirstTokenDeadline(role) + TimeSpan.FromSeconds(2));
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, firstToken.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new CloudUnavailableException($"the gateway answered {(int)response.StatusCode}: {Shorten(await response.Content.ReadAsStringAsync(ct))}");
            }

            (provider, model) = Routed(response);
            var text = new StringBuilder();
            var gotFirst = false;
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
            while (await ReadLineAsync(reader, gotFirst ? ct : firstToken.Token) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line[5..].Trim();
                if (data == "[DONE]")
                {
                    break;
                }

                var delta = JsonNode.Parse(data)?["choices"]?[0]?["delta"]?["content"];
                if (delta is JsonValue v && v.TryGetValue(out string? piece) && piece.Length > 0)
                {
                    gotFirst = true;
                    text.Append(piece);
                    onText?.Invoke(piece);
                }
            }

            // Headers arrive before the provider is final on some paths; trust only a provider the chain allows.
            if (provider.Length == 0 || !GatewayDatabase.ChainProviders[role].Contains(provider) || !GatewayDatabase.Allowlist.Contains(provider))
            {
                gateway.BlockChain(role, $"answered via '{provider}/{model}', outside the chain's providers");
                log.LogWarning("Routing mismatch on {Chain}: {Provider}/{Model}; chain blocked", chain, provider, model);
                throw new CloudUnavailableException($"the {chain} answer came through '{provider}', which isn't allowed; I've blocked that chain until you check it in the gateway dashboard");
            }

            status = ToolOutcomeStatus.Succeeded;
            detail = $"{provider}/{model}, {clock.ElapsedMilliseconds} ms, {text.Length} characters";
            return new CloudAnswer(text.ToString().Trim(), provider, model, clock.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            status = ToolOutcomeStatus.TimedOut;
            detail = $"no answer within the {FirstTokenDeadline(role).TotalSeconds:F1} s deadline";
            throw new CloudUnavailableException($"the {chain} chain didn't answer in time");
        }
        catch (HttpRequestException ex)
        {
            detail = ex.Message;
            throw new CloudUnavailableException("the cloud gateway couldn't be reached");
        }
        catch (CloudUnavailableException ex)
        {
            detail = ex.Message;
            throw;
        }
        finally
        {
            indicator.Show(null);
            await AuditAsync($"cloud:{chain}", purpose, privacy, status, detail.Length > 0 ? detail : "failed", clock.Elapsed);
        }
    }

    /// <summary>The gateway's search layer (public query text only).</summary>
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int max, CancellationToken ct)
    {
        if (!enabled() || gateway.BaseUri is null)
        {
            throw new CloudUnavailableException(enabled() ? "the cloud gateway isn't running" : "cloud access is switched off");
        }

        if (SensitiveDetector.Check(query) is { } secret)
        {
            throw new CloudUnavailableException($"the query contains {secret}, which never leaves this PC");
        }

        var clock = Stopwatch.StartNew();
        indicator.Show("Searching online");
        var status = ToolOutcomeStatus.Failed;
        var detail = "";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(gateway.BaseUri, "v1/search"))
            {
                Content = new StringContent(new JsonObject { ["query"] = query, ["max_results"] = max }.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key());
            request.Headers.Host = $"localhost:{gateway.Status.Port}";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await Http.SendAsync(request, timeout.Token);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                detail = $"{(int)response.StatusCode}: {Shorten(json)}";
                throw new CloudUnavailableException("the gateway's search didn't work (is a search engine key added in its dashboard?)");
            }

            var hits = new List<SearchHit>();
            foreach (var hit in JsonNode.Parse(json)?["results"]?.AsArray() ?? [])
            {
                var url = hit?["url"]?.GetValue<string>();
                if (url is not null)
                {
                    hits.Add(new SearchHit(hit?["title"]?.GetValue<string>() ?? url, url, hit?["content"]?.GetValue<string>() ?? ""));
                }
            }

            status = ToolOutcomeStatus.Succeeded;
            detail = $"{hits.Count} results via {JsonNode.Parse(json)?["engine"]}";
            return hits;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            detail = ex.Message;
            throw new CloudUnavailableException("the gateway's search couldn't be reached");
        }
        finally
        {
            indicator.Show(null);
            await AuditAsync("cloud:search", "web search", PrivacyClass.Public, status, detail, clock.Elapsed);
        }
    }

    private string Key() => gateway.Database.UnifiedKey() ?? throw new CloudUnavailableException("the gateway has no API key yet; open its dashboard once to finish its setup");

    private static (string Provider, string Model) Routed(HttpResponseMessage response)
    {
        var header = response.Headers.TryGetValues("X-Routed-Via", out var values) ? values.FirstOrDefault() : null;
        var slash = header?.IndexOf('/') ?? -1;
        return slash <= 0 ? ("", "") : (Uri.UnescapeDataString(header![..slash]), Uri.UnescapeDataString(header[(slash + 1)..]));
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct) => await reader.ReadLineAsync(ct);

    private async Task AuditAsync(string tool, string purpose, PrivacyClass privacy, ToolOutcomeStatus status, string detail, TimeSpan elapsed)
    {
        try
        {
            await audit.AppendAsync(new AuditRecord(DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), tool, RiskTier.Read, CallOrigin.Model,
                JsonSerializer.Serialize(new { purpose, privacy = privacy.ToString() }), AuditStage.Finished, status, detail, elapsed,
                Confirmed: privacy == PrivacyClass.Sensitive, Tainted: false), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.LogWarning(ex, "Couldn't audit a cloud request");
        }
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
