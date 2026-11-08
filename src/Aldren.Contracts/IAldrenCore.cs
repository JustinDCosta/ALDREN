namespace Aldren.Contracts;

/// <summary>What Core offers the App over the pipe.</summary>
public interface IAldrenCore
{
    Task<CoreStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Sends typed text to the assistant and returns its reply.</summary>
    Task<string> SubmitTextAsync(string text, CancellationToken cancellationToken);

    /// <summary>The push-to-talk hotkey: starts listening, or stops ALDREN speaking and listens.</summary>
    Task PushToTalkAsync(CancellationToken cancellationToken);

    /// <summary>The Stop button or Escape: ALDREN stops speaking (and writing the reply) at once, and the voice turn ends.</summary>
    Task StopSpeakingAsync(CancellationToken cancellationToken);

    Task<AldrenSettings> GetSettingsAsync(CancellationToken cancellationToken);

    Task<SettingsResult> UpdateSettingsAsync(AldrenSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<PermissionState>> GetPermissionsAsync(CancellationToken cancellationToken);

    /// <summary>The only way a permission changes: the Settings window, on your action. No tool reaches it.</summary>
    Task SetPermissionAsync(string permission, bool granted, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> GetAuditAsync(AuditQuery query, CancellationToken cancellationToken);

    Task SetStartWithWindowsAsync(bool enabled, CancellationToken cancellationToken);

    /// <summary>Quit: Core stops and restarts nothing.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken);
}
