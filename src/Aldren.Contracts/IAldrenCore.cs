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

    /// <summary>The memory inspector: keyword search, or the latest when the query is empty.</summary>
    Task<IReadOnlyList<MemoryItem>> GetMemoriesAsync(string? query, CancellationToken cancellationToken);

    Task DeleteMemoryAsync(long id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReminderItem>> GetRemindersAsync(CancellationToken cancellationToken);

    Task CancelReminderAsync(long id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskItem>> GetTasksAsync(CancellationToken cancellationToken);

    Task CompleteTaskAsync(long id, CancellationToken cancellationToken);

    Task<PresenceInfo> GetPresenceAsync(CancellationToken cancellationToken);

    /// <summary>Saves the network the PC is on as home (from Settings, on your click). Returns what happened, in words.</summary>
    Task<string> SetHomeNetworkAsync(CancellationToken cancellationToken);

    Task ForgetHomeNetworksAsync(CancellationToken cancellationToken);

    Task<CloudStatusInfo> GetCloudStatusAsync(CancellationToken cancellationToken);

    /// <summary>Runs the gateway drift check again (after you change the chains or keys in its dashboard).</summary>
    Task<CloudStatusInfo> RecheckCloudAsync(CancellationToken cancellationToken);

    Task<AldrenSettings> GetSettingsAsync(CancellationToken cancellationToken);

    Task<SettingsResult> UpdateSettingsAsync(AldrenSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<PermissionState>> GetPermissionsAsync(CancellationToken cancellationToken);

    /// <summary>The only way a permission changes: the Settings window, on your action. No tool reaches it.</summary>
    Task SetPermissionAsync(string permission, bool granted, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> GetAuditAsync(AuditQuery query, CancellationToken cancellationToken);

    Task SetStartWithWindowsAsync(bool enabled, CancellationToken cancellationToken);

    Task<BrowserStatusInfo> GetBrowserStatusAsync(CancellationToken cancellationToken);

    /// <summary>Registers the browser host with Chrome and Edge (HKCU), from Settings on your click. Returns what happened.</summary>
    Task<string> ConnectBrowserAsync(CancellationToken cancellationToken);

    /// <summary>Removes the browser host registration (the HKCU keys).</summary>
    Task DisconnectBrowserAsync(CancellationToken cancellationToken);

    /// <summary>Sets a site's level (ReadOnly, Interact, Forms); null resets it to the default.</summary>
    Task SetSiteLevelAsync(string site, string? level, CancellationToken cancellationToken);

    Task<GoogleStatusInfo> GetGoogleStatusAsync(CancellationToken cancellationToken);

    /// <summary>Stores your own OAuth client (docs/guides/google-oauth.md) in Credential Manager.</summary>
    Task SetGoogleClientAsync(string clientId, string clientSecret, CancellationToken cancellationToken);

    /// <summary>Opens Google's consent page in your browser and waits for you to finish. Returns what happened.</summary>
    Task<string> SignInGoogleAsync(CancellationToken cancellationToken);

    /// <summary>Revokes ALDREN's Google access and deletes the stored token.</summary>
    Task DisconnectGoogleAsync(CancellationToken cancellationToken);

    Task<FocusInfo> GetFocusAsync(CancellationToken cancellationToken);

    /// <summary>The emergency exit, from the tray or Settings.</summary>
    Task EndFocusAsync(CancellationToken cancellationToken);

    /// <summary>"New conversation": ALDREN forgets this session's earlier turns (what you asked it to remember stays).</summary>
    Task NewConversationAsync(CancellationToken cancellationToken);

    /// <summary>The live picture for Settings: the local model, memory use, modes and where the data lives.</summary>
    Task<SystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken);

    /// <summary>"auto" (follow detection), "on" or "off"; the same as saying "gaming mode on".</summary>
    Task SetGamingModeAsync(string mode, CancellationToken cancellationToken);

    /// <summary>Silent mode: replies as text only until switched off or ALDREN restarts.</summary>
    Task SetSilentAsync(bool silent, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoutineItem>> GetRoutinesAsync(CancellationToken cancellationToken);

    /// <summary>Saves a routine from Settings: a name and its steps, separated by commas, "then" or new lines. Returns what happened.</summary>
    Task<string> SaveRoutineAsync(string name, string steps, CancellationToken cancellationToken);

    Task DeleteRoutineAsync(string name, CancellationToken cancellationToken);

    /// <summary>Deletes everything ALDREN remembers (Settings &gt; Privacy, on your click). Returns how many entries went.</summary>
    Task<int> DeleteAllMemoriesAsync(CancellationToken cancellationToken);

    /// <summary>Quit: Core stops and restarts nothing.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken);
}
