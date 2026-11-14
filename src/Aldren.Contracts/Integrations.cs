namespace Aldren.Contracts;

/// <param name="Registered">The browser host is registered with Chrome and Edge (HKCU, written on your click).</param>
/// <param name="Connected">The Companion extension is running and answered.</param>
/// <param name="ExtensionFolder">The folder to load with "Load unpacked" in chrome://extensions.</param>
public sealed record BrowserStatusInfo(bool Registered, bool Connected, string ExtensionFolder, string ExtensionId, IReadOnlyList<SiteLevelItem> Sites);

/// <param name="Level">ReadOnly, Interact or Forms.</param>
public sealed record SiteLevelItem(string Site, string Level);

public sealed record GoogleStatusInfo(bool HasClient, bool SignedIn);

/// <param name="Ends">Already in words ("3:45 PM"); null when no session is running.</param>
public sealed record FocusInfo(bool Active, string? Goal, string? Ends, int Slips, int WeekSessions, int WeekMinutes, int WeekSlips, int WeekEndedEarly);

/// <param name="Gaming">What you chose: "auto", "on" or "off". <paramref name="GamingActive"/> is what is in effect.</param>
/// <param name="ModelMemoryMb">Working set of the local model's process (RAM, not VRAM); 0 when none runs.</param>
public sealed record SystemInfo(
    string Model,
    bool GpuLoaded,
    bool CpuLoaded,
    long? FreeVramMiB,
    bool OnAc,
    string Gaming,
    bool GamingActive,
    bool Silent,
    long CoreMemoryMb,
    long ModelMemoryMb,
    double UptimeHours,
    string SettingsFolder,
    string DataFolder,
    string ModelsFolder,
    long DatabaseKb,
    long LogsKb,
    int Memories,
    int Reminders,
    int Tasks,
    int Routines);

public sealed record RoutineItem(string Name, IReadOnlyList<string> Steps);
