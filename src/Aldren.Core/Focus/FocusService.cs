using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aldren.Browser;
using Aldren.Core.Ipc;
using Aldren.Core.Voice;
using Aldren.Security.Gateway;
using Aldren.Windows.Apps;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aldren.Core.Focus;

public sealed record FocusProfile(string Name, IReadOnlyList<string> Apps, IReadOnlyList<string> Sites);

public sealed record FocusSession(long Id, string Goal, string Profile, DateTimeOffset Started, DateTimeOffset Ends, int Slips);

public sealed record FocusStats(int Sessions, int Minutes, int Slips, int EndedEarly);

/// <summary>
/// Focus mode (ARCHITECTURE §16): a session is a lease with an end time, kept in the database. Distracting apps in front are
/// nudged (the first slip is forgiven), then minimised, then closed through their own close button; distracting sites are
/// redirected by the Companion extension, whose rules expire on their own at the end time even if ALDREN crashed. The
/// emergency exit always works. Every enforcement step goes through the Tool Gateway and is audited.
/// </summary>
public sealed class FocusService(
    string databasePath,
    BrowserBridge browser,
    IServiceProvider services,
    AppConnections connections,
    TimeProvider time,
    ILogger<FocusService> log) : BackgroundService
{
    public static readonly IReadOnlyDictionary<string, FocusProfile> Profiles = new Dictionary<string, FocusProfile>(StringComparer.OrdinalIgnoreCase)
    {
        ["study"] = new("study",
            ["discord", "steam", "epicgameslauncher", "battle.net", "riotclientservices", "leagueclient", "valorant", "whatsapp", "telegram", "netflix"],
            ["youtube.com", "reddit.com", "x.com", "twitter.com", "instagram.com", "tiktok.com", "facebook.com", "netflix.com", "twitch.tv", "9gag.com"]),
        ["work"] = new("work",
            ["steam", "epicgameslauncher", "battle.net", "riotclientservices", "leagueclient", "valorant", "netflix"],
            ["reddit.com", "instagram.com", "tiktok.com", "netflix.com", "twitch.tv", "9gag.com", "youtube.com"]),
    };

    private static readonly string[] Gentle = ["Back to it, boss. {0} will still be there later.", "Eyes on the goal, boss: {1}."];
    private static readonly string[] Sarcastic = ["Ah, {0} again. Fascinating. Your goal, {1}, misses you.", "I'm sure {0} is urgent. So is {1}."];
    private static readonly string[] Disappointed = ["I'm not angry, boss. Just quietly devastated. {0}, closed. {1} awaits.", "Third time. I've closed {0}. Let's finish {1} together."];

    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 5 }.ToString();
    private readonly Dictionary<string, (int Strikes, DateTimeOffset Last)> _slips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private FocusSession? _active;
    private bool _ready;

    // Resolved when first needed: the gateway and the voice loop depend on the tools, which depend on this service.
    private ToolGateway Gateway => (ToolGateway)services.GetService(typeof(ToolGateway))!;

    private VoiceLoop Voice => (VoiceLoop)services.GetService(typeof(VoiceLoop))!;

    public FocusSession? Active => _active;

    public async Task<FocusSession> StartAsync(string goal, int minutes, string profile, CancellationToken ct)
    {
        if (_active is { } current && current.Ends > time.GetUtcNow())
        {
            throw new InvalidOperationException($"A focus session on \"{current.Goal}\" is already running until {Local(current.Ends)}. Say \"end focus\" first.");
        }

        var p = Profiles.TryGetValue(profile, out var known) ? known : Profiles["study"];
        var now = time.GetUtcNow();
        var ends = now.AddMinutes(Math.Clamp(minutes, 5, 240));
        await using var db = await OpenAsync(ct);
        await using var insert = db.CreateCommand();
        insert.CommandText = "INSERT INTO focus_sessions (goal, profile, started_utc, ends_utc, slips, early) VALUES ($g, $p, $s, $e, 0, 0); SELECT last_insert_rowid();";
        insert.Parameters.AddWithValue("$g", goal);
        insert.Parameters.AddWithValue("$p", p.Name);
        insert.Parameters.AddWithValue("$s", now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$e", ends.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        var session = new FocusSession((long)(await insert.ExecuteScalarAsync(ct))!, goal, p.Name, now, ends, 0);
        lock (_lock)
        {
            _active = session;
            _slips.Clear();
        }

        await BlockSitesAsync(session, p, ct);
        log.LogInformation("Focus session {Id} started: {Minutes} min, profile {Profile}", session.Id, minutes, p.Name);
        return session;
    }

    /// <summary>The emergency exit, or a normal stop: always works, and lifts every restriction at once.</summary>
    public async Task<FocusSession?> EndAsync(bool early, CancellationToken ct)
    {
        FocusSession? session;
        lock (_lock)
        {
            session = _active;
            _active = null;
        }

        await ClearSitesAsync(ct);
        if (session is null)
        {
            return null;
        }

        await using var db = await OpenAsync(ct);
        await using var update = db.CreateCommand();
        update.CommandText = "UPDATE focus_sessions SET ended_utc = $n, slips = $s, early = $e WHERE id = $id;";
        update.Parameters.AddWithValue("$n", time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$s", session.Slips);
        update.Parameters.AddWithValue("$e", early ? 1 : 0);
        update.Parameters.AddWithValue("$id", session.Id);
        await update.ExecuteNonQueryAsync(ct);
        log.LogInformation("Focus session {Id} ended{Early}", session.Id, early ? " early" : "");
        return session;
    }

    public async Task<FocusStats> StatsAsync(int days, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM((julianday(COALESCE(ended_utc, ends_utc)) - julianday(started_utc)) * 1440), 0), COALESCE(SUM(slips), 0), COALESCE(SUM(early), 0)
            FROM focus_sessions WHERE started_utc >= $since;
            """;
        command.Parameters.AddWithValue("$since", time.GetUtcNow().AddDays(-days).UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new FocusStats(reader.GetInt32(0), (int)Math.Round(reader.GetDouble(1)), reader.GetInt32(2), reader.GetInt32(3));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Focus check failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>At start: a session that ended while ALDREN was down is closed and its restrictions cleared; one still running resumes.</summary>
    private async Task RecoverAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT id, goal, profile, started_utc, ends_utc, slips FROM focus_sessions WHERE ended_utc IS NULL ORDER BY id DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return;
        }

        var session = new FocusSession(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture), reader.GetInt32(5));
        await reader.DisposeAsync();
        lock (_lock)
        {
            _active = session;
        }

        if (session.Ends <= time.GetUtcNow())
        {
            await EndAsync(early: false, ct);
        }
        else
        {
            log.LogInformation("Resuming focus session {Id} until {Ends}", session.Id, session.Ends);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var session = _active;
        if (session is null)
        {
            return;
        }

        if (time.GetUtcNow() >= session.Ends)
        {
            await EndAsync(early: false, ct);
            var minutes = (int)Math.Round((session.Ends - session.Started).TotalMinutes);
            var summary = $"Focus session done: {minutes} minutes on {session.Goal}, {session.Slips} slip{(session.Slips == 1 ? "" : "s")}. {(session.Slips == 0 ? "Flawless, boss." : "Well fought.")} Take a ten-minute break.";
            await NotifyAsync("Focus session done", summary, ct);
            await Voice.AnnounceAsync(summary, ct);
            return;
        }

        var profile = Profiles.TryGetValue(session.Profile, out var p) ? p : Profiles["study"];
        var foreground = WindowFinder.List().FirstOrDefault(w => w.Handle == WindowFinder.GetForegroundWindow());
        if (foreground is null)
        {
            return;
        }

        var app = profile.Apps.FirstOrDefault(a => foreground.ProcessName.Contains(a, StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            return;
        }

        await SlipAsync(session, foreground, app, ct);
    }

    private async Task SlipAsync(FocusSession session, TopWindow window, string app, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        int strikes;
        lock (_lock)
        {
            var (count, last) = _slips.TryGetValue(app, out var s) ? s : (0, DateTimeOffset.MinValue);
            if (now - last < TimeSpan.FromSeconds(8))
            {
                return; // the same slip, still being handled
            }

            strikes = count + 1;
            _slips[app] = (strikes, now);
            _active = session with { Slips = session.Slips + 1 };
        }

        var name = WindowFinder.Describe(window);
        string line;
        switch (strikes)
        {
            case 1:
                // The first slip is forgiven: a nudge, nothing closed.
                line = string.Format(CultureInfo.InvariantCulture, Gentle[now.Second % Gentle.Length], name, session.Goal);
                break;
            case 2:
                line = string.Format(CultureInfo.InvariantCulture, Sarcastic[now.Second % Sarcastic.Length], name, session.Goal);
                await EnforceAsync(window, "minimize", ct);
                break;
            default:
                line = string.Format(CultureInfo.InvariantCulture, Disappointed[now.Second % Disappointed.Length], name, session.Goal);
                await EnforceAsync(window, "close", ct);
                break;
        }

        log.LogInformation("Focus slip {Strikes} on {App}", strikes, app);
        await NotifyAsync("Focus", line, ct);
        _ = Voice.AnnounceAsync(line, ct);
    }

    private async Task EnforceAsync(TopWindow window, string action, CancellationToken ct)
    {
        var args = JsonDocument.Parse(JsonSerializer.Serialize(new { app = window.ProcessName, action })).RootElement.Clone();
        var outcome = await Gateway.InvokeAsync(new ToolCall("focus_enforce", args, Guid.NewGuid().ToString("N")), new CallContext(CallOrigin.User, []), ct);
        if (outcome.Status != ToolOutcomeStatus.Succeeded)
        {
            log.LogInformation("Focus enforcement not applied: {Message}", outcome.Message);
        }
    }

    private async Task BlockSitesAsync(FocusSession session, FocusProfile profile, CancellationToken ct)
    {
        try
        {
            await browser.SendAsync("focusBlock", new JsonObject
            {
                ["domains"] = new JsonArray([.. profile.Sites.Select(s => (JsonNode)s)]),
                ["until"] = session.Ends.ToUnixTimeMilliseconds(),
                ["goal"] = session.Goal,
            }, ct);
        }
        catch (InvalidOperationException ex)
        {
            log.LogInformation("Sites not blocked (the Companion isn't connected): {Reason}", ex.Message);
        }
    }

    private async Task ClearSitesAsync(CancellationToken ct)
    {
        try
        {
            await browser.SendAsync("focusClear", null, ct, TimeSpan.FromSeconds(3));
        }
        catch (InvalidOperationException)
        {
            // Not connected: the extension clears its own rules at their end time.
        }
    }

    private async Task NotifyAsync(string title, string message, CancellationToken ct)
    {
        try
        {
            if (connections.Current is { } app)
            {
                await app.App.NotifyAsync(title, message, app.Disconnected);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogDebug(ex, "Focus notification didn't reach the App");
        }
    }

    public string Local(DateTimeOffset when) => TimeZoneInfo.ConvertTime(when, time.LocalTimeZone).ToString("t", CultureInfo.CurrentCulture);

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        if (!_ready)
        {
            await using var create = db.CreateCommand();
            create.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS focus_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, goal TEXT NOT NULL, profile TEXT NOT NULL, started_utc TEXT NOT NULL,
                    ends_utc TEXT NOT NULL, ended_utc TEXT NULL, slips INTEGER NOT NULL DEFAULT 0, early INTEGER NOT NULL DEFAULT 0);
                """;
            await create.ExecuteNonQueryAsync(ct);
            _ready = true;
        }

        return db;
    }
}
