using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Aldren.Security.Secrets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aldren.Cloud;

/// <param name="Root">E:\AldrenData\gateway: the pinned release and the pinned portable Node live here, never in the repo.</param>
/// <param name="DatabasePath">%LOCALAPPDATA%\Aldren\gateway\freeapi.db.</param>
public sealed record GatewayOptions(string Root, string DatabasePath, string LogPath)
{
    public string Version { get; init; } = "0.16.14";
    /// <summary>Node 24 LTS: the gateway supports Node 20.18–24, and its better-sqlite3 build targets Node 24 (module ABI 137).</summary>
    public string NodeVersion { get; init; } = "24.22.2";
    public string AppDirectory => Path.Combine(Root, $"freellmapi-{Version}");
    public string NodePath => Path.Combine(Root, $"node-v{NodeVersion}-win-x64", "node.exe");
    public string EntryPoint => Path.Combine(AppDirectory, "server", "dist", "index.js");
}

public sealed record GatewayStatus(bool Running, int? Port, string? Problem, DriftReport? Drift);

/// <summary>Puts a child process in Core's kill-on-close Job Object, so it never outlives Core.</summary>
public interface IProcessJob
{
    void Add(Process process);
}

/// <summary>
/// Runs FreeLLMAPI for ALDREN (ARCHITECTURE §8.1, SECURITY.md): the pinned release on the pinned Node, 127.0.0.1 and a
/// random port, the hardening environment, short provider timeouts, the encryption key from Credential Manager. Restarted
/// with backoff if it dies; drift-checked whenever it starts.
/// </summary>
public sealed class GatewaySupervisor(
    GatewayOptions options,
    ISecretStore secrets,
    IProcessJob job,
    Func<bool> enabled,
    ILogger<GatewaySupervisor> log) : BackgroundService
{
    private const string KeyName = "gateway-encryption-key";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private Process? _process;
    private volatile GatewayStatus _status = new(false, null, "not started", null);

    public GatewayStatus Status => _status;

    public Uri? BaseUri => _status.Running && _status.Port is { } p ? new Uri($"http://127.0.0.1:{p}/") : null;

    public GatewayDatabase Database { get; } = new(options.DatabasePath);

    /// <summary>Runs the drift check again (after you change the chains in the dashboard).</summary>
    public DriftReport? Recheck()
    {
        try
        {
            var drift = Database.Check();
            _status = _status with { Drift = drift };
            return drift;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            log.LogWarning(ex, "Gateway drift check failed");
            return null;
        }
    }

    /// <summary>Blocks one chain after a routing mismatch until the next start or recheck.</summary>
    public void BlockChain(CloudRole role, string reason)
    {
        if (_status.Drift is { } drift)
        {
            var chains = drift.Chains.Select(c => c.Role == role ? c with { State = ChainState.Disallowed, Problem = reason } : c).ToList();
            _status = _status with { Drift = drift with { Chains = chains } };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!enabled())
            {
                _status = new GatewayStatus(false, null, "cloud is switched off in Settings", null);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
                continue;
            }

            try
            {
                await RunOnceAsync(stoppingToken);
                delay = TimeSpan.FromSeconds(5);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "The gateway stopped or failed to start");
                _status = new GatewayStatus(false, null, ex.Message, null);
            }

            await Task.Delay(delay, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
            delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
        }

        Stop();
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        if (!File.Exists(options.NodePath) || !File.Exists(options.EntryPoint))
        {
            _status = new GatewayStatus(false, null, $"FreeLLMAPI {options.Version} or its Node isn't installed in {options.Root}", null);
            await Task.Delay(Timeout.Infinite, ct);
            return;
        }

        var port = FreePort();
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(options.LogPath)!);
        var psi = new ProcessStartInfo(options.NodePath)
        {
            WorkingDirectory = options.AppDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(options.EntryPoint);
        foreach (var (name, value) in HardenedEnvironment(port))
        {
            psi.Environment[name] = value;
        }

        var writer = TextWriter.Synchronized(new StreamWriter(options.LogPath, append: true) { AutoFlush = true });
        var process = Process.Start(psi) ?? throw new InvalidOperationException("The gateway didn't start.");
        process.OutputDataReceived += (_, e) => Write(writer, e.Data);
        process.ErrorDataReceived += (_, e) => Write(writer, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        job.Add(process);
        _process = process;

        try
        {
            var clock = Stopwatch.StartNew();
            while (!await PingAsync(port, ct))
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"The gateway exited during start-up (code {process.ExitCode}); see {options.LogPath}.");
                }

                if (clock.Elapsed > TimeSpan.FromSeconds(60))
                {
                    throw new TimeoutException("The gateway didn't answer within 60 s.");
                }

                await Task.Delay(250, ct);
            }

            var created = Database.EnsureChains();
            var drift = Database.Check();
            _status = new GatewayStatus(true, port, null, drift);
            log.LogInformation("Gateway ready on 127.0.0.1:{Port} in {Ms} ms; {Created} chains created; usable: {Usable}; keys outside the allowlist: {Blocked}",
                port, clock.ElapsedMilliseconds, created,
                string.Join(", ", drift.Chains.Where(c => drift.Usable(c.Role)).Select(c => c.Name)) is { Length: > 0 } u ? u : "none",
                drift.KeysOutsideAllowlist.Count == 0 ? "none" : string.Join(", ", drift.KeysOutsideAllowlist));
            await process.WaitForExitAsync(ct);
            throw new InvalidOperationException($"The gateway exited (code {process.ExitCode}).");
        }
        finally
        {
            _status = _status with { Running = false };
            Stop();
            writer.Dispose();
        }
    }

    /// <summary>SECURITY.md's hardening, plus ADR-0012's short provider timeouts.</summary>
    private IEnumerable<(string, string)> HardenedEnvironment(int port)
    {
        yield return ("NODE_ENV", "production");
        yield return ("HOST", "127.0.0.1");
        yield return ("PORT", port.ToString(CultureInfo.InvariantCulture));
        yield return ("ALLOWED_HOSTS", "localhost");
        yield return ("FALLBACK_TIME_BUDGET_MS", "8000");
        yield return ("PROVIDER_STREAM_STALL_TIMEOUT_MS", "15000");
        yield return ("PROVIDER_TIMEOUT_MS", "15000");
        yield return ("PROVIDER_TIMEOUT_GOOGLE", "15000");
        yield return ("PROVIDER_TIMEOUT_GROQ", "15000");
        yield return ("REQUEST_ANALYTICS_LOG_CLIENT", "false");
        yield return ("FREELLMAPI_UPDATE_CHECK", "off");
        yield return ("FREEAPI_DB_PATH", options.DatabasePath);
        yield return ("FREEAPI_DB_DIR_HARDENING", "1");
        yield return ("FREEAPI_ENV_PATH", Path.Combine(options.Root, "no-such.env"));
        yield return ("ENCRYPTION_KEY", EncryptionKey());
    }

    private string EncryptionKey()
    {
        var key = secrets.Get(KeyName);
        if (key is null)
        {
            key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            secrets.Set(KeyName, key);
            log.LogInformation("Generated the gateway's encryption key (kept in Windows Credential Manager)");
        }

        return key;
    }

    private static async Task<bool> PingAsync(int port, CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{port}/api/ping", ct);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static void Write(TextWriter writer, string? line)
    {
        if (line is null)
        {
            return;
        }

        try
        {
            writer.WriteLine(line);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Stop()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        process.Dispose();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
