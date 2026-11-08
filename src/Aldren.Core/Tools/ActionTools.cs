using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Aldren.Core.Perception;
using Aldren.Inference;
using Aldren.Memory;
using Aldren.Perception;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Aldren.Windows;
using Aldren.Windows.Apps;
using Aldren.Windows.Files;
using Aldren.Windows.Layouts;

namespace Aldren.Core.Tools;

/// <summary>A spoken message plus the details the model needs (paths, IDs).</summary>
public sealed record DetailedResult(string Message, IReadOnlyList<string> Items);

public sealed record PathArgs(string Path);

// ---------------------------------------------------------------- windows

public sealed record MoveWindowArgs(string App, string Placement, int Monitor = 0);

public sealed class ListWindowsTool : Tool<NoArguments, DetailedResult>
{
    public override string Name => "list_windows";
    public override string Description => "List the open app windows, with their monitor and state";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.WindowsManage;

    protected override Task<DetailedResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var items = WindowManager.Describe()
            .Select(w => $"{WindowFinder.Describe(w.Window)}: \"{w.Window.Title}\" on monitor {w.Monitor}{(w.Maximized ? ", maximized" : w.Minimized ? ", minimized" : "")}")
            .ToList();
        return Task.FromResult(new DetailedResult($"{items.Count} windows open: {string.Join("; ", items.Take(8))}.", items));
    }
}

public sealed class MoveWindowTool : Tool<MoveWindowArgs, SpokenResult>
{
    private static readonly Dictionary<string, Placement> Placements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = Placement.Left, ["right"] = Placement.Right, ["top"] = Placement.Top, ["bottom"] = Placement.Bottom,
        ["top_left"] = Placement.TopLeft, ["top_right"] = Placement.TopRight, ["bottom_left"] = Placement.BottomLeft,
        ["bottom_right"] = Placement.BottomRight, ["maximize"] = Placement.Maximize, ["minimize"] = Placement.Minimize,
        ["restore"] = Placement.Restore, ["center"] = Placement.Center, ["other_monitor"] = Placement.OtherMonitor,
    };

    public override string Name => "move_window";
    public override string Description =>
        "Place an app's window. app: the app name, or \"this\" for the window in use. placement: left, right, top, bottom, top_left, top_right, bottom_left, bottom_right, maximize, minimize, restore, center or other_monitor. monitor: 1 = primary (optional)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.WindowsManage;

    public override IEnumerable<string> Validate(MoveWindowArgs args)
    {
        if (!Placements.ContainsKey(args.Placement.Replace(' ', '_').Replace("maximise", "maximize").Replace("minimise", "minimize")))
        {
            yield return "placement must be one of: " + string.Join(", ", Placements.Keys) + ".";
        }
    }

    protected override Task<SpokenResult> ExecuteAsync(MoveWindowArgs args, CancellationToken ct)
    {
        var placement = Placements[args.Placement.Replace(' ', '_').Replace("maximise", "maximize").Replace("minimise", "minimize")];
        var (hwnd, name) = Resolve(args.App);
        MonitorInfo? monitor = null;
        if (args.Monitor > 0)
        {
            var all = WindowManager.Monitors();
            monitor = args.Monitor <= all.Count ? all[args.Monitor - 1] : throw new InvalidOperationException($"There's no monitor {args.Monitor}.");
        }

        return WindowManager.Place(hwnd, placement, monitor)
            ? Task.FromResult(new SpokenResult(placement switch
            {
                Placement.Maximize => $"{name} maximized.",
                Placement.Minimize => $"{name} minimized.",
                Placement.OtherMonitor => $"{name} moved to the other monitor.",
                _ => $"{name} moved {args.Placement.Replace('_', ' ')}.",
            }))
            : throw new InvalidOperationException($"Windows wouldn't let me move {name} (it may be running as administrator).");
    }

    internal static (nint Handle, string Name) Resolve(string app)
    {
        if (app.Trim().ToLowerInvariant() is "this" or "current" or "this window" or "it" or "that" or "the window")
        {
            var target = UiaService.TargetWindow();
            var info = WindowFinder.List().FirstOrDefault(w => w.Handle == target);
            return target != 0 ? (target, info is null ? "The window" : WindowFinder.Describe(info)) : throw new InvalidOperationException("There's no window to move.");
        }

        var window = WindowFinder.Find(app).FirstOrDefault() ?? throw new InvalidOperationException($"{app} doesn't seem to be open.");
        return (window.Handle, WindowFinder.Describe(window));
    }
}

