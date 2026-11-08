namespace Aldren.Contracts;

public sealed record AldrenSettings(int SchemaVersion, GeneralSettings General, VoiceSettings Voice, PresenceSettings Presence, CloudSettings Cloud)
{
    public const int CurrentSchemaVersion = 4;

    public static AldrenSettings Default { get; } = new(CurrentSchemaVersion, new GeneralSettings(), new VoiceSettings(), new PresenceSettings(), new CloudSettings());
}

public sealed record GeneralSettings(string Hotkey = "Ctrl+/", bool ShowOnStream = false);

/// <param name="WakeWord">Off by default: the wake word is still short of its false-wake target (Phase 3 plan).</param>
/// <param name="TranscriptCheck">A wake must also be heard as "Aldrin" in the transcript: fewer false wakes, more misses.</param>
public sealed record VoiceSettings(
    bool WakeWord = false,
    bool TranscriptCheck = true,
    bool SpeakReplies = true,
    string PushToTalkHotkey = "Ctrl+*");

/// <param name="GreetingAfterHours">"Welcome back" only after at least this long since the last one.</param>
/// <param name="AwayMute">Away from home, ALDREN mutes the PC and restores it at home if it did the muting.</param>
public sealed record PresenceSettings(
    bool Greeting = true,
    bool BriefingWithGreeting = true,
    int GreetingAfterHours = 4,
    bool AwayMute = true,
    bool AwayMuteSkipHeadphones = true);

/// <param name="Enabled">Runs the gateway and allows research when you ask; nothing is sent without your request.</param>
/// <param name="QuickFallback">Off by default (ARCHITECTURE §22): non-personal questions go to aldren-fast only when the local model is unavailable.</param>
public sealed record CloudSettings(bool Enabled = true, bool QuickFallback = false);

public sealed record SettingsResult(bool Saved, IReadOnlyList<string> Errors);
