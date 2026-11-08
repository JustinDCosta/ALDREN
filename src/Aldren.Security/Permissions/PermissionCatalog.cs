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

    public IReadOnlyCollection<PermissionDefinition> Definitions => _definitions.Values;

    public bool Contains(string name) => _definitions.ContainsKey(name);

    public bool TryGet(string name, out PermissionDefinition definition) =>
        _definitions.TryGetValue(name, out definition!);
}
