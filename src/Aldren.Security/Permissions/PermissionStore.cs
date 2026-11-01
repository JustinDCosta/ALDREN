using System.Text.Json;
using System.Text.Json.Nodes;
using Aldren.Contracts;

namespace Aldren.Security.Permissions;

public interface IPermissionStore
{
    bool IsGranted(string permission);

    IReadOnlyList<PermissionState> List();

    /// <summary>Why the file couldn't be used, shown in Settings; null when it loaded.</summary>
    string? LoadProblem { get; }

    /// <summary>Reached only through <c>IAldrenCore.SetPermissionAsync</c>, which the Settings window calls; no tool can.</summary>
    Task SetAsync(string permission, bool granted, CancellationToken ct);
}

/// <summary>
/// <c>permissions.json</c> = <c>{ "schemaVersion": 1, "granted": [...], "denied": [...] }</c>, written atomically.
/// Entries outside the catalog deny but are kept, so a downgrade doesn't lose them. A corrupt file is set aside;
/// a file from a newer version is never overwritten.
/// </summary>
public sealed class PermissionStore : IPermissionStore
{
    public const int SchemaVersion = 1;

    private readonly string _path;
    private readonly PermissionCatalog _catalog;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _stateLock = new();
    private HashSet<string> _granted = new(StringComparer.Ordinal);
    private HashSet<string> _denied = new(StringComparer.Ordinal);
    private bool _readOnly;

    public PermissionStore(string path, PermissionCatalog catalog)
    {
        _path = path;
        _catalog = catalog;
        Load();
    }

    public string? LoadProblem { get; private set; }

    public bool IsGranted(string permission)
    {
        if (!_catalog.TryGet(permission, out var definition))
        {
            return false;
        }

        lock (_stateLock)
        {
            if (_denied.Contains(permission)) return false;
            if (_granted.Contains(permission)) return true;
            return definition.GrantedByDefault;
        }
    }

    public IReadOnlyList<PermissionState> List() =>
        _catalog.Definitions
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => new PermissionState(d.Name, d.Description, IsGranted(d.Name)))
            .ToList();

    public async Task SetAsync(string permission, bool granted, CancellationToken ct)
    {
        if (!_catalog.Contains(permission))
        {
            throw new ArgumentException($"Unknown permission '{permission}'.", nameof(permission));
        }

        if (_readOnly)
        {
            throw new InvalidOperationException(
                "permissions.json comes from a newer version of ALDREN, so it is left untouched; permissions can't be changed until you update.");
        }

        await _gate.WaitAsync(ct);
        try
        {
            HashSet<string> nextGranted, nextDenied;
            lock (_stateLock)
            {
                nextGranted = new HashSet<string>(_granted, StringComparer.Ordinal);
                nextDenied = new HashSet<string>(_denied, StringComparer.Ordinal);
            }

            (granted ? nextGranted : nextDenied).Add(permission);
            (granted ? nextDenied : nextGranted).Remove(permission);
            await SaveAsync(nextGranted, nextDenied, ct);
            lock (_stateLock)
            {
                _granted = nextGranted;
                _denied = nextDenied;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                ?? throw new JsonException("The root is not an object.");
            var version = root["schemaVersion"]?.GetValue<int>()
                ?? throw new JsonException("schemaVersion is missing.");
            if (version > SchemaVersion)
            {
                _readOnly = true;
                LoadProblem = $"permissions.json has schema version {version}, newer than this ALDREN ({SchemaVersion}). Using defaults; the file is left untouched.";
                return;
            }

            _granted = ReadSet(root, "granted");
            _denied = ReadSet(root, "denied");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            var aside = $"{_path}.bad-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}";
            File.Move(_path, aside);
            _granted.Clear();
            _denied.Clear();
            LoadProblem = $"permissions.json couldn't be read ({ex.Message}). It was kept as {Path.GetFileName(aside)} and defaults are in use.";
        }
    }

    private static HashSet<string> ReadSet(JsonObject root, string property)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (root[property] is JsonArray array)
        {
            foreach (var item in array)
            {
                set.Add(item?.GetValue<string>() ?? throw new JsonException($"{property} holds a null."));
            }
        }
        else if (root[property] is not null)
        {
            throw new JsonException($"{property} is not an array.");
        }

        return set;
    }

    private async Task SaveAsync(HashSet<string> granted, HashSet<string> denied, CancellationToken ct)
    {
        var json = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["granted"] = new JsonArray(granted.Order(StringComparer.Ordinal).Select(n => (JsonNode?)n).ToArray()),
            ["denied"] = new JsonArray(denied.Order(StringComparer.Ordinal).Select(n => (JsonNode?)n).ToArray()),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
        File.Move(temporary, _path, overwrite: true);
    }
}
