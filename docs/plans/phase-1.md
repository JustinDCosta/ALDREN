# Phase 1 plan: foundation

**Goal:** ALDREN's two processes running for real. Core starts silently at logon and keeps the App alive; the App shows the tray icon, the island and the settings shells; every action passes through the Tool Gateway, the permission engine and the audit log.

**Approach:** five projects under `src/` and two test projects. Core is a background .NET generic host. The App is the WinUI 3 shell, with the island ported from the S2 spike. They talk through StreamJsonRpc over a named pipe that only your account can open. There is no model yet (that's Phase 2): typed text reaches a placeholder that can only tell the time and run a confirmation test, both through the gateway, so the whole path is exercised.

**Stack:** .NET 10 (SDK 10.0.403, runtime 10.0.14) · WinUI 3 / Windows App SDK 2.6.4, unpackaged and self-contained as in S2 · Microsoft.Extensions.Hosting 10.0.14 · Serilog 4.4.0 · StreamJsonRpc 2.26.150 · Microsoft.Data.Sqlite 10.0.14 · CsWin32 0.3.367 (build time only) · xUnit 2.9.3.

**Spec:** [ARCHITECTURE.md](../ARCHITECTURE.md) (§3–4 processes and IPC, §13 interface, §17–20 security, data, observability and testing), [SECURITY.md](../SECURITY.md) (Tool Gateway contract, risk tiers, permissions), ADR-0002, ADR-0003 and ADR-0006, and the Phase 1 section of [ROADMAP.md](../ROADMAP.md).

**Exit (from the roadmap):** starts silently at login; a UI crash recovers automatically; idle under 1% CPU and under 300 MB of RAM; the unit tests for the gateway policies pass.

**Approved 2026-11-02, with one change (your decision, to save usage):** write no tests except the Tool Gateway's (Task 7). Skip every other "write the tests / run → fail" step; build, run and check by hand instead. Execution: native, one task at a time, brief.

## Global constraints
- C# on .NET 10 with nullable enabled, file-scoped namespaces, async with `CancellationToken` end to end, records for DTOs and tool arguments, Microsoft.Extensions hosting, DI, configuration and logging with Serilog.
- Warnings are errors in `src/` and `tests/`. Versions for `src/` live in `src/Directory.Packages.props`; test projects pin theirs as in Phase 0. Every project under `src/` and `tests/` commits a `packages.lock.json` and restores in locked mode, which pins each package's SHA-512, transitive ones included.
- Core and the App never run elevated or ask for admin rights. ALDREN opens no TCP port.
- Both ends of the pipe use `PipeOptions.CurrentUserOnly`: the server's ACL admits only your account, and the client checks that the server belongs to you.
- No secret ever goes into a file, a log or the settings. Logs are redacted (Task 2).
- Settings live in `%APPDATA%\Aldren`, the database and logs in `%LOCALAPPDATA%\Aldren` (ARCHITECTURE §18). The `ALDREN_HOME` variable moves both, for tests and development.
- Idle means idle: nothing polls while the island is hidden. Timers run only while something is visible or a job is due.
- Times are stored in UTC and shown in local time.
- Every tool and policy has unit tests. Window code is checked by the acceptance script (Task 14). Logic the windows depend on lives in `Aldren.Common` and is unit-tested.
- Every Phase 1 window works with the keyboard and Narrator: controls have accessible names, Tab order follows the layout, and Esc closes.
- Each task updates `docs/STATUS.md` in the same commit.
- The development build runs from `E:\AldrenData\dev\aldren`, with Core and the App side by side.

## Review focus
Failure modes a person would hit that the happy path doesn't show. Each has a test in the task that owns the code.
1. **Quit, logoff and crash racing each other.** Core must not restart the App after Quit or during logoff, and the App must not relaunch Core in those cases either (Tasks 11 and 12).
2. **A settings or permissions file that is corrupt or comes from a newer version.** ALDREN keeps the file, runs on safe defaults, says so in Settings, and never overwrites what it couldn't read (Tasks 4 and 9).
3. **The hotkey is already taken.** In S2, Ctrl+Alt+Space was. The App falls back to the next candidate and shows which hotkey is active; it never fails silently (Task 2).
4. **A confirmation with nobody there, or with the App gone.** It's declined after 60 s, or at once when no App is connected, and the tool doesn't run (Tasks 7 and 11).
5. **The audit log can't be written** (database locked, disk full). Anything above tier 0 fails closed: the action doesn't run, and the failure is logged (Task 7).

## Setup

Downloads: NuGet packages from nuget.org into `E:\AldrenData\cache\nuget`, about 60 MB new. The Windows App SDK and xUnit packages are already in the cache from Phase 0.

| Package | Version | Licence | Used by | Why |
|---|---|---|---|---|
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | Core, App | Generic host, DI, configuration, logging |
| Microsoft.Win32.SystemEvents | 10.0.14 | MIT | Core | Hearing logoff and shutdown, so Core stops cleanly and restarts nothing (not part of the base runtime) |
| Serilog, Serilog.Extensions.Hosting, Serilog.Sinks.File, Serilog.Formatting.Compact | 4.4.0, 10.0.0, 7.0.0, 3.0.0 | Apache-2.0 | Common | Structured, rolling, redacted log files |
| StreamJsonRpc | 2.26.150 | MIT | Contracts | IPC between Core and the App (ADR-0003) |
| Microsoft.Data.Sqlite | 10.0.14 | MIT | Security | The audit log in `aldren.db` (carries the native SQLite library) |
| Microsoft.Windows.CsWin32 | 0.3.367 | MIT | Security, Core, App (build time only) | Generated Win32 declarations: Job Objects, Credential Manager, tray icon, hotkeys |
| Microsoft.WindowsAppSDK, Microsoft.Windows.SDK.BuildTools | 2.6.4, 10.0.28000.2957 | MIT | App | WinUI 3, as in S2 (cached) |
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 | MIT | tests | A fake clock for timeouts and backoff |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector | 2.9.3, 3.1.4, 17.14.1, 6.0.4 | Apache-2.0, MIT | tests | As in Phase 0 (cached) |

The Windows APIs for folder permissions, the registry and pipe security are part of the .NET runtime already installed, so they need no package. These packages bring their own dependencies, all from nuget.org: the Microsoft.Extensions 10.0.14 family, Serilog.Extensions.Logging 10.0.0, SQLitePCLRaw 2.1.13 (the native SQLite build), StreamJsonRpc's MessagePack, Nerdbank.MessagePack, Nerdbank.Streams, Newtonsoft.Json, Microsoft.VisualStudio.Threading and Microsoft.VisualStudio.Validation, and CsWin32's Win32 metadata packages (build time only). The lock files record every one with its hash, and `docs/DEPENDENCIES.md` lists them.

Nothing else is downloaded or installed. The App carries the Windows App SDK runtime itself (self-contained, as in S2).

System changes, all reversible:
- **Start with Windows** writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ALDREN` only when you switch it on (in Settings or the tray menu), and deletes it when you switch it off. The acceptance script asks you to switch it on once and sign out and back in; afterwards you keep it or switch it off.
- **Credential Manager:** one integration test creates, reads and deletes a generic credential named `Aldren/test-<random>`, once, when you run it. Phase 1 stores no real secret.
- **Data folders:** `%APPDATA%\Aldren` and `%LOCALAPPDATA%\Aldren` are created, and `%LOCALAPPDATA%\Aldren` is restricted to your account (ARCHITECTURE §18).
- **Development build** in `E:\AldrenData\dev\aldren`.
- While ALDREN runs, it holds a global hotkey, released when it exits.
- No services, scheduled tasks, firewall rules, policies, elevation or Windows settings changes.

Doc changes this plan includes (approving the plan approves them):
- ARCHITECTURE §4: the App runs **outside** Core's Job Object. A job that kills its processes when Core dies would take the App with it, and the App could never relaunch Core as §4 requires. llama-server and the gateway stay inside the job.
- ARCHITECTURE §21: a new `Aldren.Common` project for code both processes share, so the App's logic can be unit-tested without WinUI.
- SECURITY.md: ALDREN's own folders are off-limits to every tool (that is what keeps permissions "where no tool can write"), and the gateway fails closed when it can't write the audit log.

## Tasks

### Task 1: Solution scaffolding
**Files:** `src/Directory.Build.props` (imports the root one; warnings as errors, lock files, locked-mode restore), `src/Directory.Packages.props`; projects `src/Aldren.Contracts`, `src/Aldren.Common`, `src/Aldren.Security` (all `net10.0-windows`), `src/Aldren.Core` (`net10.0-windows`, `WinExe`, so no console window ever appears), `src/Aldren.App` (`net10.0-windows10.0.26100.0`, WinUI 3, `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`); `tests/Aldren.Security.Tests`, `tests/Aldren.Core.Tests` (which also covers Common and Contracts), `tests/Aldren.TestChild` (a console program that waits until it's killed, for the Job Object test); `tools/dev/publish-local.ps1`; `docs/DEPENDENCIES.md`. Everything joins `ALDREN.sln`, the App included.

- [ ] Create the projects and references: Contracts ← Security ← Core, Common ← Security, Core and App, Contracts ← App.
- [ ] Restore and build. Expected: `dotnet build ALDREN.sln` with 0 warnings and 0 errors; a `packages.lock.json` beside each project under `src/` and `tests/`; `dotnet restore ALDREN.sln --locked-mode` succeeds.
- [ ] `publish-local.ps1` publishes Core (framework-dependent, win-x64) and the App (self-contained Windows App SDK) into `E:\AldrenData\dev\aldren`, and refuses to run while an ALDREN process from that folder is running.
- [ ] `docs/DEPENDENCIES.md`: every package with version, licence and purpose, pointing to the lock files for hashes.
- [ ] `tools/check-repo.ps1`, commit "Add the Phase 1 solution", push.

### Task 2: Shared building blocks
**Files:** `src/Aldren.Common/`: `AldrenPaths.cs`, `RestartBackoff.cs`, `SingleInstance.cs`, `Logging/Redactor.cs`, `Logging/RedactingEnricher.cs`, `Logging/LogSetup.cs`, `Shell/Hotkey.cs`, `Shell/HotkeyChooser.cs`, `Shell/IslandPlacement.cs`; tests in `tests/Aldren.Core.Tests/Common/`.

**Interfaces (produces):**
- `sealed record AldrenPaths(string SettingsDirectory, string DataDirectory)` with `LogsDirectory`, `DatabasePath`, `SettingsFile`, `PermissionsFile`; `static AldrenPaths FromEnvironment()`: `ALDREN_HOME` gives `<home>\settings` and `<home>\data`, otherwise `%APPDATA%\Aldren` and `%LOCALAPPDATA%\Aldren`.
- `sealed class RestartBackoff(TimeProvider time)`: `void MarkStarted()`; `TimeSpan? NextDelay()`, called on each crash. The n-th consecutive crash waits min(2^(n−1), 60) s; staying up 5 minutes resets the count; the 9th consecutive crash returns null (give up).
- `static SingleInstance? SingleInstance.TryAcquire(string name)` (disposable) and `static bool SingleInstance.IsHeld(string name)`. It is decided by whether the named mutex already existed, not by waiting on it, because a mutex is re-entrant for the thread that owns it. Names: `Local\Aldren.Core`, `Local\Aldren.App`.
- `static string Redactor.Redact(string text)`: masks as `***` the values of JSON properties and `name=value` or `name: value` pairs whose name matches password, passwd, secret, token, api key, access key, private key, master key, unified key, encryption key, authorization, cookie or credential; `Bearer` tokens; `sk-…`, `gsk_…`, `AIza…` and `ghp_…` keys; and runs of 32 or more hex digits. GUIDs and ordinary text are kept.
- `static LoggerConfiguration LogSetup.Configure(LoggerConfiguration config, AldrenPaths paths, string processName)`: compact JSON in `<logs>\<process>-<date>.log`, daily files, 14 kept, the redacting enricher on every string property, Information level (Debug when `ALDREN_LOG_DEBUG=1`).
- `[Flags] enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4 }` (the `RegisterHotKey` values) and `readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)` with `static bool TryParse(string text, out Hotkey hotkey)` and a normalised `ToString()`. Valid: Ctrl and/or Alt, optional Shift, plus A–Z, 0–9, F1–F24 or Space. The Windows key is refused (Windows reserves it), and so is Ctrl+Alt+Delete.
- `static (Hotkey? Active, bool UsedFallback) HotkeyChooser.Choose(Hotkey configured, Func<Hotkey, bool> tryRegister)`: tries the configured hotkey, then Ctrl+Alt+Shift+Space.
- `readonly record struct RectInt(int X, int Y, int Width, int Height)` and `static RectInt IslandPlacement.Place(RectInt workArea, double scale, int widthDip, int heightDip, int topMarginDip = 8)`: width and height scaled and rounded, x = workArea.X + (workArea.Width − width) / 2 (integer division), y = workArea.Y + round(margin × scale).

- [ ] Write the tests:
  - `Backoff_doubles_up_to_a_sixty_second_cap` (1, 2, 4, 8, 16, 32, 60, 60 s); `Backoff_gives_up_on_the_ninth_consecutive_crash`; `Backoff_resets_after_five_minutes_up`.
  - `Second_acquire_fails_while_the_first_is_held` (from another thread); `Acquire_succeeds_after_release`.
  - `Redacts_json_secret_properties` (`{"apiKey":"abc123","hotkey":"Ctrl+Alt+Space"}` → apiKey masked, hotkey kept); `Redacts_name_value_pairs` (`password=hunter2 user=me` → `password=*** user=me`); `Redacts_bearer_tokens`; `Redacts_known_key_formats` (`sk-`, `gsk_`, `AIza`, 48 hex digits); `Keeps_guids_and_ordinary_text`.
  - `Logged_secret_is_redacted_in_the_file` (logs `apiKey=sk-…` to a temporary folder; the file has `***`, not the key).
  - `Parses_and_normalises_hotkeys` (`ctrl + alt + space` → `Ctrl+Alt+Space`); `Rejects_invalid_hotkeys` (`Space`, `Ctrl+`, `Win+A`, `Ctrl+Alt+Delete`).
  - `Hotkeys_fall_back_when_taken` (first refused → second active, `UsedFallback` true); `No_hotkey_when_all_are_taken` (→ null).
  - `Places_top_centre_at_100_percent` (work area 0, 0, 1920, 1040; 180×36 → 870, 8, 180, 36); `Scales_at_125_percent` (1920×1032 → 847, 10, 225, 45); `Respects_the_second_monitor_offset` (work area starting at x = 1920 → x = 2790).
- [ ] Run → fail (types missing). Implement. Run → pass.
- [ ] Commit "Add shared building blocks", push.

### Task 3: Contracts and the pipe
**Files:** `src/Aldren.Contracts/`: `IAldrenCore.cs`, `IAldrenApp.cs`, `Status.cs`, `Settings.cs`, `Permissions.cs`, `Audit.cs`, `Confirmation.cs`, `Rpc/AldrenPipe.cs`, `Rpc/AldrenRpc.cs`; tests in `tests/Aldren.Core.Tests/Ipc/RpcRoundTripTests.cs`.

**Interfaces (produces):**
- `interface IAldrenCore` (Core implements, the App calls): `GetStatusAsync → CoreStatus`, `SubmitTextAsync(string text) → string` (the reply), `GetSettingsAsync → AldrenSettings`, `UpdateSettingsAsync(AldrenSettings) → SettingsResult`, `GetPermissionsAsync → IReadOnlyList<PermissionState>`, `SetPermissionAsync(string permission, bool granted)`, `GetAuditAsync(AuditQuery) → IReadOnlyList<AuditEntry>`, `SetStartWithWindowsAsync(bool enabled)`, `ShutdownAsync()`; each takes a `CancellationToken`.
- `interface IAldrenApp` (the App implements, Core calls back on the same connection): `Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct)`.
- Records: `CoreStatus(string Version, int ProcessId, DateTimeOffset StartedAt, StartupState StartWithWindows)`; `enum StartupState { Off, On, DisabledInWindows }`; `AldrenSettings(int SchemaVersion, GeneralSettings General)`; `GeneralSettings(string Hotkey = "Ctrl+Alt+Space", bool ShowOnStream = false)`; `SettingsResult(bool Saved, IReadOnlyList<string> Errors)`; `PermissionState(string Name, string Description, bool Granted)`; `AuditQuery(int Limit = 100, long? BeforeId = null)` (Limit capped at 1,000); `AuditEntry(long Id, DateTimeOffset Timestamp, string Tool, string Tier, string Origin, string Outcome, string? Detail, int DurationMs, bool Confirmed, bool Tainted)`; `ConfirmationRequest(string RequestId, string Tool, string Tier, string Summary, IReadOnlyList<string> UntrustedSources, int TimeoutSeconds)`.
- `AldrenPipe.Name` = `aldren-core-<your account SID>`; `CreateServer(string? name = null)` (one instance, asynchronous, current user only) and `CreateClient(string? name = null)`.
- `AldrenRpc.Attach(Stream stream, object localTarget) → JsonRpc`: header-delimited messages, `SystemTextJsonFormatter` with shared options, the local target added; the caller starts listening.

- [ ] Write the tests, on a real pipe with a random name: `App_calls_core_over_the_pipe`; `Core_calls_the_app_back_on_the_same_connection`; `Cancellation_reaches_the_other_side` (the server's token is cancelled within 1 s); `Disconnect_fails_pending_calls` (the server goes away mid-call → `ConnectionLostException` within 1 s); `A_second_client_cannot_connect`.
- [ ] Run → fail. Implement. Run → pass.
- [ ] Commit "Add the IPC contracts", push.

### Task 4: Permissions
**Files:** `src/Aldren.Security/Permissions/PermissionCatalog.cs`, `PermissionStore.cs`; tests `tests/Aldren.Security.Tests/PermissionStoreTests.cs`.

**Interfaces (produces):**
- `record PermissionDefinition(string Name, string Description, bool GrantedByDefault)`; `PermissionCatalog(IEnumerable<PermissionDefinition>)`. Phase 1 defines `system.clock` and `diagnostics.test`, both on by default.
- `interface IPermissionStore { bool IsGranted(string permission); IReadOnlyList<PermissionState> List(); string? LoadProblem { get; } Task SetAsync(string permission, bool granted, CancellationToken ct); }`. Only `IAldrenCore.SetPermissionAsync`, which the Settings window calls, reaches `SetAsync`; no tool can.
- `PermissionStore(string path, PermissionCatalog catalog)`: `permissions.json` = `{ "schemaVersion": 1, "granted": [...], "denied": [...] }`, written atomically (temporary file, then replace). Permissions outside the catalog deny, but entries for them in the file are kept, so a downgrade doesn't lose them.

- [ ] Write the tests: `Unknown_permission_is_denied`; `Defaults_apply_without_a_file`; `Grant_and_revoke_persist_across_instances`; `Entries_outside_the_catalog_survive_a_save`; `Corrupt_file_is_set_aside_and_defaults_used` (renamed to `permissions.json.bad-<UTC time>`, `LoadProblem` set); `Newer_file_is_left_untouched` (schema version 2: defaults in memory, `SetAsync` refuses with a message, the file's bytes unchanged).
- [ ] Run → fail. Implement. Run → pass.
- [ ] Commit "Add the permission engine", push.

### Task 5: Tools and argument checks
**Files:** `src/Aldren.Security/Tools/`: `RiskTier.cs`, `ITool.cs`, `Tool.cs`, `ToolDescriptor.cs`, `ToolRegistry.cs`, `ArgumentBinder.cs`; tests `tests/Aldren.Security.Tests/ToolRegistryTests.cs`, `ArgumentBinderTests.cs`.

**Interfaces (produces):**
- `enum RiskTier { Read = 0, Reversible = 1, Impactful = 2, Destructive = 3, Forbidden = 4 }` (SECURITY.md's tiers).
- `abstract class Tool<TArgs, TResult> : ITool where TArgs : class`: `Name`, `Description`, `Tier`, `Permission`; `virtual TimeSpan Timeout` (10 s); `virtual IEnumerable<string> Validate(TArgs args)` for checks beyond the schema; `virtual string Summarize(TArgs args)` for the plain-words line in a confirmation; `protected abstract Task<TResult> ExecuteAsync(TArgs args, CancellationToken ct)`; `protected virtual Task<bool> VerifyAsync(TArgs args, TResult result, CancellationToken ct)`, the post-condition, true by default.
- `record ToolDescriptor(string Name, string Description, RiskTier Tier, string Permission, TimeSpan Timeout, JsonNode ArgumentSchema)`. The schema comes from `JsonSchemaExporter` with the binder's options, so the model in Phase 2 sees exactly what the binder enforces.
- `ToolRegistry(IEnumerable<ITool> tools, PermissionCatalog catalog)`: fails at startup on duplicate names, names outside `^[a-z][a-z0-9_]{2,47}$`, unknown permissions, or timeouts outside 1–120 s. `TryGet(string name, out ITool tool)`; `Descriptors`.
- `ArgumentBinder.Bind<T>(JsonElement json) → BindResult<T>`: unknown properties, missing required ones, wrong types and nulls where the type isn't nullable are all errors (`UnmappedMemberHandling.Disallow`, `RespectNullableAnnotations`, `RespectRequiredConstructorParameters`); then the tool's `Validate`.

- [ ] Write the tests: `Unknown_property_is_rejected`; `Missing_required_property_is_rejected`; `Wrong_type_is_rejected`; `Null_for_a_non_nullable_property_is_rejected`; `Tool_validation_errors_are_reported`; `Schema_lists_required_properties_and_forbids_others` (`required` holds the property, `additionalProperties` is false); `Registry_rejects_duplicate_names`; `Registry_rejects_unknown_permissions`; `Registry_rejects_bad_names`.
- [ ] Run → fail. Implement. Run → pass.
- [ ] Commit "Add tool definitions and argument checks", push.

### Task 6: Audit log
**Files:** `src/Aldren.Security/Gateway/Outcomes.cs`, `src/Aldren.Security/Audit/AuditRecord.cs`, `IAuditLog.cs`, `SqliteAuditLog.cs`, `DataFolderAcl.cs`; tests `AuditLogTests.cs`, `DataFolderAclTests.cs`.

**Interfaces (produces):**
- In `Outcomes.cs`, for the gateway in Task 7 as well: `enum CallOrigin { User, Model }`; `enum ToolOutcomeStatus { Succeeded, UnknownTool, InvalidArguments, PermissionDenied, Forbidden, Declined, TimedOut, PostconditionFailed, Failed, AuditUnavailable }`.
- `record AuditRecord(DateTimeOffset Timestamp, string RequestId, string Tool, RiskTier Tier, CallOrigin Origin, string Arguments, AuditStage Stage, ToolOutcomeStatus? Outcome, string? Detail, TimeSpan? Duration, bool Confirmed, bool Tainted)`; `enum AuditStage { Started, Finished }`.
- `interface IAuditLog { Task<long> AppendAsync(AuditRecord record, CancellationToken ct); Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct); Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken ct); }`. `QueryAsync` returns finished entries, newest first.
- `SqliteAuditLog(string databasePath)`: creates table `audit_log` on first use, WAL mode; arguments are redacted (`Redactor`) and cut to 2,000 characters before they are stored.
- `static void DataFolderAcl.RestrictToCurrentUser(string directory)`: a protected ACL giving only your account full control, inherited by everything inside.
- Permission changes are recorded too: tool `set_permission`, tier Impactful, origin User, detail such as `system.clock: off`.

- [ ] Write the tests, on a temporary database: `Entries_come_back_newest_first`; `Arguments_are_redacted_and_cut` (an `apiKey` and 5,000 characters → `***` and 2,000 characters plus `…`); `Prune_removes_entries_older_than_the_cutoff`; `Timestamps_round_trip_as_utc`; `Restricted_folder_grants_only_the_current_user` (exactly one access rule, for your SID, with inheritance protected).
- [ ] Run → fail. Implement. Run → pass.
- [ ] Commit "Add the audit log", push.

### Task 7: Tool Gateway
**Files:** `src/Aldren.Security/Gateway/ToolCall.cs`, `Policies.cs`, `IConfirmationBroker.cs`, `ToolGateway.cs`; tests `GatewayPipelineTests.cs`, `PolicyTests.cs`.

**Interfaces (consumes):** `ToolRegistry` and `ITool` (Task 5), `IPermissionStore` (Task 4), `IAuditLog`, `CallOrigin` and `ToolOutcomeStatus` (Task 6), `ConfirmationRequest` (Task 3).

**Interfaces (produces):**
- `record ToolCall(string Tool, JsonElement Arguments, string RequestId)`; `record CallContext(CallOrigin Origin, IReadOnlyList<string> UntrustedSources)` (empty when no untrusted content is in the model's context); `record ToolOutcome(ToolOutcomeStatus Status, JsonNode? Result, string Message)`.
- `interface IConfirmationBroker { Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct); }`.
- `enum ConfirmationLevel { None, Standard, Explicit }`; `record PolicyDecision(bool Allowed, ConfirmationLevel Confirmation, string? Reason)`; `interface IToolPolicy { PolicyDecision Evaluate(ToolDescriptor tool, CallContext context); }`.
  - `TierPolicy`: Forbidden is refused; Read and Reversible need no confirmation; Impactful needs Standard; Destructive needs Explicit, with full details.
  - `TaintPolicy`: a model-proposed call at tier Impactful or above, made while untrusted content is in context, needs at least Standard confirmation, and the request names the untrusted sources. Today the tier rule asks anyway; this policy keeps the rule once later phases let you relax a tier-2 tool.
- `ToolGateway(ToolRegistry, IPermissionStore, IEnumerable<IToolPolicy>, IConfirmationBroker, IAuditLog, TimeProvider, ILogger<ToolGateway>)` with `Task<ToolOutcome> InvokeAsync(ToolCall call, CallContext context, CancellationToken ct)`. Steps, in SECURITY.md's order: resolve the tool → bind and validate the arguments → permission → policies (the strictest decision wins) → audit "started" (fails closed above tier 0) → confirmation (60 s; no answer means declined; a broker error means declined) → run with the tool's timeout → post-condition → audit "finished" → sanitised result. Results over 8 KB become `{"truncated": true, "preview": "<first 8,000 characters>"}`. Exceptions become a short message without a stack trace (the full exception goes to the log). Every call is audited, refused ones included.

- [ ] Write the tests, with fake tools, a fake broker, an in-memory audit log and `FakeTimeProvider`:
  - `Forbidden_tier_never_runs_even_with_permission`; `Unknown_tool_is_refused_and_audited`; `Invalid_arguments_never_reach_the_tool`; `Missing_permission_is_refused`.
  - `Read_and_reversible_run_without_confirmation`; `Impactful_asks_and_runs_on_yes`; `Declined_confirmation_does_not_run_the_tool`; `Destructive_asks_for_explicit_confirmation` (tier and the tool's summary in the request); `Tainted_model_call_names_its_sources` (`UntrustedSources = ["web page: example.com"]` → in the request).
  - `Unanswered_confirmation_is_declined_after_60_seconds`; `Confirmation_failure_counts_as_declined`.
  - `Tool_timeout_cancels_and_reports_timed_out`; `Failed_postcondition_is_reported`; `Tool_exception_becomes_a_short_message`.
  - `Audit_failure_blocks_tier1_and_above`; `Audit_failure_does_not_block_reads`; `Every_outcome_is_audited` (one case per status, except AuditUnavailable, which goes to the log); `Large_results_are_capped`.
- [ ] Run → fail. Implement. Run → pass.
- [ ] Commit "Add the Tool Gateway", push.

### Task 8: Secret store
**Files:** `src/Aldren.Security/Secrets/ISecretStore.cs`, `CredentialManagerSecretStore.cs`, `NativeMethods.txt` (CsWin32: `CredReadW`, `CredWriteW`, `CredDeleteW`, `CredFree`); tests `SecretStoreTests.cs`.

**Interfaces (produces):** `interface ISecretStore { string? Get(string name); void Set(string name, string secret); bool Delete(string name); }`. Names match `^[a-z0-9][a-z0-9.-]{0,63}$` and are stored as generic credentials `Aldren/<name>`, kept on this PC only (`CRED_PERSIST_LOCAL_MACHINE`). Values are UTF-8, at most 2,560 bytes, and never logged.

- [ ] Write the tests: `Rejects_invalid_names`; `Rejects_oversized_values`; `Round_trips_a_secret` (writes `test-<guid>`, reads it, deletes it, and then reads null). The last one touches the real Credential Manager, so it is skipped unless `ALDREN_TEST_CREDMAN=1`; it runs once, with your OK (see Setup).
- [ ] Run → fail. Implement. Run → pass (the integration test when you allow it).
- [ ] Commit "Add the Credential Manager secret store", push.

### Task 9: Settings
**Files:** `src/Aldren.Core/Settings/SettingsStore.cs`, `SettingsMigrator.cs`, `SettingsValidator.cs`; tests in `tests/Aldren.Core.Tests/Settings/`.

**Interfaces (produces):**
- `SettingsStore(AldrenPaths paths, SettingsMigrator migrator, ILogger<SettingsStore> log)`: `AldrenSettings Current`, `string? LoadProblem`, `event Action<AldrenSettings>? Changed`, `Task<SettingsResult> SaveAsync(AldrenSettings settings, CancellationToken ct)` (validates, then writes `settings.json` atomically).
- `SettingsMigrator(IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> steps)`: step n upgrades version n to n + 1. The current version is 1, with no steps yet.
- `static IReadOnlyList<string> SettingsValidator.Validate(AldrenSettings settings)`: the hotkey must parse (`Hotkey.TryParse`).

- [ ] Write the tests: `Defaults_without_a_file`; `Saved_settings_load_back`; `Invalid_settings_are_not_saved` (hotkey `Space` → an error, file unchanged); `Older_file_is_migrated_and_saved` (a test step renames a property from version 0); `Newer_file_is_left_untouched` (version 99: defaults in memory, `LoadProblem` set, `SaveAsync` refused, the file's bytes unchanged); `Corrupt_file_is_set_aside` (renamed `settings.json.bad-<UTC time>`, defaults, `LoadProblem` set).
- [ ] Run → fail. Implement. Run → pass.
- [ ] Commit "Add versioned settings", push.

### Task 10: Start with Windows
**Files:** `src/Aldren.Core/Startup/IRegistryValues.cs`, `CurrentUserRegistry.cs`, `StartupEntry.cs`; tests `StartupEntryTests.cs` (a fake registry; the real one is only touched in the acceptance script).

**Interfaces (produces):**
- `interface IRegistryValues { object? Get(string key, string name); void Set(string key, string name, string value); void Delete(string key, string name); }` over `HKEY_CURRENT_USER`.
- `StartupEntry(IRegistryValues registry)`: `StartupState GetState(string executablePath)`, `void Enable(string executablePath)`, `void Disable()`. The value `ALDREN` under `Software\Microsoft\Windows\CurrentVersion\Run` holds `"<path>" --startup`. On means the value points to this executable. `DisabledInWindows` means you switched it off in Task Manager or in Settings > Apps > Startup: the `StartupApproved\Run` entry exists and its first byte is something other than 0x02 or 0x06. ALDREN never overrides that choice; Settings tells you where to change it.

- [ ] Write the tests: `Off_without_a_run_value`; `Enable_writes_the_quoted_path_with_the_startup_flag`; `On_only_when_the_value_points_to_this_executable`; `Switched_off_in_task_manager_reads_as_disabled_in_windows` (first byte 0x03); `Disable_removes_the_value`.
- [ ] Run → fail. Implement. Run → pass.
- [ ] Commit "Add the start-with-Windows toggle", push.

### Task 11: Core process
**Files:** `src/Aldren.Core/`: `Program.cs`, `Supervision/IProcessLauncher.cs`, `ProcessLauncher.cs`, `ChildProcessJob.cs`, `AppSupervisor.cs`, `Ipc/CoreRpcServer.cs`, `CoreService.cs`, `AppConfirmationBroker.cs`, `Commands/PlaceholderCommands.cs`, `Tools/GetTimeTool.cs`, `Tools/DiagnosticsConfirmTool.cs`, `Maintenance/AuditRetentionService.cs`, `NativeMethods.txt`; tests in `tests/Aldren.Core.Tests/Core/`.

**Interfaces (produces):**
- `interface IProcessLauncher { IChildProcess Start(string path, string arguments, bool breakAwayFromJob); IChildProcess? Attach(int processId); }`; `interface IChildProcess : IDisposable { int Id { get; } bool HasExited { get; } Task WaitForExitAsync(CancellationToken ct); }`.
- `ChildProcessJob : IDisposable` with `Add(IChildProcess)`: a Job Object that kills its processes when Core goes away. llama-server and the gateway join it in later phases; the App never does.
- `AppSupervisor : BackgroundService` with `RequestShutdown()`. Started with `--startup`, it first waits 15 s. It launches `Aldren.App.exe` from Core's own folder (`--app <path>` overrides it for development), breaking away from any job Core itself runs in, and falling back to a normal launch if Windows refuses. If `Local\Aldren.App` is already held, it adopts that App: it waits for its connection and watches its process (the pipe client's process ID). It restarts the App after each exit using `RestartBackoff`, never after `RequestShutdown()`. If the backoff gives up, it shows one message box naming the log folder.
- `CoreRpcServer : BackgroundService`: accepts one App connection at a time on `AldrenPipe`, serves `CoreService`, keeps the App's `IAldrenApp` proxy for the broker, and listens again after a disconnect.
- `CoreService : IAldrenCore`: status (version, process ID, start time, `StartupEntry` state); `SubmitTextAsync` → `PlaceholderCommands` as a User-origin call; settings → `SettingsStore`; permissions → `IPermissionStore`, each change audited; audit → `IAuditLog`; Start with Windows → `StartupEntry` with Core's own path; `ShutdownAsync` → `AppSupervisor.RequestShutdown()`, then the host stops.
- `AppConfirmationBroker : IConfirmationBroker`: forwards to the connected App, and declines at once when none is connected or the App disconnects mid-question.
- `PlaceholderCommands(ToolGateway gateway)`, `Task<string> HandleAsync(string text, CancellationToken ct)`, standing in until Phase 2's fast path: "time", "what time is it" and "what's the time" (any case, punctuation ignored) call `get_time`; "test confirmation" calls `diagnostics_confirm_test`; anything else gets "I can't do that yet: understanding requests arrives in Phase 2."
- `GetTimeTool`: tier Read, permission `system.clock`, `record GetTimeArgs()`, `record TimeResult(string LocalTime, string TimeZone)`. `DiagnosticsConfirmTool`: tier Impactful, permission `diagnostics.test`; it changes nothing and returns "confirmed", so the confirmation path can be tried end to end.
- `Program`: holds `Local\Aldren.Core` (a second Core exits at once) → paths and logging → `DataFolderAcl` on the data folder → host → runs until `ShutdownAsync` or logoff. On logoff (`SystemEvents.SessionEnding`) it stops gracefully and tells the supervisor not to restart anything.

- [ ] Write the tests, with a fake launcher and `FakeTimeProvider`: `Supervisor_starts_the_app_after_the_startup_delay` (nothing before 15 s, launched at 15 s); `Supervisor_adopts_a_running_app`; `Supervisor_restarts_a_crashed_app_with_backoff` (after 1 s, then 2 s); `AppSupervisor_does_not_restart_after_shutdown`; `Job_kills_children_when_disposed` (a real `Aldren.TestChild` exits within 2 s); `Broker_declines_when_no_app_is_connected`; `Broker_declines_when_the_app_disconnects_mid_question`; `Placeholder_tells_the_time_through_the_gateway` (the reply has the time; the audit log has `get_time` Succeeded); `Placeholder_runs_the_confirmation_test_only_on_yes`; `Unrecognised_text_gets_the_phase_2_reply`; `Retention_prunes_entries_older_than_180_days`; `Permission_changes_are_audited`.
- [ ] Run → fail. Implement. Run → pass.
- [ ] Publish and start Core on its own from a terminal tab. Expected: it logs that it's waiting, can't find the App yet (Task 12 adds it), and backs off without a crash loop; a second Core exits at once.
- [ ] Commit "Add the Core process", push.

### Task 12: App shell, tray and Settings
**Files:** `src/Aldren.App/`: `Program.cs` (custom `Main`: holds `Local\Aldren.App`, a second App exits), `App.xaml(.cs)` (host, logging, starts hidden), `Ipc/CoreClient.cs`, `Ipc/AppCallbacks.cs`, `Ipc/CoreWatchdog.cs`, `Tray/TrayIcon.cs`, `Settings/SettingsWindow.xaml` with `GeneralPage`, `PermissionsPage`, `ActivityPage`, `DiagnosticsPage` and `ComingLaterPage`, `Expanded/ExpandedWindow.xaml`, `Assets/aldren.ico` (drawn by `tools/dev/make-icon.ps1`), `NativeMethods.txt`; policy and tests: `src/Aldren.Common/Shell/CoreWatchdogPolicy.cs`, `tests/Aldren.Core.Tests/Common/CoreWatchdogPolicyTests.cs`.

**Interfaces (produces):**
- `CoreWatchdogPolicy(RestartBackoff backoff)` with `TimeSpan? OnCoreLost(bool quitRequested, bool sessionEnding, bool coreStillRunning)`; null means don't relaunch.
- `CoreClient`: connects with backoff (250 ms up to 5 s), exposes `IAldrenCore` and `Connected` / `Disconnected` events, and registers `AppCallbacks` (`IAldrenApp`) as the local target. When the connection drops and the policy says so, it relaunches `Aldren.Core.exe --relaunched` from its own folder.
- `TrayIcon`: `Shell_NotifyIconW` on the App's hidden top-level window, which also receives the hotkey and the logoff messages (a message-only window would miss the `TaskbarCreated` and end-of-session broadcasts). Logoff sets the watchdog's `sessionEnding`. Menu: Open ALDREN, Settings, —, Pause listening (greyed: Phase 3), Gaming mode (greyed: Phase 2), Focus (greyed: Phase 8), —, Start with Windows (checked from `CoreStatus`), Quit. It re-adds itself when Explorer restarts (`TaskbarCreated`).
- Settings: a NavigationView with every section from ARCHITECTURE §13.
  - General: Start with Windows (with the "switched off in Windows" note when it applies), the active hotkey (with the fallback note), "Show ALDREN on stream".
  - Permissions: one switch per permission, from Core.
  - Activity history: the latest 100 audit entries.
  - Diagnostics: show each island state, run the confirmation test, write a redaction test line to the log, open the log folder, Core and App versions.
  - The other sections say which phase brings them.
- Expanded window: Mica, a conversation list and a text box that uses `SubmitTextAsync`.
- Quit: the App calls `ShutdownAsync`, Core stops without restarting anything, and then the App exits.

- [ ] Write the tests: `Watchdog_relaunches_a_crashed_core` (→ 1 s); `Watchdog_never_relaunches_while_session_ends`; `Watchdog_never_relaunches_after_quit`; `Watchdog_reconnects_instead_when_core_is_still_running` (→ null).
- [ ] Run → fail. Implement. Run → pass.
- [ ] Publish and start Core from Explorer. Expected: no window, the tray icon appears; Settings opens; switching `system.clock` off shows up in Activity history; Quit leaves no ALDREN process.
- [ ] Commit "Add the App shell", push.

### Task 13: Island, hotkey and confirmations
**Files:** `src/Aldren.App/Island/IslandWindow.xaml(.cs)` and its native helpers, ported from `spikes/S2.Island` (transparency, click-through, topmost, capture exclusion; S2's interop code is kept as it is), `Island/IslandState.cs`, `Island/TextEntry.xaml`, `Hotkeys/HotkeyService.cs`.

- [ ] Hotkey: `HotkeyChooser` registers the configured hotkey or its fallback through `RegisterHotKey` on the App's hidden window (Task 12), re-registers when you change it in Settings, and Settings shows which one is active.
- [ ] Text: the hotkey opens the island in text mode on the monitor with the foreground window (`IslandPlacement`), with the caret in the box. Enter sends the text to Core and shows the reply. Esc dismisses it and gives focus back to the previous window. Ctrl+Enter opens the expanded window. The island collapses after 8 s without input.
- [ ] Confirmations: `ConfirmAsync` shows the confirm state (the tool's summary, its tier, a "suggested while reading …" line when the request names untrusted sources, Yes and No, and the Y and N keys). It returns on a click; a cancelled request closes it.
- [ ] States: listening, transcribing, thinking, acting, researching, speaking, confirm and error, with sample content for Diagnostics; the real ones arrive in Phase 3. When Windows' "Animation effects" setting is off, the morphs are instant.
- [ ] Hidden means hidden: S2's 33 ms cursor check for click-through runs only while the island is visible, and nothing else ticks.
- [ ] Publish and check. Expected: the hotkey and "what time is it" give the time; "test confirmation" asks, and only Yes runs it; the island doesn't appear in a screenshot unless "Show ALDREN on stream" is on; typing in another app is never interrupted until you press the hotkey.
- [ ] Commit "Add the island and hotkey", push.

### Task 14: Idle check, docs and acceptance
**Files:** `tools/bench/Commands/IdleCommand.cs`; tests `tests/Aldren.Bench.Tests/IdleTests.cs`; `docs/acceptance/phase-1.md`, `docs/STATUS.md`, `docs/ARCHITECTURE.md` (§4 and §21 lines from Setup), `docs/SECURITY.md` (the two rules from Setup), `README.md` (how to build, publish and run).

- [ ] `aldren-bench idle [--minutes 10]` samples `Aldren.Core` and `Aldren.App` every 5 s and reports CPU (as Task Manager shows it, both processes together) and private working set (peak and final) against the 1% and 300 MB targets; it appends to `E:\AldrenData\bench\phase1\idle.md`.
- [ ] Test first: `Cpu_percent_matches_task_manager` (1 s of CPU over 10 s on 12 logical processors → 0.83%). Run → fail. Implement `CpuPercent(TimeSpan cpu, TimeSpan wall, int logicalProcessors)`. Run → pass.
- [ ] Acceptance script, for you to run, with the expected result for each step:
  1. Build and publish.
  2. Start ALDREN: tray icon, no window.
  3. Hotkey and "what time is it".
  4. "test confirmation": once Yes, once No, once ignored for 60 s.
  5. Switch `system.clock` off: the time is refused with a clear message, and Activity history shows both the change and the refusal.
  6. End `Aldren.App` in Task Manager: it's back within about 2 s.
  7. End `Aldren.Core`: the App relaunches it.
  8. Quit: both gone, and nothing comes back.
  9. Start with Windows on, sign out and in: the tray icon appears after about 15 s, with no window.
  10. `aldren-bench idle` for 10 minutes.
  11. The redaction test line shows `***` in the log.
  12. The Credential Manager test (`ALDREN_TEST_CREDMAN=1`).
- [ ] Update STATUS (each Phase 1 row Implemented, then Tested as you confirm each step on this laptop), ARCHITECTURE, SECURITY and README.
- [ ] Run the whole suite. Expected: all tests pass (the Credential Manager one skipped unless enabled).
- [ ] `tools/check-repo.ps1`, commit "Add the Phase 1 idle check and acceptance script", push.
