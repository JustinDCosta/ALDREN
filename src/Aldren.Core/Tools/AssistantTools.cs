using System.Globalization;
using Aldren.Core.Assistant;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;

namespace Aldren.Core.Tools;

public sealed record NoArgs;

public sealed record DateResult(string Date, string DayOfWeek);

public sealed class GetDateTool(TimeProvider time) : Tool<NoArgs, DateResult>
{
    public override string Name => "get_date";
    public override string Description => "Tell today's date";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.SystemClock;

    protected override Task<DateResult> ExecuteAsync(NoArgs args, CancellationToken ct)
    {
        var now = time.GetLocalNow();
        return Task.FromResult(new DateResult(now.ToString("D", CultureInfo.CurrentCulture), now.DayOfWeek.ToString()));
    }
}

public sealed record CalculateArgs(string Expression);

public sealed record CalculateResult(string Expression, string Result);

public sealed class CalculateTool : Tool<CalculateArgs, CalculateResult>
{
    public override string Name => "calculate";
    public override string Description => "Evaluate an arithmetic expression (+ - * / % ^, parentheses, sqrt, abs, round, pi)";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.Calculator;

    public override IEnumerable<string> Validate(CalculateArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Expression) || args.Expression.Length > 200)
        {
            yield return "expression must be 1–200 characters.";
        }
    }

    protected override Task<CalculateResult> ExecuteAsync(CalculateArgs args, CancellationToken ct)
    {
        var value = Calculator.Evaluate(args.Expression);
        var text = Math.Abs(value) >= 1e15 || (value != 0 && Math.Abs(value) < 1e-6)
            ? value.ToString("G6", CultureInfo.CurrentCulture)
            : Math.Round(value, 6).ToString("#,0.######", CultureInfo.CurrentCulture);
        return Task.FromResult(new CalculateResult(args.Expression, text));
    }
}

public sealed record TimerArgs(int Seconds, string? Label = null);

public sealed record TimerResult(string Duration, string Ends, string? Label);

public sealed class StartTimerTool(TimerService timers, TimeProvider time) : Tool<TimerArgs, TimerResult>
{
    public override string Name => "start_timer";
    public override string Description => "Start a countdown timer (seconds, up to 24 hours) with an optional label";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Timers;

    public override IEnumerable<string> Validate(TimerArgs args)
    {
        if (args.Seconds is < 1 or > 86_400)
        {
            yield return "seconds must be 1–86400.";
        }

        if (args.Label is { Length: > 80 })
        {
            yield return "label must be at most 80 characters.";
        }
    }

    public override string Summarize(TimerArgs args) => $"Start a {TimerService.Describe(TimeSpan.FromSeconds(args.Seconds))} timer";

    protected override Task<TimerResult> ExecuteAsync(TimerArgs args, CancellationToken ct)
    {
        var duration = TimeSpan.FromSeconds(args.Seconds);
        timers.Start(duration, args.Label);
        var ends = time.GetLocalNow() + duration;
        return Task.FromResult(new TimerResult(TimerService.Describe(duration), ends.ToString("t", CultureInfo.CurrentCulture), args.Label));
    }
}

public sealed record DelegateArgs(string Request);

/// <summary>The Quick Brain hands screen and PC-operation requests over; the Action Brain arrives in Phase 5.</summary>
public sealed class DelegateToActionTool : Tool<DelegateArgs, string>
{
    public override string Name => "delegate_to_action";
    public override string Description => "Hand over a request that needs to see the screen or operate apps, windows, files or the browser";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.Delegation;

    protected override Task<string> ExecuteAsync(DelegateArgs args, CancellationToken ct) =>
        Task.FromResult("not_available_yet");
}

/// <summary>The Quick Brain asks for online research; the Research Brain arrives in Phase 6.</summary>
public sealed class RequestResearchTool : Tool<DelegateArgs, string>
{
    public override string Name => "request_research";
    public override string Description => "Ask for online research when the answer needs current information from the web";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.Delegation;

    protected override Task<string> ExecuteAsync(DelegateArgs args, CancellationToken ct) =>
        Task.FromResult("not_available_yet");
}
