using System.Globalization;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;

namespace Aldren.Core.Tools;

public sealed record GetTimeArgs;

public sealed record TimeResult(string LocalTime, string TimeZone);

public sealed class GetTimeTool(TimeProvider time) : Tool<GetTimeArgs, TimeResult>
{
    public override string Name => "get_time";
    public override string Description => "Tell the current local time";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.SystemClock;

    protected override Task<TimeResult> ExecuteAsync(GetTimeArgs args, CancellationToken ct)
    {
        var now = time.GetLocalNow();
        var zone = time.LocalTimeZone;
        return Task.FromResult(new TimeResult(
            now.ToString("t", CultureInfo.CurrentCulture),
            zone.IsDaylightSavingTime(now) ? zone.DaylightName : zone.StandardName));
    }
}
