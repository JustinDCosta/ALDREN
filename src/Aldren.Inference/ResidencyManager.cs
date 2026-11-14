using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aldren.Inference;

public enum ModelTier
{
    Gpu,
    Cpu,
}

public sealed record ResidencyStatus(bool GpuLoaded, bool CpuLoaded, bool GamingMode, bool GamingDetected, bool? GamingOverride, bool OnAc, long? FreeVramMiB);

/// <summary>Raised when the host can't load any model.</summary>
public sealed class ModelUnavailableException(string message) : Exception(message);

/// <summary>
/// Keeps at most one GPU model warm (ARCHITECTURE §7): loads on demand and preloads on AC power, unloads when idle, on
/// battery and within seconds of a fullscreen game or OBS. While the GPU is off limits, conversation falls back to the
/// small CPU model, stopped again after a short idle.
/// </summary>
public sealed class ResidencyManager(
    InferenceOptions options,
    IInferenceHost host,
    TimeProvider time,
    ILogger<ResidencyManager> log) : BackgroundService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LlamaServerProcess? _gpu;
    private LlamaServerProcess? _cpu;
    private DateTimeOffset _gpuUsed;
    private DateTimeOffset _cpuUsed;
    private volatile bool _gamingDetected;
    private volatile bool _preloadDone;

    /// <summary>Null follows detection; true or false is your choice ("gaming mode on/off").</summary>
    public bool? GamingOverride { get; private set; }

    public bool GamingMode => GamingOverride ?? _gamingDetected;

    public event Action<bool>? GamingModeChanged;

    /// <summary>The running model processes, for the memory figures in Settings.</summary>
    public int? GpuProcessId => _gpu is { HasExited: false } gpu ? gpu.ProcessId : null;

    public int? CpuProcessId => _cpu is { HasExited: false } cpu ? cpu.ProcessId : null;

    public ResidencyStatus Status => new(
        _gpu is { HasExited: false }, _cpu is { HasExited: false }, GamingMode, _gamingDetected, GamingOverride,
        PowerStatus.OnAcPower(), Nvml.FreeMiB());

    public async Task SetGamingOverrideAsync(bool? value, CancellationToken ct)
    {
        var before = GamingMode;
        GamingOverride = value;
        log.LogInformation("Gaming mode override: {Override}", value?.ToString() ?? "auto");
        if (GamingMode)
        {
            await UnloadGpuAsync("gaming mode", ct);
        }

        if (before != GamingMode)
        {
            GamingModeChanged?.Invoke(GamingMode);
        }
    }

    /// <summary>The model to use now: the GPU model when allowed, otherwise the CPU fallback.</summary>
    public async Task<(ChatClient Client, ModelTier Tier)> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // On battery the GPU still serves the request; the idle unload just comes sooner.
            if (!GamingMode)
            {
                var gpu = await EnsureGpuLockedAsync(ct);
                if (gpu is not null)
                {
                    _gpuUsed = time.GetUtcNow();
                    return (new ChatClient(gpu), ModelTier.Gpu);
                }
            }

            var cpu = await EnsureCpuLockedAsync(ct);
            _cpuUsed = time.GetUtcNow();
            return (new ChatClient(cpu), ModelTier.Cpu);
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = time.GetUtcNow();
        using var timer = new PeriodicTimer(options.PollInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(started, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Residency check failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task TickAsync(DateTimeOffset started, CancellationToken ct)
    {
        var wasGaming = GamingMode;
        _gamingDetected = GamingDetector.FullscreenAppRunning() || GamingDetector.ObsRunning();
        if (GamingMode != wasGaming)
        {
            log.LogInformation("Gaming mode {State}", GamingMode ? "on" : "off");
            GamingModeChanged?.Invoke(GamingMode);
        }

        var now = time.GetUtcNow();
        var onAc = PowerStatus.OnAcPower();
        if (_gpu is not null)
        {
            if (GamingMode)
            {
                await UnloadGpuAsync("a fullscreen game or OBS", ct);
            }
            else if (_gpu.HasExited)
            {
                log.LogWarning("llama-server (GPU) exited on its own; it reloads on the next request");
                await UnloadGpuAsync("it exited", ct);
            }
            else if (now - _gpuUsed > (onAc ? options.IdleUnloadOnAc : options.IdleUnloadOnBattery))
            {
                await UnloadGpuAsync(onAc ? "idle" : "idle on battery", ct);
            }
        }

        if (_cpu is not null && (now - _cpuUsed > options.CpuIdleUnload || _cpu.HasExited))
        {
            await UnloadCpuAsync(ct);
        }

        if (!_preloadDone && now - started >= options.PreloadDelay)
        {
            _preloadDone = true;
            if (onAc && !GamingMode && _gpu is null)
            {
                await _gate.WaitAsync(ct);
                try
                {
                    if (await EnsureGpuLockedAsync(ct) is not null)
                    {
                        _gpuUsed = time.GetUtcNow();
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
    }

    private async Task<LlamaServerProcess?> EnsureGpuLockedAsync(CancellationToken ct)
    {
        if (_gpu is { HasExited: false })
        {
            return _gpu;
        }

        var free = Nvml.FreeMiB();
        if (free is { } mib && mib < options.RequiredFreeVramMiB)
        {
            log.LogInformation("Not loading the GPU model: {Free} MiB of VRAM free, {Needed} needed", mib, options.RequiredFreeVramMiB);
            return null;
        }

        try
        {
            _gpu = await LlamaServerProcess.StartAsync(
                options, new LlamaLaunch(options.QuickModelPath, options.ProjectorPath, options.ContextSize, OnGpu: true),
                host.AddToJob, host.LogDirectory, log, ct);
            return _gpu;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "The GPU model failed to load");
            _gpu = null;
            return null;
        }
    }

    private async Task<LlamaServerProcess> EnsureCpuLockedAsync(CancellationToken ct)
    {
        if (_cpu is { HasExited: false })
        {
            return _cpu;
        }

        try
        {
            _cpu = await LlamaServerProcess.StartAsync(
                options, new LlamaLaunch(options.CpuModelPath, null, 4096, OnGpu: false), host.AddToJob, host.LogDirectory, log, ct);
            return _cpu;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "The CPU model failed to load");
            _cpu = null;
            throw new ModelUnavailableException("My local model isn't available right now.");
        }
    }

    private async Task UnloadGpuAsync(string reason, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_gpu is null)
            {
                return;
            }

            var clock = Stopwatch.StartNew();
            await _gpu.DisposeAsync();
            _gpu = null;
            log.LogInformation("GPU model unloaded ({Reason}) in {Ms} ms", reason, clock.ElapsedMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UnloadCpuAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_cpu is not null)
            {
                await _cpu.DisposeAsync();
                _cpu = null;
                log.LogInformation("CPU model unloaded");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_gpu is not null)
        {
            await _gpu.DisposeAsync();
        }

        if (_cpu is not null)
        {
            await _cpu.DisposeAsync();
        }
    }
}

/// <summary>What the host provides: its Job Object and log folder.</summary>
public interface IInferenceHost
{
    void AddToJob(Process process);

    string LogDirectory { get; }
}
