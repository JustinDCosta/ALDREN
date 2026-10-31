using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Aldren.Bench.Llama;

public record LlamaServerOptions(
    string ModelPath,
    string? ProjectorPath,
    int ContextSize,
    bool ProjectorOnCpu,
    string KvCacheType = "q8_0",
    IReadOnlyList<string>? ExtraArgs = null);

/// <summary>
/// Runs llama-server on 127.0.0.1 with a random port and API key, and stops it (and any children) on dispose.
/// </summary>
public sealed class LlamaServer : IAsyncDisposable
{
    public static string ExecutablePath { get; set; } =
        Environment.GetEnvironmentVariable("ALDREN_LLAMA_SERVER") ?? @"E:\AldrenData\tools\llama.cpp\b12023\llama-server.exe";

    public static string LogDirectory { get; set; } = @"E:\AldrenData\bench\logs";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    private readonly Process _process;
    private readonly TextWriter _log;

    private LlamaServer(Process process, TextWriter log, Uri baseUri, string apiKey, TimeSpan loadTime, string logPath)
    {
        _process = process;
        _log = log;
        BaseUri = baseUri;
        ApiKey = apiKey;
        LoadTime = loadTime;
        LogPath = logPath;
    }

    public Uri BaseUri { get; }
    public string ApiKey { get; }
    public TimeSpan LoadTime { get; }
    public string LogPath { get; }
    public int ProcessId => _process.Id;

    public static Task<LlamaServer> StartAsync(LlamaServerOptions options, CancellationToken ct) =>
        StartWithAsync((port, apiKey) => Arguments(options, port, apiKey), ct);

    /// <summary>Starts llama-server in router mode: it loads models from a folder on demand, at most maxModels at a time.</summary>
    public static Task<LlamaServer> StartRouterAsync(string modelsDirectory, int maxModels, IReadOnlyList<string> extraArgs, CancellationToken ct) =>
        StartWithAsync((port, apiKey) =>
        [
            "--models-dir", modelsDirectory,
            "--models-max", maxModels.ToString(CultureInfo.InvariantCulture),
            "--host", "127.0.0.1",
            "--port", port.ToString(CultureInfo.InvariantCulture),
            "--api-key", apiKey,
            .. extraArgs,
        ], ct);

    private static async Task<LlamaServer> StartWithAsync(Func<int, string, IEnumerable<string>> arguments, CancellationToken ct)
    {
        var port = FreePort();
        var apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        var psi = new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments(port, apiKey))
        {
            psi.ArgumentList.Add(arg);
        }

        Directory.CreateDirectory(LogDirectory);
        var logPath = Path.Combine(LogDirectory, $"llama-server-{DateTime.Now:yyyyMMdd-HHmmss}-{port}.log");
        var log = TextWriter.Synchronized(new StreamWriter(logPath) { AutoFlush = true });

        var clock = Stopwatch.StartNew();
        var process = Process.Start(psi) ?? throw new InvalidOperationException("llama-server did not start.");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var baseUri = new Uri($"http://127.0.0.1:{port}/");
        try
        {
            await WaitUntilHealthyAsync(baseUri, process, logPath, ct);
        }
        catch
        {
            Stop(process);
            log.Dispose();
            throw;
        }

        return new LlamaServer(process, log, baseUri, apiKey, clock.Elapsed, logPath);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            Stop(_process);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
        _log.Dispose();
    }

    private static IEnumerable<string> Arguments(LlamaServerOptions options, int port, string apiKey)
    {
        var ctx = options.ContextSize.ToString(CultureInfo.InvariantCulture);
        string[] args =
        [
            "-m", options.ModelPath,
            "--host", "127.0.0.1",
            "--port", port.ToString(CultureInfo.InvariantCulture),
            "--api-key", apiKey,
            "-c", ctx,
            "-ngl", "999",
            "-fa", "on",
            "--cache-type-k", options.KvCacheType,
            "--cache-type-v", options.KvCacheType,
            "--jinja",
        ];

        foreach (var arg in args)
        {
            yield return arg;
        }

        if (options.ProjectorPath is not null)
        {
            yield return "--mmproj";
            yield return options.ProjectorPath;
            if (options.ProjectorOnCpu)
            {
                yield return "--no-mmproj-offload";
            }
        }

        foreach (var arg in options.ExtraArgs ?? [])
        {
            yield return arg;
        }
    }

    private static async Task WaitUntilHealthyAsync(Uri baseUri, Process process, string logPath, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException($"llama-server exited with code {process.ExitCode}; see {logPath}.");
            }

            try
            {
                // /health answers 503 while the model is loading and 200 once it can serve.
                using var response = await Http.GetAsync(new Uri(baseUri, "health"), ct);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Request timeout while busy loading.
            }

            // Fine-grained polling keeps the measured load time close to the real one.
            await Task.Delay(50, ct);
        }

        throw new TimeoutException($"llama-server was not ready after {StartupTimeout.TotalSeconds:F0} s; see {logPath}.");
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
