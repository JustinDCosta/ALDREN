using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Aldren.Inference;

public sealed record LlamaLaunch(string ModelPath, string? ProjectorPath, int ContextSize, bool OnGpu);

/// <summary>One llama-server: 127.0.0.1, random port and API key, thinking off. Stopped on dispose.</summary>
public sealed class LlamaServerProcess : IAsyncDisposable
{
    private static readonly HttpClient Health = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly Process _process;

    private LlamaServerProcess(Process process, Uri baseUri, string apiKey, LlamaLaunch launch)
    {
        _process = process;
        BaseUri = baseUri;
        ApiKey = apiKey;
        Launch = launch;
    }

    public Uri BaseUri { get; }
    public string ApiKey { get; }
    public LlamaLaunch Launch { get; }
    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;

    /// <param name="onStarted">Puts the process in Core's Job Object, so it never outlives Core.</param>
    public static async Task<LlamaServerProcess> StartAsync(
        InferenceOptions options, LlamaLaunch launch, Action<Process> onStarted, string logDirectory, ILogger log, CancellationToken ct)
    {
        if (!File.Exists(options.LlamaServerPath))
        {
            throw new FileNotFoundException("llama-server isn't installed where ALDREN expects it.", options.LlamaServerPath);
        }

        if (!File.Exists(launch.ModelPath))
        {
            throw new FileNotFoundException("The model file is missing.", launch.ModelPath);
        }

        var port = FreePort();
        var apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var psi = new ProcessStartInfo(options.LlamaServerPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(options.LlamaServerPath)!,
        };
        foreach (var arg in Arguments(launch, port, apiKey))
        {
            psi.ArgumentList.Add(arg);
        }

        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"llama-server-{DateTime.Now:yyyyMMdd-HHmmss}-{port}.log");
        var writer = TextWriter.Synchronized(new StreamWriter(logPath) { AutoFlush = true });

        var process = Process.Start(psi) ?? throw new InvalidOperationException("llama-server did not start.");
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => writer.Dispose();
        // The API key is on the command line, never in the log: llama-server doesn't print it.
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { try { writer.WriteLine(e.Data); } catch (ObjectDisposedException) { } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { try { writer.WriteLine(e.Data); } catch (ObjectDisposedException) { } } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        onStarted(process);

        var server = new LlamaServerProcess(process, new Uri($"http://127.0.0.1:{port}/"), apiKey, launch);
        var clock = Stopwatch.StartNew();
        try
        {
            await server.WaitUntilHealthyAsync(options.StartupTimeout, ct);
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }

        log.LogInformation("llama-server ready in {Seconds:F1} s ({Model}, {Where}, process {ProcessId}, log {Log})",
            clock.Elapsed.TotalSeconds, Path.GetFileName(launch.ModelPath), launch.OnGpu ? "GPU" : "CPU", process.Id, logPath);
        return server;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        if (_process.HasExited)
        {
            return false;
        }

        try
        {
            using var response = await Health.GetAsync(new Uri(BaseUri, "health"), ct);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task WaitUntilHealthyAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        while (true)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"llama-server exited during start-up (code {_process.ExitCode}).");
            }

            if (await IsHealthyAsync(deadline.Token))
            {
                return;
            }

            try
            {
                await Task.Delay(100, deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"llama-server wasn't ready after {timeout.TotalSeconds:F0} s.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            // Already gone, or the Job Object reaps it with Core.
        }

        _process.Dispose();
    }

    private static IEnumerable<string> Arguments(LlamaLaunch launch, int port, string apiKey)
    {
        yield return "-m"; yield return launch.ModelPath;
        yield return "--host"; yield return "127.0.0.1";
        yield return "--port"; yield return port.ToString(CultureInfo.InvariantCulture);
        yield return "--api-key"; yield return apiKey;
        yield return "-c"; yield return launch.ContextSize.ToString(CultureInfo.InvariantCulture);
        yield return "-np"; yield return "1";
        yield return "--jinja";
        yield return "--reasoning-budget"; yield return "0";
        yield return "--cache-type-k"; yield return "q8_0";
        yield return "--cache-type-v"; yield return "q8_0";
        yield return "-fa"; yield return "on";
        yield return "-ngl"; yield return launch.OnGpu ? "999" : "0";
        if (launch.ProjectorPath is not null)
        {
            yield return "--mmproj"; yield return launch.ProjectorPath;
        }

        if (!launch.OnGpu)
        {
            yield return "-t"; yield return "6";
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
