using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aldren.Core.Focus;
using Aldren.Inference;
using Aldren.Memory;
using Aldren.Perception;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Aldren.Windows;
using Aldren.Windows.Apps;
using Aldren.Windows.Files;

namespace Aldren.Core.Tools;

// ---------------------------------------------------------------- focus (Phase 8)

public sealed record FocusArgs(string Goal, int Minutes = 50, string Profile = "study");

public sealed class StartFocusTool(IServiceProvider services) : Tool<FocusArgs, SpokenResult>
{
    public override string Name => "start_focus";
    public override string Description => "Start a focus session: goal, minutes (default 50), profile study or work; blocks distracting apps and sites until it ends";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Focus;

    public override IEnumerable<string> Validate(FocusArgs args)
    {
        if (args.Minutes is < 5 or > 240)
        {
            yield return "minutes must be 5–240.";
        }
    }

    protected override async Task<SpokenResult> ExecuteAsync(FocusArgs args, CancellationToken ct)
    {
        var focus = (FocusService)services.GetService(typeof(FocusService))!;
        var goal = string.IsNullOrWhiteSpace(args.Goal) ? "your work" : args.Goal.Trim();
        var session = await focus.StartAsync(goal, args.Minutes, args.Profile, ct);
        return new SpokenResult($"Focus on: {goal}, until {focus.Local(session.Ends)}. I'll keep distractions away; say \"emergency exit\" if you must stop.");
    }
}

public sealed class EndFocusTool(IServiceProvider services) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "end_focus";
    public override string Description => "End the focus session now (the emergency exit); lifts every restriction";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Focus;

    protected override async Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var session = await ((FocusService)services.GetService(typeof(FocusService))!).EndAsync(early: true, ct);
        return new SpokenResult(session is null ? "There's no focus session running." : $"Focus session ended. Everything's unblocked. You did {(int)(DateTimeOffset.UtcNow - session.Started).TotalMinutes} minutes on {session.Goal}.");
    }
}

public sealed class FocusStatusTool(IServiceProvider services) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "focus_status";
    public override string Description => "How the current focus session is going, and this week's focus stats";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.Focus;

    protected override async Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var focus = (FocusService)services.GetService(typeof(FocusService))!;
        var stats = await focus.StatsAsync(7, ct);
        var week = $"This week: {stats.Sessions} session{(stats.Sessions == 1 ? "" : "s")}, {stats.Minutes} minutes, {stats.Slips} slip{(stats.Slips == 1 ? "" : "s")}.";
        return focus.Active is { } s
            ? new SpokenResult($"Focusing on {s.Goal} until {focus.Local(s.Ends)}, {s.Slips} slip{(s.Slips == 1 ? "" : "s")} so far. {week}")
            : new SpokenResult($"No focus session right now. {week}");
    }
}

public sealed record EnforceArgs(string App, string Action);

/// <summary>Focus enforcement (minimise or close a distracting app), called only by the focus service so it's audited.</summary>
public sealed class FocusEnforceTool : Tool<EnforceArgs, SpokenResult>
{
    public override string Name => "focus_enforce";
    public override string Description => "Focus mode: minimise or close a distracting app (internal)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.FocusEnforce;

    public override IEnumerable<string> Validate(EnforceArgs args)
    {
        if (args.Action is not ("minimize" or "close"))
        {
            yield return "action must be minimize or close.";
        }
    }

    protected override Task<SpokenResult> ExecuteAsync(EnforceArgs args, CancellationToken ct)
    {
        var windows = WindowFinder.List().Where(w => w.ProcessName.Equals(args.App, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var w in windows)
        {
            if (args.Action == "minimize")
            {
                Aldren.Windows.Layouts.WindowManager.Place(w.Handle, Aldren.Windows.Layouts.Placement.Minimize);
            }
            else
            {
                WindowFinder.RequestClose(w.Handle);
            }
        }

        return Task.FromResult(new SpokenResult($"{args.Action}: {windows.Count} window(s) of {args.App}."));
    }
}

// ---------------------------------------------------------------- developer assistant (Phase 9)

public sealed record RepoArgs(string Path = "");

/// <summary>Read-only git: three fixed commands, no shell, only in your own folders.</summary>
public sealed partial class GitTool : Tool<RepoArgs, ReadResult>
{
    public override string Name => "git_overview";
    public override string Description => "Read a git repository's branch, changed files, last 10 commits and diff summary (path, or empty for the folder open in VS Code)";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.DevGit;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);

    [GeneratedRegex(@" - (?<folder>[^-]+?) - Visual Studio Code$")]
    private static partial Regex VsCodeTitle();

    protected override async Task<ReadResult> ExecuteAsync(RepoArgs args, CancellationToken ct)
    {
        var repo = Resolve(args.Path) ?? throw new InvalidOperationException("Which repository? Open it in VS Code or give me its folder.");
        if (FileOperations.Refusal(repo) is { } reason)
        {
            throw new InvalidOperationException($"I won't look in {repo}: {reason}.");
        }

        var status = await GitAsync(repo, ct, "status", "--short", "--branch");
        var log = await GitAsync(repo, ct, "log", "--oneline", "-10");
        var diff = await GitAsync(repo, ct, "diff", "--stat");
        return new ReadResult($"Git overview of {repo}.", $"$ git status --short --branch\n{status}\n$ git log --oneline -10\n{log}\n$ git diff --stat\n{diff}");
    }

    private static string? Resolve(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Directory.Exists(path) ? path : null;
        }

