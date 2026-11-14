using System.Globalization;
using Aldren.Assistant;
using Aldren.Integrations.Google;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Aldren.Windows.Files;

namespace Aldren.Core.Tools;

public sealed record DayArgs(string Day = "today");

public sealed record CalendarEventArgs(string Title, string When, int Minutes = 60);

public sealed class CalendarAgendaTool(GoogleCalendar calendar, AssistantStore store) : Tool<DayArgs, DetailedResult>
{
    public override string Name => "calendar_agenda";
    public override string Description => "Read the user's Google Calendar for a day (today, tomorrow, a weekday or a date)";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.CalendarRead;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);

    protected override async Task<DetailedResult> ExecuteAsync(DayArgs args, CancellationToken ct)
    {
        var day = ResolveDay(args.Day, store.NowLocal);
        var from = DateParser.ToInstant(day, store.Zone);
        var events = await calendar.AgendaAsync(from, DateParser.ToInstant(day.AddDays(1), store.Zone), ct);
        var label = DateParser.Describe(day.AddHours(12), store.NowLocal).Replace(" at 12:00 PM", "").Replace(" at 12:00", "");
        if (events.Count == 0)
        {
            return new DetailedResult($"Nothing on your calendar {label}.", []);
        }

        var items = events.Select(e => e.AllDay || e.Start is null
            ? $"{e.Title} (all day)"
            : $"{e.Title} at {TimeZoneInfo.ConvertTime(e.Start.Value, store.Zone).ToString("t", CultureInfo.CurrentCulture)}").ToList();
        return new DetailedResult($"{items.Count} event{(items.Count == 1 ? "" : "s")} {label}: {string.Join("; ", items.Take(8))}.", items);
    }

    internal static DateTime ResolveDay(string text, DateTime now)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t is "" or "today")
        {
            return now.Date;
        }

        if (t == "tomorrow")
        {
            return now.Date.AddDays(1);
        }

        return DateParser.Parse(t + " at 12:00", now) is AtLocal at ? at.Local.Date : now.Date;
    }
}

public sealed class CalendarCreateTool(GoogleCalendar calendar, AssistantStore store) : Tool<CalendarEventArgs, SpokenResult>
{
    public override string Name => "calendar_create";
    public override string Description => "Add an event to the user's Google Calendar. when: as the user said it (\"tomorrow at 3 pm\"); minutes: length (default 60)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.CalendarWrite;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public override IEnumerable<string> Validate(CalendarEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Title) || args.Title.Length > 200)
        {
            yield return "title must be 1–200 characters.";
        }

        if (args.Minutes is < 5 or > 1440)
        {
            yield return "minutes must be 5–1440.";
        }
    }

    protected override async Task<SpokenResult> ExecuteAsync(CalendarEventArgs args, CancellationToken ct)
    {
        if (DateParser.Parse(args.When, store.NowLocal) is not AtLocal at)
        {
            throw new InvalidOperationException(DateParser.Parse(args.When, store.NowLocal) is Unclear u ? u.Question : "When should the event be?");
        }

        var start = DateParser.ToInstant(at.Local, store.Zone);
        var created = await calendar.CreateAsync(args.Title.Trim(), start, start.AddMinutes(args.Minutes), ct);
        return new SpokenResult($"Added \"{created.Title}\" to your calendar {DateParser.Describe(at.Local, store.NowLocal)}, for {args.Minutes} minutes.");
    }
}

public sealed record CalendarFindArgs(string Query, string Day = "today");

public sealed class CalendarDeleteTool(GoogleCalendar calendar, AssistantStore store) : Tool<CalendarFindArgs, SpokenResult>
{
    public override string Name => "calendar_delete";
    public override string Description => "Delete the calendar event on a day whose title best matches the query (asks the user first)";
    public override RiskTier Tier => RiskTier.Destructive;
    public override string Permission => PermissionCatalog.CalendarWrite;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public override string Summarize(CalendarFindArgs args) => $"Delete the calendar event matching \"{args.Query}\" on {args.Day}";

