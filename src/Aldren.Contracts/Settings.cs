namespace Aldren.Contracts;

public sealed record AldrenSettings(int SchemaVersion, GeneralSettings General)
{
    public const int CurrentSchemaVersion = 1;

    public static AldrenSettings Default { get; } = new(CurrentSchemaVersion, new GeneralSettings());
}

public sealed record GeneralSettings(string Hotkey = "Ctrl+Alt+Space", bool ShowOnStream = false);

public sealed record SettingsResult(bool Saved, IReadOnlyList<string> Errors);
