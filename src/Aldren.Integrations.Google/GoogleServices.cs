using Google.Apis.Calendar.v3.Data;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Aldren.Integrations.Google;

public sealed record CalendarItem(string Id, string Title, DateTimeOffset? Start, DateTimeOffset? End, bool AllDay);

public sealed record DriveItem(string Id, string Name, string MimeType, DateTimeOffset? Modified, long? Size);

/// <summary>Calendar (ARCHITECTURE §14.3): agenda, create, delete. Confirmation and read-back happen in the tools.</summary>
public sealed class GoogleCalendar(GoogleAccount account)
{
    public async Task<IReadOnlyList<CalendarItem>> AgendaAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var service = await account.CalendarAsync(ct);
        var request = service.Events.List("primary");
        request.TimeMinDateTimeOffset = from;
        request.TimeMaxDateTimeOffset = to;
        request.SingleEvents = true;
        request.OrderBy = global::Google.Apis.Calendar.v3.EventsResource.ListRequest.OrderByEnum.StartTime;
        request.MaxResults = 50;
        var events = await request.ExecuteAsync(ct);
        return (events.Items ?? []).Select(ToItem).ToList();
    }

    public async Task<CalendarItem> CreateAsync(string title, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var service = await account.CalendarAsync(ct);
        var created = await service.Events.Insert(new Event
        {
            Summary = title,
            Start = new EventDateTime { DateTimeDateTimeOffset = start },
            End = new EventDateTime { DateTimeDateTimeOffset = end },
        }, "primary").ExecuteAsync(ct);
        return ToItem(created);
    }

    public async Task DeleteAsync(string eventId, CancellationToken ct)
    {
        using var service = await account.CalendarAsync(ct);
        await service.Events.Delete("primary", eventId).ExecuteAsync(ct);
    }

    private static CalendarItem ToItem(Event e) => new(
        e.Id, e.Summary ?? "(no title)",
        e.Start?.DateTimeDateTimeOffset ?? (e.Start?.Date is { } d ? DateTimeOffset.Parse(d) : null),
        e.End?.DateTimeDateTimeOffset, e.Start?.Date is not null);
}

/// <summary>Drive (ARCHITECTURE §14.3): search, download or export, upload, rename and move. Summaries stay local.</summary>
public sealed class GoogleDrive(GoogleAccount account)
{
    private static readonly Dictionary<string, (string Mime, string Extension)> Exports = new()
    {
        ["application/vnd.google-apps.document"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx"),
        ["application/vnd.google-apps.spreadsheet"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx"),
        ["application/vnd.google-apps.presentation"] = ("application/pdf", ".pdf"),
        ["application/vnd.google-apps.drawing"] = ("application/pdf", ".pdf"),
    };

    public async Task<IReadOnlyList<DriveItem>> SearchAsync(string words, CancellationToken ct)
    {
        using var service = await account.DriveAsync(ct);
        var request = service.Files.List();
        // Drive query values are single-quoted; quotes and backslashes in your words are escaped.
        var safe = words.Replace("\\", "\\\\").Replace("'", "\\'");
        request.Q = $"name contains '{safe}' and trashed = false";
        request.Fields = "files(id, name, mimeType, modifiedTime, size)";
        request.PageSize = 15;
        request.OrderBy = "modifiedTime desc";
        var result = await request.ExecuteAsync(ct);
        return (result.Files ?? []).Select(ToItem).ToList();
    }

    /// <summary>Downloads into a folder, exporting Google Docs, Sheets and Slides to Office or PDF files.</summary>
    public async Task<string> DownloadAsync(string fileId, string folder, CancellationToken ct)
    {
        using var service = await account.DriveAsync(ct);
        var meta = await service.Files.Get(fileId).ExecuteAsync(ct);
        var (mime, extension) = Exports.TryGetValue(meta.MimeType ?? "", out var export) ? export : (null!, "");
        var name = string.Concat((meta.Name + extension).Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(folder, name);
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
        }

        await using var stream = File.Create(path);
        if (mime is not null)
        {
            await service.Files.Export(fileId, mime).DownloadAsync(stream, ct);
        }
        else
        {
            await service.Files.Get(fileId).DownloadAsync(stream, ct);
        }

        return path;
    }

    public async Task<DriveItem> UploadAsync(string path, CancellationToken ct)
    {
        using var service = await account.DriveAsync(ct);
        await using var stream = File.OpenRead(path);
        var upload = service.Files.Create(new DriveFile { Name = Path.GetFileName(path) }, stream, "application/octet-stream");
        upload.Fields = "id, name, mimeType, modifiedTime, size";
        var progress = await upload.UploadAsync(ct);
        if (progress.Exception is not null)
        {
            throw new InvalidOperationException("The upload failed: " + progress.Exception.Message);
        }

        return ToItem(upload.ResponseBody);
    }

    public async Task<DriveItem> RenameAsync(string fileId, string name, CancellationToken ct)
    {
        using var service = await account.DriveAsync(ct);
        var request = service.Files.Update(new DriveFile { Name = name }, fileId);
        request.Fields = "id, name, mimeType, modifiedTime, size";
        return ToItem(await request.ExecuteAsync(ct));
    }

    private static DriveItem ToItem(DriveFile f) => new(f.Id, f.Name, f.MimeType ?? "", f.ModifiedTimeDateTimeOffset, f.Size);
}
