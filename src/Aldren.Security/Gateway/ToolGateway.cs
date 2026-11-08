using System.Text.Json;
using System.Text.Json.Nodes;
using Aldren.Common.Logging;
using Aldren.Contracts;
using Aldren.Security.Audit;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Microsoft.Extensions.Logging;

namespace Aldren.Security.Gateway;

/// <summary>
/// The only way anything runs. In SECURITY.md's order: resolve → bind and validate → permission → policies →
/// audit "started" → confirmation → run with timeout → post-condition → audit "finished" → sanitised result.
/// Every call is audited, refused ones included.
/// </summary>
public sealed class ToolGateway(
    ToolRegistry registry,
    IPermissionStore permissions,
    IEnumerable<IToolPolicy> policies,
    IConfirmationBroker confirmations,
    IAuditLog audit,
    TimeProvider time,
    ILogger<ToolGateway> logger)
{
    public static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(60);
    public const int MaxResultLength = 8_192;
    public const int PreviewLength = 8_000;

    private readonly IReadOnlyList<IToolPolicy> _policies = policies.ToList();

    public async Task<ToolOutcome> InvokeAsync(ToolCall call, CallContext context, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var arguments = call.Arguments.ValueKind == JsonValueKind.Undefined ? "" : call.Arguments.GetRawText();

        if (!registry.TryGet(call.Tool, out var tool))
        {
            return await RefuseAsync(call, context, RiskTier.Read, arguments, started,
                ToolOutcomeStatus.UnknownTool, $"There is no tool called '{call.Tool}'.", ct);
        }

        var descriptor = tool.Descriptor;
        var bound = tool.Bind(call.Arguments);
        if (!bound.Success)
        {
            return await RefuseAsync(call, context, descriptor.Tier, arguments, started,
                ToolOutcomeStatus.InvalidArguments, "Invalid arguments: " + string.Join(" ", bound.Errors), ct);
        }

        // A tool may raise its tier for particular arguments ("press Send" over "press Next"), never lower it.
        var tier = tool.TierFor(bound.Value!);
        if (tier > descriptor.Tier)
        {
            descriptor = descriptor with { Tier = tier };
        }

        if (!permissions.IsGranted(descriptor.Permission))
        {
            return await RefuseAsync(call, context, descriptor.Tier, arguments, started,
                ToolOutcomeStatus.PermissionDenied, $"Permission '{descriptor.Permission}' is off. You can turn it on in Settings.", ct);
        }

        var decisions = _policies.Select(p => p.Evaluate(descriptor, context)).ToList();
        if (decisions.FirstOrDefault(d => !d.Allowed) is { } refusal)
        {
            return await RefuseAsync(call, context, descriptor.Tier, arguments, started,
                ToolOutcomeStatus.Forbidden, refusal.Reason ?? $"{descriptor.Name} is not allowed.", ct);
        }

        var level = decisions.Count == 0 ? ConfirmationLevel.None : decisions.Max(d => d.Confirmation);

        try
        {
            await audit.AppendAsync(Record(call, context, descriptor.Tier, arguments, AuditStage.Started, null, null, null, false), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (descriptor.Tier > RiskTier.Read)
            {
                logger.LogError(ex, "Audit log unavailable; refusing {Tool} ({RequestId})", descriptor.Name, call.RequestId);
                return new ToolOutcome(ToolOutcomeStatus.AuditUnavailable, null, "The audit log can't be written, so the action didn't run.");
            }

            logger.LogWarning(ex, "Audit log unavailable; running read-only {Tool} ({RequestId}) anyway", descriptor.Name, call.RequestId);
        }

        var confirmed = false;
        if (level != ConfirmationLevel.None)
        {
            confirmed = await ConfirmAsync(call, context, tool, bound.Value!, arguments, level, ct);
            if (!confirmed)
            {
                return await FinishAsync(call, context, descriptor.Tier, arguments, started,
                    new ToolOutcome(ToolOutcomeStatus.Declined, null, "You declined, so the action didn't run."), false, ct);
            }
        }

        var outcome = await RunAsync(call, tool, bound.Value!, ct);
        return await FinishAsync(call, context, descriptor.Tier, arguments, started, outcome, confirmed, ct);
    }

    private async Task<bool> ConfirmAsync(
        ToolCall call, CallContext context, ITool tool, object args, string arguments, ConfirmationLevel level, CancellationToken ct)
    {
        var summary = tool.Summarize(args);
        if (level == ConfirmationLevel.Explicit)
        {
            summary += Environment.NewLine + "Details: " + Redactor.Redact(arguments);
        }

        var request = new ConfirmationRequest(
            call.RequestId, tool.Descriptor.Name, tool.Descriptor.Tier.ToString(), summary,
            context.UntrustedSources, (int)ConfirmationTimeout.TotalSeconds);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            // WaitAsync as well as the token: a broker that ignores cancellation still can't hold the call open.
            return await confirmations.ConfirmAsync(request, timeout.Token).WaitAsync(ConfirmationTimeout, time, ct);
        }
        catch (TimeoutException)
        {
            logger.LogInformation("Confirmation for {Tool} ({RequestId}) unanswered after {Seconds} s", request.Tool, call.RequestId, request.TimeoutSeconds);
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Confirmation for {Tool} ({RequestId}) failed; treating it as declined", request.Tool, call.RequestId);
            return false;
        }
        finally
        {
            await timeout.CancelAsync();
        }
    }

    private async Task<ToolOutcome> RunAsync(ToolCall call, ITool tool, object args, CancellationToken ct)
    {
        var descriptor = tool.Descriptor;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            object? result;
            try
            {
                result = await tool.RunAsync(args, cancel.Token).WaitAsync(descriptor.Timeout, time, ct);
            }
            catch (TimeoutException)
            {
                await cancel.CancelAsync();
                return new ToolOutcome(ToolOutcomeStatus.TimedOut, null, $"{descriptor.Name} took longer than {descriptor.Timeout.TotalSeconds:0} s and was stopped.");
            }

            if (!await tool.VerifyAsync(args, result, ct))
            {
                return new ToolOutcome(ToolOutcomeStatus.PostconditionFailed, null, $"{descriptor.Name} ran, but its result couldn't be verified.");
            }

            return new ToolOutcome(ToolOutcomeStatus.Succeeded, Sanitize(result), "Done.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "{Tool} ({RequestId}) failed", descriptor.Name, call.RequestId);
            return new ToolOutcome(ToolOutcomeStatus.Failed, null, $"{descriptor.Name} failed: {ShortMessage(ex)}");
        }
    }

    private static JsonNode? Sanitize(object? result)
    {
        var node = JsonSerializer.SerializeToNode(result, ArgumentBinder.Options);
        var text = node?.ToJsonString(ArgumentBinder.Options) ?? "null";
        if (text.Length <= MaxResultLength)
        {
            return node;
        }

        return new JsonObject { ["truncated"] = true, ["preview"] = text[..PreviewLength] };
    }

    private static string ShortMessage(Exception ex)
    {
        var message = Redactor.Redact(ex.Message.ReplaceLineEndings(" "));
        return message.Length <= 200 ? message : message[..200] + "…";
    }

    private Task<ToolOutcome> RefuseAsync(
        ToolCall call, CallContext context, RiskTier tier, string arguments, long started,
        ToolOutcomeStatus status, string message, CancellationToken ct) =>
        FinishAsync(call, context, tier, arguments, started, new ToolOutcome(status, null, message), false, ct);

    private async Task<ToolOutcome> FinishAsync(
        ToolCall call, CallContext context, RiskTier tier, string arguments, long started,
        ToolOutcome outcome, bool confirmed, CancellationToken ct)
    {
        try
        {
            await audit.AppendAsync(
                Record(call, context, tier, arguments, AuditStage.Finished, outcome.Status, outcome.Message, time.GetElapsedTime(started), confirmed),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit log unavailable; {Tool} ({RequestId}) finished as {Status} without a record", call.Tool, call.RequestId, outcome.Status);
        }

        ct.ThrowIfCancellationRequested();
        return outcome;
    }

    private AuditRecord Record(
        ToolCall call, CallContext context, RiskTier tier, string arguments, AuditStage stage,
        ToolOutcomeStatus? status, string? detail, TimeSpan? duration, bool confirmed) =>
        new(time.GetUtcNow(), call.RequestId, call.Tool, tier, context.Origin, arguments, stage, status, detail, duration, confirmed, context.Tainted);
}