public sealed class SaveLayoutTool(LayoutStore layouts) : Tool<TextArgs, SpokenResult>
{
    public override string Name => "save_layout";
    public override string Description => "Save where the open windows are now as a named layout (for this monitor setup)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.WindowsManage;

    protected override async Task<SpokenResult> ExecuteAsync(TextArgs args, CancellationToken ct)
    {
        var layout = await layouts.SaveCurrentAsync(args.Text, ct);
        return new SpokenResult($"Saved the \"{layout.Name}\" layout with {layout.Windows.Count} windows.");
    }
}

public sealed class ApplyLayoutTool(LayoutStore layouts) : Tool<TextArgs, SpokenResult>
{
    public override string Name => "apply_layout";
    public override string Description => "Arrange windows in a saved layout (\"coding\" is built in), opening apps that aren't running";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.WindowsManage;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(45);

    protected override async Task<SpokenResult> ExecuteAsync(TextArgs args, CancellationToken ct)
    {
        var layout = await layouts.FindAsync(args.Text, ct)
                     ?? throw new InvalidOperationException($"There's no layout called \"{args.Text}\". Say \"save this layout as {args.Text}\" first.");
        var result = await layouts.ApplyAsync(layout, ct);
        var missing = result.Missing.Count > 0 ? $" I couldn't find {string.Join(", ", result.Missing)}." : "";
        return new SpokenResult($"{char.ToUpperInvariant(layout.Name[0])}{layout.Name[1..]} layout applied: {result.Placed} windows placed{(result.Launched > 0 ? $", {result.Launched} apps opened" : "")}.{missing}");
    }
}

public sealed class ListLayoutsTool(LayoutStore layouts) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "list_layouts";
    public override string Description => "List the saved window layouts";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.WindowsManage;

    protected override async Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct) =>
        new($"Layouts: {string.Join(", ", (await layouts.ListAsync(ct)).Select(l => l.Name).Distinct())}.");
}

// ---------------------------------------------------------------- files

public sealed class FindFilesTool : Tool<QueryArgs, DetailedResult>
{
    public override string Name => "find_files";
    public override string Description => "Find files by words in their names (Windows Search index, then your own folders)";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.FilesRead;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);

    protected override async Task<DetailedResult> ExecuteAsync(QueryArgs args, CancellationToken ct) =>
        Files.List(await FileFinder.SearchAsync(args.Query, 10, ct), $"No file names match \"{args.Query}\".");
}

public sealed class RecentFilesTool : Tool<NoArguments, DetailedResult>
{
    public override string Name => "recent_files";
    public override string Description => "List the files you opened recently";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.FilesRead;

    protected override Task<DetailedResult> ExecuteAsync(NoArguments args, CancellationToken ct) =>
        Task.FromResult(Files.List(FileFinder.Recent(10), "Windows has no recent files listed."));
}

public sealed class DownloadsTool : Tool<NoArguments, DetailedResult>
{
    public override string Name => "list_downloads";
    public override string Description => "List the newest files in the Downloads folder";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.FilesRead;

    protected override Task<DetailedResult> ExecuteAsync(NoArguments args, CancellationToken ct) =>
        Task.FromResult(Files.List(FileFinder.LatestDownloads(10), "Your Downloads folder is empty."));
}

public sealed class VsCodeRecentTool : Tool<NoArguments, DetailedResult>
{
    public override string Name => "vscode_recent";
    public override string Description => "List VS Code's recently opened folders and workspaces";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.FilesRead;

    protected override Task<DetailedResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var items = FileFinder.VsCodeRecent(10);
        return Task.FromResult(items.Count == 0
            ? new DetailedResult("I couldn't find VS Code's recent list.", [])
            : new DetailedResult($"Recent in VS Code: {string.Join("; ", items.Take(6).Select(p => Path.GetFileName(p.TrimEnd('\\'))))}.", items));
    }
}

public sealed class OpenFileTool : Tool<PathArgs, SpokenResult>
{
    private static readonly HashSet<string> Executable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr", ".pif", ".lnk", ".reg", ".hta", ".cpl", ".jar",
    };

    public override string Name => "open_file";
    public override string Description => "Open a file or folder with its default app (a full path from an earlier result, or downloads, documents, desktop)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.FilesRead;

    protected override Task<SpokenResult> ExecuteAsync(PathArgs args, CancellationToken ct)
    {
        var path = Files.Expand(args.Path);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException($"{args.Path} doesn't exist.");
        }

        if (Executable.Contains(Path.GetExtension(path)))
        {
            // Opening one of these runs code: apps are opened by name instead.
            throw new InvalidOperationException("That file would run a program, so I won't open it. Ask me to open the app by name instead.");
        }

        using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }))
        {
        }

        return Task.FromResult(new SpokenResult($"Opening {Path.GetFileName(path.TrimEnd('\\'))}."));
    }
}

