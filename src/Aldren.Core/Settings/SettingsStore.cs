using System.Text.Json;
using System.Text.Json.Nodes;
using Aldren.Common;
using Aldren.Contracts;
using Aldren.Contracts.Rpc;
using Microsoft.Extensions.Logging;

namespace Aldren.Core.Settings;

/// <summary>
/// <c>settings.json</c>, versioned and written atomically. An older file is migrated; a corrupt one is set aside;
/// one from a newer version is never overwritten, and ALDREN runs on defaults until you update.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new(AldrenRpc.SerializerOptions) { WriteIndented = true };

    private readonly string _path;
    private readonly SettingsMigrator _migrator;
    private readonly ILogger<SettingsStore> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _readOnly;

    public SettingsStore(AldrenPaths paths, SettingsMigrator migrator, ILogger<SettingsStore> log)
    {
        _path = paths.SettingsFile;
        _migrator = migrator;
        _log = log;
        Current = Load();
    }

    public AldrenSettings Current { get; private set; }

    /// <summary>Why the file couldn't be used, shown in Settings; null when it loaded.</summary>
    public string? LoadProblem { get; private set; }

    public event Action<AldrenSettings>? Changed;

    public async Task<SettingsResult> SaveAsync(AldrenSettings settings, CancellationToken ct)
    {
        if (_readOnly)
        {
            return new SettingsResult(false, ["settings.json comes from a newer version of ALDREN, so it is left untouched. Update ALDREN to change settings."]);
        }

        settings = settings with { SchemaVersion = _migrator.CurrentVersion };
        var errors = SettingsValidator.Validate(settings);
        if (errors.Count > 0)
        {
            return new SettingsResult(false, errors);
        }

        await _gate.WaitAsync(ct);
        try
        {
            await WriteAsync(JsonSerializer.Serialize(settings, WriteOptions), ct);
            Current = settings;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(settings);
        return new SettingsResult(true, []);
    }

    private AldrenSettings Load()
    {
        if (!File.Exists(_path))
        {
            return AldrenSettings.Default;
        }

        try
        {
            var document = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                ?? throw new JsonException("The root is not an object.");
            var version = document["schemaVersion"]?.GetValue<int>()
                ?? throw new JsonException("schemaVersion is missing.");

            if (version > _migrator.CurrentVersion)
            {
                _readOnly = true;
                LoadProblem = $"settings.json has schema version {version}, newer than this ALDREN ({_migrator.CurrentVersion}). Using defaults; the file is left untouched.";
                _log.LogWarning("{Problem}", LoadProblem);
                return AldrenSettings.Default;
            }

            var migrated = version < _migrator.CurrentVersion;
            if (migrated)
            {
                document = _migrator.Migrate(document, version);
            }

            var settings = document.Deserialize<AldrenSettings>(AldrenRpc.SerializerOptions)
                ?? throw new JsonException("The settings are empty.");
            settings = settings with { General = settings.General ?? new GeneralSettings() };

            if (SettingsValidator.Validate(settings) is { Count: > 0 } errors)
            {
                throw new JsonException(string.Join(" ", errors));
            }

            if (migrated)
            {
                File.Copy(_path, $"{_path}.v{version}.bak", overwrite: true);
                WriteAsync(JsonSerializer.Serialize(settings, WriteOptions), CancellationToken.None).GetAwaiter().GetResult();
                _log.LogInformation("Migrated settings.json from version {From} to {To}", version, _migrator.CurrentVersion);
            }

            return settings;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            var aside = $"{_path}.bad-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}";
            File.Move(_path, aside);
            LoadProblem = $"settings.json couldn't be read ({ex.Message}). It was kept as {Path.GetFileName(aside)} and defaults are in use.";
            _log.LogWarning("{Problem}", LoadProblem);
            return AldrenSettings.Default;
        }
    }

    private async Task WriteAsync(string json, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, json, ct);
        File.Move(temporary, _path, overwrite: true);
    }
}
