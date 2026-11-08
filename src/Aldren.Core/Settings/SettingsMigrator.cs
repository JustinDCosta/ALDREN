using System.Text.Json.Nodes;
using Aldren.Contracts;

namespace Aldren.Core.Settings;

/// <summary>Step n upgrades a settings document from version n to n + 1.</summary>
public sealed class SettingsMigrator(IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> steps)
{
    public static SettingsMigrator Default { get; } = new(new Dictionary<int, Func<JsonObject, JsonObject>>
    {
        // 1 → 2: the Voice section (Phase 3), with its defaults.
        [1] = document =>
        {
            document["voice"] ??= new JsonObject();
            return document;
        },
        // 3 → 4: the Cloud section (Phase 6), with its defaults.
        [3] = document =>
        {
            document["cloud"] ??= new JsonObject();
            return document;
        },
        // 2 → 3: the Home & away section (Phase 4), with its defaults.
        [2] = document =>
        {
            document["presence"] ??= new JsonObject();
            return document;
        },
    });

    public int CurrentVersion => AldrenSettings.CurrentSchemaVersion;

    /// <summary>Brings an older document up to <see cref="CurrentVersion"/>.</summary>
    public JsonObject Migrate(JsonObject document, int fromVersion)
    {
        for (var version = fromVersion; version < CurrentVersion; version++)
        {
            if (!steps.TryGetValue(version, out var step))
            {
                throw new InvalidOperationException($"There is no settings migration from version {version}.");
            }

            document = step(document);
        }

        document["schemaVersion"] = CurrentVersion;
        return document;
    }
}