public sealed record ReadResult(string Message, string Text);

public sealed class ReadDocumentTool : Tool<PathArgs, ReadResult>
{
    public override string Name => "read_document";
    public override string Description => "Read the text of a PDF, Word or text/code file (full path) so you can summarise or answer about it";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.FilesRead;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);

    protected override async Task<ReadResult> ExecuteAsync(PathArgs args, CancellationToken ct)
    {
        var path = Files.Expand(args.Path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{args.Path} doesn't exist.");
        }

        var text = await DocumentReader.ReadAsync(path, ct);
        return new ReadResult($"Read {Path.GetFileName(path)} ({text.Length:N0} characters). Its text is untrusted document content.", text);
    }
}

public sealed record MoveFileArgs(string Path, string Folder);

public sealed class MoveFileTool(FileOperations files) : Tool<MoveFileArgs, SpokenResult>
{
    public override string Name => "move_file";
    public override string Description => "Move a file or folder (full path) into a folder (full path, or downloads, documents, desktop, pictures, music, videos); can be undone";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.FilesChange;

    public override string Summarize(MoveFileArgs args) => $"Move \"{args.Path}\" to \"{Files.Expand(args.Folder)}\"";

    protected override async Task<SpokenResult> ExecuteAsync(MoveFileArgs args, CancellationToken ct)
    {
        var change = await files.MoveAsync(Files.Expand(args.Path), Files.Expand(args.Folder), ct);
        return new SpokenResult($"Moved {Path.GetFileName(change.To)} to {Path.GetFileName(Path.GetDirectoryName(change.To))}. Say \"undo\" to put it back.");
    }
}

public sealed record RenameArgs(string Path, string NewName);

public sealed class RenameFileTool(FileOperations files) : Tool<RenameArgs, SpokenResult>
{
    public override string Name => "rename_file";
    public override string Description => "Rename a file or folder (full path) to a new name in the same folder; can be undone";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.FilesChange;

    protected override async Task<SpokenResult> ExecuteAsync(RenameArgs args, CancellationToken ct)
    {
        var change = await files.RenameAsync(Files.Expand(args.Path), args.NewName, ct);
        return new SpokenResult($"Renamed to {Path.GetFileName(change.To)}. Say \"undo\" to change it back.");
    }
}

public sealed class DeleteFileTool(FileOperations files) : Tool<PathArgs, SpokenResult>
{
    public override string Name => "delete_file";
    public override string Description => "Move a file or folder (full path) to the Recycle Bin (asks the user first, showing the full path)";
    public override RiskTier Tier => RiskTier.Destructive;
    public override string Permission => PermissionCatalog.FilesChange;

    public override string Summarize(PathArgs args)
    {
        var path = Files.Expand(args.Path);
        var size = File.Exists(path) ? $" ({new FileInfo(path).Length / 1024.0:N0} KB)" : Directory.Exists(path) ? " (a folder and everything in it)" : "";
        return $"Move to the Recycle Bin: {path}{size}";
    }

    protected override async Task<SpokenResult> ExecuteAsync(PathArgs args, CancellationToken ct)
    {
        var change = await files.RecycleAsync(Files.Expand(args.Path), ct);
        return new SpokenResult($"{Path.GetFileName(change.From)} is in the Recycle Bin.");
    }
}

public sealed class FindDuplicatesTool : Tool<PathArgs, DetailedResult>
{
    public override string Name => "find_duplicates";
    public override string Description => "Find files with identical content in a folder (full path, or downloads, documents, desktop, pictures…)";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.FilesRead;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(120);

    protected override async Task<DetailedResult> ExecuteAsync(PathArgs args, CancellationToken ct)
    {
        var groups = await FileOperations.DuplicatesAsync(Files.Expand(args.Path), ct);
        if (groups.Count == 0)
        {
            return new DetailedResult("No duplicates there.", []);
        }

        var wasted = groups.Sum(g => g.Size * (g.Paths.Count - 1));
        var items = groups.Take(20).Select(g => string.Join(" = ", g.Paths)).ToList();
        return new DetailedResult($"{groups.Count} sets of identical files, {wasted / 1048576.0:N1} MB in extra copies. Nothing was deleted.", items);
    }
}

