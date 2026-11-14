namespace Aldren.Security.Permissions;

public sealed record PermissionDefinition(string Name, string Description, bool GrantedByDefault);

/// <summary>The permissions ALDREN knows. Anything outside the catalog is denied.</summary>
public sealed class PermissionCatalog
{
    public const string SystemClock = "system.clock";
    public const string DiagnosticsTest = "diagnostics.test";
    public const string AppsLaunch = "apps.launch";
    public const string AppsFocus = "apps.focus";
    public const string AppsClose = "apps.close";
    public const string AudioVolume = "audio.volume";
    public const string MediaControl = "media.control";
    public const string Timers = "assistant.timers";
    public const string Calculator = "assistant.calculator";
    public const string Delegation = "assistant.delegation";
    public const string MemoryRead = "memory.read";
    public const string MemoryWrite = "memory.write";
    public const string Reminders = "assistant.reminders";
    public const string Tasks = "assistant.tasks";
    public const string Briefing = "assistant.briefing";
    public const string Presence = "presence.home";
    public const string WindowsManage = "windows.manage";
    public const string FilesRead = "files.read";
    public const string FilesChange = "files.change";
    public const string ScreenRead = "screen.read";
    public const string ScreenAct = "screen.act";
    public const string ClipboardRead = "clipboard.read";
    public const string ClipboardWrite = "clipboard.write";
    public const string BrowserRead = "browser.read";
    public const string BrowserAct = "browser.act";
    public const string BrowserDownload = "browser.download";
    public const string CalendarRead = "google.calendar.read";
    public const string CalendarWrite = "google.calendar.write";
    public const string DriveRead = "google.drive.read";
    public const string DriveWrite = "google.drive.write";
    public const string Focus = "focus.control";
    public const string FocusEnforce = "focus.enforce";
    public const string Routines = "assistant.routines";
    public const string DevGit = "dev.git";
    public const string Dictation = "voice.dictation";

    private readonly Dictionary<string, PermissionDefinition> _definitions;

    public PermissionCatalog(IEnumerable<PermissionDefinition> definitions)
    {
        _definitions = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (!_definitions.TryAdd(definition.Name, definition))
            {
                throw new ArgumentException($"Permission '{definition.Name}' is defined twice.", nameof(definitions));
            }
        }
    }

    public static PermissionCatalog Phase1 { get; } = new(
    [
        new PermissionDefinition(SystemClock, "Read the system clock", GrantedByDefault: true),
        new PermissionDefinition(DiagnosticsTest, "Run the confirmation test", GrantedByDefault: true),
    ]);

    public static PermissionCatalog Phase2 { get; } = new(
    [
        .. Phase1.Definitions,
        new PermissionDefinition(AppsLaunch, "Open apps", GrantedByDefault: true),
        new PermissionDefinition(AppsFocus, "Switch to open apps", GrantedByDefault: true),
        new PermissionDefinition(AppsClose, "Close apps (asks first)", GrantedByDefault: true),
        new PermissionDefinition(AudioVolume, "Change the volume", GrantedByDefault: true),
        new PermissionDefinition(MediaControl, "Play, pause and skip media", GrantedByDefault: true),
        new PermissionDefinition(Timers, "Set timers", GrantedByDefault: true),
        new PermissionDefinition(Calculator, "Do calculations", GrantedByDefault: true),
        new PermissionDefinition(Delegation, "Hand requests to the Action and Research brains", GrantedByDefault: true),
    ]);

    public static PermissionCatalog Phase4 { get; } = new(
    [
        .. Phase2.Definitions,
        new PermissionDefinition(MemoryRead, "Recall what you asked ALDREN to remember", GrantedByDefault: true),
        new PermissionDefinition(MemoryWrite, "Remember and forget things (forgetting asks first)", GrantedByDefault: true),
        new PermissionDefinition(Reminders, "Create, list and cancel reminders (cancelling asks first)", GrantedByDefault: true),
        new PermissionDefinition(Tasks, "Keep your task list", GrantedByDefault: true),
        new PermissionDefinition(Briefing, "Give the daily briefing", GrantedByDefault: true),
        new PermissionDefinition(Presence, "Know whether you're home (saving a home network asks first)", GrantedByDefault: true),
    ]);

    public static PermissionCatalog Phase5 { get; } = new(
    [
        .. Phase4.Definitions,
        new PermissionDefinition(WindowsManage, "Move, snap and arrange windows, and use layouts", GrantedByDefault: true),
        new PermissionDefinition(FilesRead, "Find, list and read your files", GrantedByDefault: true),
        new PermissionDefinition(FilesChange, "Move, rename and recycle files (deleting asks first)", GrantedByDefault: true),
        new PermissionDefinition(ScreenRead, "Read the window you're using (UI Automation, OCR, local vision)", GrantedByDefault: true),
        new PermissionDefinition(ScreenAct, "Operate controls in apps (sensitive ones ask first)", GrantedByDefault: true),
        new PermissionDefinition(ClipboardRead, "Read the clipboard when you ask", GrantedByDefault: true),
        new PermissionDefinition(ClipboardWrite, "Copy text to the clipboard", GrantedByDefault: true),
    ]);

    public static PermissionCatalog Phase9 { get; } = new(
    [
        .. Phase5.Definitions,
        new PermissionDefinition(BrowserRead, "Read your browser tabs and pages", GrantedByDefault: true),
        new PermissionDefinition(BrowserAct, "Click and fill in web pages (sensitive buttons and forms ask first)", GrantedByDefault: true),
        new PermissionDefinition(BrowserDownload, "Download files in the browser (programs ask first)", GrantedByDefault: true),
        new PermissionDefinition(CalendarRead, "Read your Google Calendar", GrantedByDefault: true),
        new PermissionDefinition(CalendarWrite, "Create and delete calendar events (deleting asks first)", GrantedByDefault: true),
        new PermissionDefinition(DriveRead, "Search and download from Google Drive", GrantedByDefault: true),
        new PermissionDefinition(DriveWrite, "Upload, rename and move in Google Drive (asks first)", GrantedByDefault: true),
        new PermissionDefinition(Focus, "Run focus sessions", GrantedByDefault: true),
        new PermissionDefinition(FocusEnforce, "During focus sessions, minimise and close distracting apps", GrantedByDefault: true),
        new PermissionDefinition(Routines, "Create and run routines", GrantedByDefault: true),
        new PermissionDefinition(DevGit, "Read git status, history and changes in your repositories", GrantedByDefault: true),
        new PermissionDefinition(Dictation, "Type what you dictate into the focused field", GrantedByDefault: true),
    ]);

    public IReadOnlyCollection<PermissionDefinition> Definitions => _definitions.Values;

    public bool Contains(string name) => _definitions.ContainsKey(name);

    public bool TryGet(string name, out PermissionDefinition definition) =>
        _definitions.TryGetValue(name, out definition!);
}
