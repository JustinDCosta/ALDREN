using Aldren.Security.Audit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aldren.Core.Maintenance;

/// <summary>Removes audit entries older than 180 days: once at start, then once a day.</summary>
public sealed class AuditRetentionService(IAuditLog audit, TimeProvider time, ILogger<AuditRetentionService> log) : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(180);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), time);
        do
        {
            await PruneOnceAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    public async Task PruneOnceAsync(CancellationToken ct)
    {
        try
        {
            var removed = await audit.PruneAsync(time.GetUtcNow() - Retention, ct);
            if (removed > 0)
            {
                log.LogInformation("Removed {Count} audit entries older than {Days} days", removed, Retention.TotalDays);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Audit retention failed; trying again tomorrow");
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