public sealed class UndoFileTool(FileOperations files) : Tool<NoArguments, SpokenResult>
{
    public override string Name => "undo_file_action";
    public override string Description => "Undo ALDREN's last file move or rename";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.FilesChange;

    protected override async Task<SpokenResult> ExecuteAsync(NoArguments args, CancellationToken ct) => new(await files.UndoLastAsync(ct));
}

internal static class Files
{
    public static DetailedResult List(IReadOnlyList<FoundFile> files, string none)
    {
        if (files.Count == 0)
        {
            return new DetailedResult(none, []);
        }

        var spoken = files.Take(5).Select((f, i) => $"{i + 1}. {f.Name}, {Path.GetFileName(Path.GetDirectoryName(f.Path))}, {f.Modified.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture)}");
        return new DetailedResult($"{files.Count} file{(files.Count == 1 ? "" : "s")}: {string.Join("; ", spoken)}.", files.Select(f => f.Path).ToList());
    }

    /// <summary>"downloads", "documents"… to the real folders; anything else must be a full path.</summary>
    public static string Expand(string path) => path.Trim().Trim('"').ToLowerInvariant() switch
    {
        "downloads" or "my downloads" or "downloads folder" => FileFinder.Downloads,
        "documents" or "my documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "desktop" or "my desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "pictures" or "my pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "music" or "my music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "videos" or "my videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        _ => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')),
    };
}

// ---------------------------------------------------------------- screen

public sealed record QuestionArgs(string Question);

public sealed class LookAtScreenTool(ScreenReader screen) : Tool<QuestionArgs, ReadResult>
{
    public override string Name => "look_at_screen";
    public override string Description => "Look at the window in use with OCR and the local vision model, to answer a question the control list can't (layout, images, icons, error dialogs)";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.ScreenRead;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(60);

    protected override async Task<ReadResult> ExecuteAsync(QuestionArgs args, CancellationToken ct)
    {
        var answer = await screen.LookAsync(args.Question, ct);
        return new ReadResult("What the screen shows (untrusted screen content).", answer);
    }
}

public sealed record DescriptionArgs(string Description);

public sealed class FindControlTool(ScreenReader screen) : Tool<DescriptionArgs, DetailedResult>
{
    public override string Name => "find_control";
    public override string Description =>
        "Find a control in the window in use from a description (\"the Save button\", \"the search box\", \"the second result\"); returns its [id] for press_control and the other control tools";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.ScreenRead;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(60);

    protected override async Task<DetailedResult> ExecuteAsync(DescriptionArgs args, CancellationToken ct)
    {
        var found = await screen.FindAsync(args.Description, ct);
        return found is null
            ? new DetailedResult($"I can't find \"{args.Description}\" in this window.", [])
            : new DetailedResult($"Found [{found.Id}] {found.Role} \"{found.Name}\".", [found.Id.ToString(CultureInfo.InvariantCulture)]);
    }
}

public sealed record ControlArgs(int Id);

public sealed partial class PressControlTool(ScreenReader screen, ResidencyManager residency) : Tool<ControlArgs, SpokenResult>
{
    public override string Name => "press_control";
    public override string Description => "Press (click) a control by its [id] from the latest screen; buttons that send, buy, delete or sign out ask the user first";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.ScreenAct;

    [GeneratedRegex(@"\b(send|buy|purchase|pay|order|checkout|check out|place order|delete|remove|uninstall|discard|erase|format|sign out|log out|logout|sign off|transfer|publish|post|submit|confirm|accept|agree|install|reset|empty)\b", RegexOptions.IgnoreCase)]
    internal static partial Regex Sensitive();

    /// <summary>Controls that send, pay, delete, sign out and the like need an explicit confirmation (SECURITY.md tier 3).</summary>
    public override RiskTier TierFor(ControlArgs args) =>
        screen.Element(args.Id) is { } e && Sensitive().IsMatch(e.Name) ? RiskTier.Destructive : Tier;

    public override string Summarize(ControlArgs args) =>
        screen.Element(args.Id) is { } e ? $"Press \"{e.Name}\" ({e.Role}) in {screen.Latest?.App}" : $"Press control [{args.Id}]";

    protected override Task<SpokenResult> ExecuteAsync(ControlArgs args, CancellationToken ct)
    {
        Guard(residency);
        return Task.FromResult(new SpokenResult(screen.Act(args.Id, ElementAction.Invoke)));
    }