    protected override async Task<SpokenResult> ExecuteAsync(CalendarFindArgs args, CancellationToken ct)
    {
        var day = CalendarAgendaTool.ResolveDay(args.Day, store.NowLocal);
        var events = await calendar.AgendaAsync(DateParser.ToInstant(day, store.Zone), DateParser.ToInstant(day.AddDays(1), store.Zone), ct);
        var match = AssistantStore.BestMatch(events, e => e.Title, args.Query) ?? throw new InvalidOperationException("I couldn't find that event.");
        await calendar.DeleteAsync(match.Id, ct);
        return new SpokenResult($"Deleted \"{match.Title}\" from your calendar.");
    }
}

public sealed class DriveSearchTool(GoogleDrive drive) : Tool<QueryArgs, DetailedResult>
{
    public override string Name => "drive_search";
    public override string Description => "Search Google Drive by words in file names; returns file ids for drive_download and drive_rename";
    public override RiskTier Tier => RiskTier.Read;
    public override string Permission => PermissionCatalog.DriveRead;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);

    protected override async Task<DetailedResult> ExecuteAsync(QueryArgs args, CancellationToken ct)
    {
        var files = await drive.SearchAsync(args.Query, ct);
        return files.Count == 0
            ? new DetailedResult($"Nothing in your Drive matches \"{args.Query}\".", [])
            : new DetailedResult($"{files.Count} files: {string.Join("; ", files.Take(5).Select((f, i) => $"{i + 1}. {f.Name}"))}.",
                files.Select(f => $"{f.Id} | {f.Name} | {f.MimeType}").ToList());
    }
}

public sealed record DriveIdArgs(string FileId);

public sealed class DriveDownloadTool(GoogleDrive drive) : Tool<DriveIdArgs, SpokenResult>
{
    public override string Name => "drive_download";
    public override string Description => "Download a Drive file by id into Downloads (Google Docs, Sheets and Slides are exported)";
    public override RiskTier Tier => RiskTier.Reversible;
    public override string Permission => PermissionCatalog.DriveRead;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(120);

    protected override async Task<SpokenResult> ExecuteAsync(DriveIdArgs args, CancellationToken ct)
    {
        var path = await drive.DownloadAsync(args.FileId.Trim(), FileFinder.Downloads, ct);
        return new SpokenResult($"Downloaded {Path.GetFileName(path)} to your Downloads folder.");
    }
}

public sealed class DriveUploadTool(GoogleDrive drive) : Tool<PathArgs, SpokenResult>
{
    public override string Name => "drive_upload";
    public override string Description => "Upload a local file (full path) to Google Drive (asks the user first: the file leaves this PC)";
    public override RiskTier Tier => RiskTier.Impactful;
    public override string Permission => PermissionCatalog.DriveWrite;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(120);

    public override string Summarize(PathArgs args) =>
        $"Upload to Google Drive: {args.Path}{(File.Exists(args.Path) ? $" ({new FileInfo(args.Path).Length / 1024.0:N0} KB)" : "")}";

    protected override async Task<SpokenResult> ExecuteAsync(PathArgs args, CancellationToken ct)
    {
        if (!File.Exists(args.Path) || FileOperations.Refusal(args.Path) is not null && !args.Path.StartsWith(FileFinder.Downloads, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("That file doesn't exist or is in a folder ALDREN doesn't touch.");
        }

        var item = await drive.UploadAsync(args.Path, ct);
        return new SpokenResult($"Uploaded {item.Name} to your Drive.");
    }
}

public sealed record DriveRenameArgs(string FileId, string NewName);

public sealed class DriveRenameTool(GoogleDrive drive) : Tool<DriveRenameArgs, SpokenResult>
{
    public override string Name => "drive_rename";
    public override string Description => "Rename a Drive file by id (asks the user first; works on files ALDREN uploaded or opened)";
    public override RiskTier Tier => RiskTier.Impactful;
    public override string Permission => PermissionCatalog.DriveWrite;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public override string Summarize(DriveRenameArgs args) => $"Rename the Drive file {args.FileId} to \"{args.NewName}\"";

    protected override async Task<SpokenResult> ExecuteAsync(DriveRenameArgs args, CancellationToken ct)
    {
        var item = await drive.RenameAsync(args.FileId.Trim(), args.NewName.Trim(), ct);
        return new SpokenResult($"Renamed to {item.Name}.");
    }
}
