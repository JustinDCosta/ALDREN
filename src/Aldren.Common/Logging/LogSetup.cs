using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Aldren.Common.Logging;

public static class LogSetup
{
    /// <summary>
    /// Compact JSON in <c>&lt;logs&gt;\&lt;process&gt;-&lt;date&gt;.log</c>, one file a day, 14 kept, every string property redacted.
    /// Information level, Debug when <c>ALDREN_LOG_DEBUG=1</c>.
    /// </summary>
    public static LoggerConfiguration Configure(LoggerConfiguration config, AldrenPaths paths, string processName)
    {
        var level = Environment.GetEnvironmentVariable("ALDREN_LOG_DEBUG") == "1"
            ? LogEventLevel.Debug
            : LogEventLevel.Information;

        return config
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.With<RedactingEnricher>()
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(paths.LogsDirectory, processName + "-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14);
    }
}
