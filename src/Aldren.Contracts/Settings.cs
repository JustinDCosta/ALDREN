namespace Aldren.Contracts;

public sealed record AldrenSettings(int SchemaVersion, GeneralSettings General, VoiceSettings Voice)
{
    public const int CurrentSchemaVersion = 2;

    public static AldrenSettings Default { get; } = new(CurrentSchemaVersion, new GeneralSettings(), new VoiceSettings());
}

public sealed record GeneralSettings(string Hotkey = "Ctrl+/", bool ShowOnStream = false);

/// <param name="WakeWord">Off by default: the wake word is still short of its false-wake target (Phase 3 plan).</param>
/// <param name="TranscriptCheck">A wake must also be heard as "Aldrin" in the transcript: fewer false wakes, more misses.</param>
public sealed record VoiceSettings(
    bool WakeWord = false,
    bool TranscriptCheck = true,
    bool SpeakReplies = true,
    string PushToTalkHotkey = "Ctrl+*");

public sealed record SettingsResult(bool Saved, IReadOnlyList<string> Errors);