    /// <summary>No acting on apps during gaming (never any input into games).</summary>
    internal static void Guard(ResidencyManager residency)
    {
        if (residency.GamingMode)
        {
            throw new InvalidOperationException("Gaming mode is on, so I don't operate apps.");
        }
    }
}

public sealed record TypeArgs(int Id, string Text);

public sealed class TypeIntoControlTool(ScreenReader screen, ResidencyManager residency) : Tool<TypeArgs, SpokenResult>
{
    public override string Name => "type_into_control";
    public override string Description => "Put text into a text box by its [id] from the latest screen (replaces what's there; never passwords or card numbers)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.ScreenAct;

    public override IEnumerable<string> Validate(TypeArgs args)
    {
        if (args.Text.Length > 2000)
        {
            yield return "text must be at most 2,000 characters.";
        }
    }

    /// <summary>Passwords, card numbers and the like are never typed (SECURITY.md tier 4).</summary>
    public override RiskTier TierFor(TypeArgs args) => SensitiveDetector.Check(args.Text) is null ? Tier : RiskTier.Forbidden;

    public override string Summarize(TypeArgs args) =>
        $"Type \"{(args.Text.Length > 60 ? args.Text[..60] + "…" : args.Text)}\" into \"{screen.Element(args.Id)?.Name}\"";

    protected override Task<SpokenResult> ExecuteAsync(TypeArgs args, CancellationToken ct)
    {
        PressControlTool.Guard(residency);
        return Task.FromResult(new SpokenResult(screen.Act(args.Id, ElementAction.SetText, args.Text)));
    }
}

public sealed record OperateArgs(int Id, string Action);

public sealed class OperateControlTool(ScreenReader screen, ResidencyManager residency) : Tool<OperateArgs, SpokenResult>
{
    private static readonly Dictionary<string, ElementAction> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["select"] = ElementAction.Select, ["toggle"] = ElementAction.Toggle, ["expand"] = ElementAction.Expand,
        ["scroll_down"] = ElementAction.ScrollDown, ["scroll_up"] = ElementAction.ScrollUp, ["focus"] = ElementAction.Focus,
    };

    public override string Name => "operate_control";
    public override string Description => "Do something to a control by its [id]: action is select, toggle, expand, scroll_down, scroll_up or focus";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.ScreenAct;

    public override IEnumerable<string> Validate(OperateArgs args)
    {
        if (!Actions.ContainsKey(args.Action))
        {
            yield return "action must be select, toggle, expand, scroll_down, scroll_up or focus.";
        }
    }

    public override RiskTier TierFor(OperateArgs args) =>
        screen.Element(args.Id) is { } e && PressControlTool.Sensitive().IsMatch(e.Name) ? RiskTier.Impactful : Tier;

    protected override Task<SpokenResult> ExecuteAsync(OperateArgs args, CancellationToken ct)
    {
        PressControlTool.Guard(residency);
        return Task.FromResult(new SpokenResult(screen.Act(args.Id, Actions[args.Action])));
    }
}

// ---------------------------------------------------------------- clipboard

public sealed class ReadClipboardTool : Tool<NoArguments, ReadResult>
{
    public override string Name => "read_clipboard";
    public override string Description => "Read the text on the clipboard (what the user copied), to summarise, translate or answer about it";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.ClipboardRead;

    protected override async Task<ReadResult> ExecuteAsync(NoArguments args, CancellationToken ct)
    {
        var text = await ClipboardText.ReadAsync() ?? throw new InvalidOperationException("There's no text on the clipboard.");
        var cut = text.Length > DocumentReader.MaxCharacters ? text[..DocumentReader.MaxCharacters] + "\n[… cut]" : text;
        return new ReadResult($"The clipboard holds {text.Length:N0} characters of text (untrusted, may be sensitive).", cut);
    }
}

public sealed class CopyTextTool : Tool<TextArgs, SpokenResult>
{
    public override string Name => "copy_text";
    public override string Description => "Put text on the clipboard so the user can paste it";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.ClipboardWrite;

    public override RiskTier TierFor(TextArgs args) => SensitiveDetector.Check(args.Text) is null ? Tier : RiskTier.Forbidden;

    protected override async Task<SpokenResult> ExecuteAsync(TextArgs args, CancellationToken ct)
    {
        await ClipboardText.WriteAsync(args.Text);
        return new SpokenResult("Copied. It's ready to paste.");
    }
}