        // The folder VS Code shows in its title, matched against its recent list for the full path.
        var title = WindowFinder.List().FirstOrDefault(w => w.ProcessName.Equals("Code", StringComparison.OrdinalIgnoreCase))?.Title ?? "";
        var folder = VsCodeTitle().Match(title) is { Success: true } m ? m.Groups["folder"].Value.Trim() : null;
        var recent = FileFinder.VsCodeRecent(20);
        return (folder is null ? recent.FirstOrDefault() : recent.FirstOrDefault(p => Path.GetFileName(p.TrimEnd('\\')).Equals(folder, StringComparison.OrdinalIgnoreCase)))
               is { } found && Directory.Exists(found) ? found : null;
    }

    private static async Task<string> GitAsync(string repo, CancellationToken ct, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git isn't installed.");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var text = process.ExitCode == 0 ? output : error;
        return text.Length > 3000 ? text[..3000] + "…" : text.TrimEnd();
    }
}

// ---------------------------------------------------------------- routines (Phase 9)

public sealed record Routine(string Name, IReadOnlyList<string> Steps);

/// <summary>Named routines in ALDREN's data folder; the orchestrator runs their steps one by one through the usual path.</summary>
public sealed class RoutineStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<Routine> All()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<Routine>>(File.ReadAllText(path), Json) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public Routine? Find(string name) => All().FirstOrDefault(r => r.Name == Normalize(name));

    public Routine Save(string name, IReadOnlyList<string> steps)
    {
        var routine = new Routine(Normalize(name), steps);
        var all = All().Where(r => r.Name != routine.Name).Append(routine).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(all, Json));
        return routine;
    }

    public bool Delete(string name)
    {
        var all = All().ToList();
        var removed = all.RemoveAll(r => r.Name == Normalize(name)) > 0;
        File.WriteAllText(path, JsonSerializer.Serialize(all, Json));
        return removed;
    }

    public static string Normalize(string name) => AppIndex.Normalize(name).Replace(" routine", "").Trim();
}

public sealed record RoutineArgs(string Name, string Steps);

public sealed class CreateRoutineTool(RoutineStore routines) : Tool<RoutineArgs, SpokenResult>
{
    public override string Name => "create_routine";
    public override string Description => "Save a routine: a name, and its steps as the user said them, separated by commas or \"then\"";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Routines;

    /// <summary>
    /// "open Notepad, then set a timer for 5 minutes" → two steps. Steps end at commas, semicolons, "then", "and then",
    /// sentence ends and line breaks; at most ten.
    /// </summary>
    public static IReadOnlyList<string> SplitSteps(string steps) =>
        Regex.Split(steps, @"\s+and then\s+|\s*[,;]\s*(?:and then\s+|and\s+|then\s+)?|\s+then\s+|\.\s+(?:and then\s+|then\s+|and\s+)?|\r?\n", RegexOptions.IgnoreCase)
            .Select(s => Regex.Replace(s.Trim(), @"^(?:and then|then|and)\s+", "", RegexOptions.IgnoreCase).TrimEnd('.', '!').Trim())
            .Where(s => s.Length > 1)
            .Take(10)
            .ToList();

    protected override Task<SpokenResult> ExecuteAsync(RoutineArgs args, CancellationToken ct)
    {
        var steps = SplitSteps(args.Steps);
        if (steps.Count == 0)
        {
            throw new InvalidOperationException("What should the routine do?");
        }

        var routine = routines.Save(args.Name, steps);
        return Task.FromResult(new SpokenResult($"Saved the \"{routine.Name}\" routine with {steps.Count} steps: {string.Join("; ", steps)}. Say \"{routine.Name}\" to run it."));
    }
}

public sealed class ListRoutinesTool(RoutineStore routines) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "list_routines";
    public override string Description => "List the saved routines";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.Routines;

    protected override Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var all = routines.All();
        return Task.FromResult(new SpokenResult(all.Count == 0 ? "No routines yet." : $"Routines: {string.Join("; ", all.Select(r => $"{r.Name} ({r.Steps.Count} steps)"))}."));
    }
}

// ---------------------------------------------------------------- voice typing (Phase 9)

public sealed class TypeTextTool(UiaService uia, ResidencyManager residency, Aldren.Core.Ipc.AppConnections connections) : Tool<TextArgs, SpokenResult>
{
    public override string Name => "type_text";
    public override string Description => "Voice typing: type dictated text into the focused field of the window in use";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.Dictation;

    public override RiskTier TierFor(TextArgs args) => SensitiveDetector.Check(args.Text) is null ? Tier : RiskTier.Forbidden;

    protected override async Task<SpokenResult> ExecuteAsync(TextArgs args, CancellationToken ct)
    {
        PressControlTool.Guard(residency);
        // Asked from ALDREN's own text field: it holds the keyboard, so it hands focus back to your window first.
        if (connections.Current is { } app && ForegroundProcessId() == app.ProcessId)
        {
            await app.App.YieldFocusAsync(ct);
            await Task.Delay(200, ct);
            if (ForegroundProcessId() == app.ProcessId)
            {
                throw new InvalidOperationException("Click into the field you want me to type in, then ask again.");
            }
        }

        var snapshot = uia.Snapshot();
        if (snapshot.FocusedId is { } id && snapshot.Find(id) is { IsPassword: true })
        {
            throw new InvalidOperationException("That's a password field; I never type into those.");
        }

        Keyboard.TypeText(args.Text);
        return new SpokenResult("");
    }

    private static int ForegroundProcessId()
    {
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        return (int)processId;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}
