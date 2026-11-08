using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aldren.Inference;
using Aldren.Security.Gateway;
using Aldren.Security.Tools;
using Microsoft.Extensions.Logging;

namespace Aldren.Core.Orchestration;

/// <summary>
/// Typed text in, one reply out (ARCHITECTURE §5): normalise → fast path → router → Quick Brain, which answers or calls
/// tools. Every action goes through the Tool Gateway; model-proposed calls carry <see cref="CallOrigin.Model"/>.
/// </summary>
public sealed partial class Orchestrator(
    ToolGateway gateway,
    ToolRegistry registry,
    ResidencyManager residency,
    ICloudFallback cloud,
    TimeProvider time,
    ILogger<Orchestrator> log)
{
    public const string ActionNotYet = "Seeing your screen and operating apps for you arrives in Phase 5, boss. For now I can open, switch to and close apps, and handle volume, media, timers and questions.";
    public const string ResearchNotYet = "Online research arrives in Phase 6, boss, so I can't look that up yet. I can tell you what I know, if that helps.";

    private const int MaxTurns = 8;
    private const int MaxToolRounds = 3;
    private static readonly TimeSpan BrainTimeout = TimeSpan.FromSeconds(90);
    private static readonly CallContext Typed = new(CallOrigin.User, []);

    /// <summary>Tools the Quick Brain may propose. close_app is left out: closing is something you ask for in words.</summary>
    private static readonly string[] BrainTools =
    [
        "launch_app", "focus_app", "set_volume", "change_volume", "mute", "media_control", "start_timer",
        "calculate", "delegate_to_action", "request_research",
    ];

    private readonly List<(string Role, string Content)> _turns = [];
    private readonly SemaphoreSlim _one = new(1, 1);
    private JsonArray? _toolSchemas;

    [GeneratedRegex(@"\b(look (?:it |that |this )?up|research|search (?:the )?(?:web|online|internet)|google|online|latest|news|current price)\b")]
    private static partial Regex ResearchTrigger();

    [GeneratedRegex(@"\bI(?:'ll| will|'ve| have|'m going to| am going to)(?: just)? (?:start|set|open|launch|play|pause|skip|stop|mute|unmute|turn|switch|close)(?:ed|ing)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClaimsAction();

    [GeneratedRegex(@"\b(on (?:my|the) screen|this error|this window|this page|this file|what am i looking at|whats this|what is this)\b")]
    private static partial Regex ActionTrigger();

    /// <param name="onText">Receives the Quick Brain's reply as it is written (for speech); fast-path replies only come back at the end.</param>
    public async Task<string> HandleAsync(string text, CancellationToken ct, Action<string>? onText = null)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return "I didn't catch anything, boss.";
        }

        if (text.Length > 4000)
        {
            text = text[..4000];
        }

        await _one.WaitAsync(ct);
        var clock = Stopwatch.StartNew();
        try
        {
            var (reply, route) = await RouteAsync(text, onText, ct);
            Remember("user", text);
            Remember("assistant", reply);
            log.LogInformation("Turn handled by {Route} in {Ms} ms", route, clock.ElapsedMilliseconds);
            return reply;
        }
        finally
        {
            _one.Release();
        }
    }

    private async Task<(string Reply, string Route)> RouteAsync(string text, Action<string>? onText, CancellationToken ct)
    {
        if (FastPath.Normalize(text) == "test confirmation")
        {
            var outcome = await InvokeAsync("diagnostics_confirm_test", [], Typed, ct);
            return (outcome.Status == ToolOutcomeStatus.Succeeded ? "Confirmation test passed." : outcome.Message, "fast path");
        }

        switch (FastPath.Match(text))
        {
            case FastToolCommand command:
            {
                var outcome = await InvokeAsync(command.Tool, command.Arguments, Typed, ct);
                return (Describe(command.Tool, outcome), $"fast path ({command.Tool})");
            }
            case GamingCommand gaming:
                await residency.SetGamingOverrideAsync(gaming.On, ct);
                return (gaming.On switch
                {
                    true => "Gaming mode on. I've freed the GPU and I'll stay out of your way, boss.",
                    false => "Gaming mode off. The GPU is mine again when you need me.",
                    null => "Gaming mode is automatic again: I'll step aside whenever a fullscreen game or OBS is running.",
                }, "fast path (gaming)");
            case StatusCommand:
                return (DescribeStatus(), "fast path (status)");
        }

        var normalized = FastPath.Normalize(text);
        if (ResearchTrigger().IsMatch(normalized))
        {
            return (ResearchNotYet, "router (research)");
        }

        if (ActionTrigger().IsMatch(normalized))
        {
            return (ActionNotYet, "router (action)");
        }

        return await QuickBrainAsync(text, onText, ct);
    }

    private async Task<(string Reply, string Route)> QuickBrainAsync(string text, Action<string>? onText, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(BrainTimeout);
        ChatClient client;
        ModelTier tier;
        try
        {
            (client, tier) = await residency.AcquireAsync(timeout.Token);
        }
        catch (ModelUnavailableException ex)
        {
            return (await cloud.TryAnswerAsync(text, ct) ?? ex.Message + " Simple commands like opening apps or the volume still work.", "unavailable");
        }

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = Persona.SystemPrompt },
        };
        foreach (var (role, content) in _turns)
        {
            messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = $"{text}\n\n[Context: {Context()}]" });

        var tools = tier == ModelTier.Gpu ? ToolSchemas() : null;
        try
        {
            for (var round = 0; ; round++)
            {
                // Tools only in the first round: after acting once, the model reports in words. A model that can't get
                // what it wants from a tool otherwise keeps poking it (live: "what's the song?" → play/pause, stop, stop).
                var roundTools = round == 0 ? tools : null;
                var performed = new HashSet<string>(StringComparer.Ordinal);
                var reply = onText is null
                    ? await client.CompleteAsync(messages, roundTools, 400, timeout.Token)
                    : await client.StreamAsync(messages, roundTools, 400, onText, timeout.Token);
                log.LogInformation("{Model} answered in {Ms} ms ({Calls} tool calls)", reply.Model, (int)reply.Elapsed.TotalMilliseconds, reply.ToolCalls.Count);
                if (reply.ToolCalls.Count == 0)
                {
                    var answer = Clean(reply.Content);
                    // The 4B sometimes promises an action without calling the tool ("I'll start a 30-minute timer"): say so.
                    if (round == 0 && ClaimsAction().IsMatch(answer))
                    {
                        log.LogInformation("The model claimed an action without calling a tool");
                        var note = " Actually, I haven't done that yet. Ask me directly and I will.";
                        onText?.Invoke(note);
                        answer += note;
                    }

                    return (answer.Length > 0 ? answer : "I'm not sure what to say to that, boss.", $"quick brain ({tier})");
                }

                var assistant = new JsonObject { ["role"] = "assistant", ["content"] = reply.Content };
                var calls = new JsonArray();
                foreach (var call in reply.ToolCalls)
                {
                    calls.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.ArgumentsJson },
                    });
                }

                assistant["tool_calls"] = calls;
                messages.Add(assistant);

                foreach (var call in reply.ToolCalls)
                {
                    // Handing off ends the turn with a fixed answer: the model shouldn't improvise one.
                    if (call.Name == "delegate_to_action")
                    {
                        await InvokeModelCallAsync(call, ct);
                        return (ActionNotYet, "quick brain → action");
                    }

                    if (call.Name == "request_research")
                    {
                        await InvokeModelCallAsync(call, ct);
                        return (ResearchNotYet, "quick brain → research");
                    }

                    // The same action twice in one reply ("stop", "stop") runs once.
                    var outcome = performed.Add(call.Name + call.ArgumentsJson)
                        ? await InvokeModelCallAsync(call, ct)
                        : new ToolOutcome(ToolOutcomeStatus.Succeeded, null, "Already done above.");
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = call.Id,
                        ["content"] = new JsonObject
                        {
                            ["status"] = outcome.Status.ToString(),
                            ["message"] = outcome.Message,
                            ["result"] = outcome.Result?.DeepClone(),
                        }.ToJsonString(),
                    });
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ("That took too long, boss, so I stopped.", "quick brain (timeout)");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            log.LogWarning(ex, "The Quick Brain failed");
            return ("My local model hit a problem with that one. The details are in the log.", "quick brain (error)");
        }
    }

    private Task<ToolOutcome> InvokeModelCallAsync(ChatToolCall call, CancellationToken ct)
    {
        if (!BrainTools.Contains(call.Name))
        {
            return Task.FromResult(new ToolOutcome(ToolOutcomeStatus.UnknownTool, null, $"{call.Name} isn't available to the model."));
        }

        JsonNode? args;
        try
        {
            args = JsonNode.Parse(call.ArgumentsJson);
        }
        catch (JsonException)
        {
            return Task.FromResult(new ToolOutcome(ToolOutcomeStatus.InvalidArguments, null, "The arguments weren't valid JSON."));
        }

        return InvokeAsync(call.Name, args as JsonObject ?? [], new CallContext(CallOrigin.Model, []), ct);
    }

    private Task<ToolOutcome> InvokeAsync(string tool, JsonObject args, CallContext context, CancellationToken ct)
    {
        var element = JsonDocument.Parse(args.ToJsonString()).RootElement.Clone();
        return gateway.InvokeAsync(new ToolCall(tool, element, Guid.NewGuid().ToString("N")), context, ct);
    }

    private JsonArray ToolSchemas()
    {
        if (_toolSchemas is not null)
        {
            return _toolSchemas;
        }

        var tools = new JsonArray();
        foreach (var d in registry.Descriptors.Where(d => BrainTools.Contains(d.Name)))
        {
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = d.Name,
                    ["description"] = d.Description,
                    ["parameters"] = d.ArgumentSchema.DeepClone(),
                },
            });
        }

        return _toolSchemas = tools;
    }

    private string Context()
    {
        var now = time.GetLocalNow();
        var mode = residency.GamingMode ? "gaming" : "normal";
        return $"{now.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)}; mode: {mode}";
    }

    private void Remember(string role, string content)
    {
        _turns.Add((role, content.Length > 1500 ? content[..1500] : content));
        while (_turns.Count > MaxTurns * 2)
        {
            _turns.RemoveAt(0);
        }
    }

    private string DescribeStatus()
    {
        var s = residency.Status;
        var gpu = s.GpuLoaded ? "Qwen3.5-4B is loaded on the GPU" : "the GPU model is unloaded";
        var cpu = s.CpuLoaded ? ", the CPU fallback is running" : "";
        var gaming = s.GamingMode ? (s.GamingOverride == true ? "gaming mode is on (you set it)" : "gaming mode is on (detected)")
            : s.GamingOverride == false ? "gaming mode is off (you set it)" : "gaming mode is off";
        var vram = s.FreeVramMiB is { } mib ? $"{mib:N0} MiB of VRAM free" : "VRAM unknown";
        return $"Right now {gpu}{cpu}; {gaming}; {(s.OnAc ? "on AC power" : "on battery")}; {vram}.";
    }

    /// <summary>Plain words for a fast-path tool outcome.</summary>
    private static string Describe(string tool, ToolOutcome outcome)
    {
        if (outcome.Status != ToolOutcomeStatus.Succeeded)
        {
            return Friendly(outcome);
        }

        var r = outcome.Result;
        return tool switch
        {
            "get_time" => $"It's {r?["localTime"]}.",
            "get_date" => $"It's {r?["date"]}.",
            "launch_app" => $"Opening {r?["app"]}.",
            "focus_app" => r?["detail"]?.GetValue<string>() == "focused" ? $"Here's {r?["app"]}." : $"I've tried to bring {r?["app"]} forward; Windows may only flash it in the taskbar.",
            "close_app" => r?["detail"]?.GetValue<string>() == "closed" ? $"Closed {r?["app"]}." : $"I've asked {r?["app"]} to close; it may be asking you to save.",
            "set_volume" or "change_volume" => $"Volume at {r?["volume"]}%.",
            "mute" => r?["muted"]?.GetValue<bool>() == true ? "Muted." : $"Unmuted, volume at {r?["volume"]}%.",
            "media_control" => r?.GetValue<string>() switch
            {
                "next" => "Next track.",
                "previous" => "Previous track.",
                "stop" => "Stopped.",
                _ => "Done.",
            },
            "start_timer" => $"Timer set for {r?["duration"]}; it ends at {r?["ends"]}.",
            "calculate" => $"{r?["expression"]} = {r?["result"]}.",
            _ => "Done.",
        };
    }

    private static string Friendly(ToolOutcome outcome) => outcome.Status switch
    {
        ToolOutcomeStatus.Declined => "Okay, I won't.",
        ToolOutcomeStatus.Failed => StripToolName(outcome.Message),
        _ => outcome.Message,
    };

    // "launch_app failed: I couldn't find…" → "I couldn't find…"
    private static string StripToolName(string message)
    {
        var i = message.IndexOf(" failed: ", StringComparison.Ordinal);
        return i > 0 && i < 40 ? message[(i + 9)..] : message;
    }

    /// <summary>Drops any leftover reasoning block and markdown emphasis from a spoken-style reply.</summary>
    private static string Clean(string content)
    {
        var end = content.IndexOf("</think>", StringComparison.Ordinal);
        if (end >= 0)
        {
            content = content[(end + 8)..];
        }

        return content.Replace("**", "").Trim();
    }
}
