using System.Diagnostics;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Aldren.Windows.Apps;

namespace Aldren.Core.Tools;

public sealed record AppArgs(string Name);

public sealed record AppResult(string App, string Detail);

public sealed class LaunchAppTool(AppIndex index, Aldren.Browser.BrowserBridge browser) : Tool<AppArgs, AppResult>
{
    public override string Name => "launch_app";
    public override string Description => "Open an installed application by name";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.AppsLaunch;

    public override IEnumerable<string> Validate(AppArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Name) || args.Name.Length > 100)
        {
            yield return "name must be 1–100 characters.";
        }
    }

    public override string Summarize(AppArgs args) => $"Open {args.Name}";

    protected override async Task<AppResult> ExecuteAsync(AppArgs args, CancellationToken ct)
    {
        var match = await index.FindAsync(args.Name, ct);
        if (match is null)
        {
            // No such app, but a website by that name ("open youtube"): open the site.
            var site = WebSites.ByName(args.Name) ?? WebSites.AddressOf(args.Name)
                       ?? throw new InvalidOperationException($"I couldn't find an app called \"{args.Name}\".");
            return new AppResult(await WebSites.OpenAsync(browser, site, ct), "opened");
        }

        // AppsFolder ids cover desktop and Store apps alike; the shell resolves and starts them non-elevated.
        using var _ = Process.Start(new ProcessStartInfo($@"shell:AppsFolder\{match.App.AppId}") { UseShellExecute = true });
        return new AppResult(match.App.Name, "opened");
    }
}

public sealed class FocusAppTool : Tool<AppArgs, AppResult>
{
    public override string Name => "focus_app";
    public override string Description => "Switch to an app that is already open";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.AppsFocus;

    public override string Summarize(AppArgs args) => $"Switch to {args.Name}";

    protected override Task<AppResult> ExecuteAsync(AppArgs args, CancellationToken ct)
    {
        var window = WindowFinder.Find(args.Name).FirstOrDefault()
                     ?? throw new InvalidOperationException($"{args.Name} doesn't seem to be open.");
        var focused = WindowFinder.Focus(window.Handle);
        return Task.FromResult(new AppResult(WindowFinder.Describe(window), focused ? "focused" : "brought forward (Windows may only flash it)"));
    }
}

public sealed class CloseAppTool : Tool<AppArgs, AppResult>
{
    public override string Name => "close_app";
    public override string Description => "Close an open app's windows, as its close button would (it may still ask to save)";

    // Closing can lose unsaved work, so it asks first.
    public override RiskTier Tier => RiskTier.Impactful;
    public override string Permission => PermissionCatalog.AppsClose;

    public override string Summarize(AppArgs args)
    {
        var windows = WindowFinder.Find(args.Name);
        return windows.Count == 0
            ? $"Close {args.Name}"
            : $"Close {WindowFinder.Describe(windows[0])}: {string.Join(", ", windows.Select(w => $"\"{w.Title}\"").Take(4))}";
    }

    protected override async Task<AppResult> ExecuteAsync(AppArgs args, CancellationToken ct)
    {
        var windows = WindowFinder.Find(args.Name);
        if (windows.Count == 0)
        {
            throw new InvalidOperationException($"{args.Name} doesn't seem to be open.");
        }

        foreach (var w in windows)
        {
            WindowFinder.RequestClose(w.Handle);
        }

        await Task.Delay(1500, ct);
        var open = windows.Count(w => WindowFinder.Exists(w.Handle));
        return new AppResult(
            WindowFinder.Describe(windows[0]),
            open == 0 ? "closed" : $"asked to close; {open} window(s) still open (maybe asking to save)");
    }
}
