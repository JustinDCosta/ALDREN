using Microsoft.Data.Sqlite;

namespace Aldren.Cloud;

public enum CloudRole
{
    Fast,
    Reason,
    Vision,
    Research,
}

public enum ChainState
{
    Ok,
    Missing,
    Empty,
    Disallowed,
}

public sealed record ChainCheck(CloudRole Role, string Name, ChainState State, IReadOnlyList<string> Models, string? Problem);

public sealed record DriftReport(IReadOnlyList<ChainCheck> Chains, IReadOnlyList<string> KeysOutsideAllowlist, DateTimeOffset CheckedAt)
{
    /// <summary>ADR-0012: any enabled key outside the allowlist keeps all cloud use off until it's fixed.</summary>
    public bool CloudAllowed => KeysOutsideAllowlist.Count == 0;

    public ChainCheck For(CloudRole role) => Chains.First(c => c.Role == role);

    public bool Usable(CloudRole role) => CloudAllowed && For(role).State == ChainState.Ok;
}

/// <summary>
/// FreeLLMAPI's own database (0.16.14 schema), read by ALDREN for the drift check and its unified API key. The only write
/// creates the four <c>aldren-*</c> chains empty when they're missing (ARCHITECTURE §8.2); their contents are yours to
/// choose in the gateway dashboard.
/// </summary>
public sealed class GatewayDatabase(string path)
{
    /// <summary>First-party providers ALDREN may use (ARCHITECTURE §8.2). Keyless, community and reseller relays never.</summary>
    public static readonly IReadOnlySet<string> Allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "google", "groq", "cerebras", "mistral", "openrouter", "github",
    };

    /// <summary>Which providers each chain may contain.</summary>
    public static readonly IReadOnlyDictionary<CloudRole, IReadOnlySet<string>> ChainProviders = new Dictionary<CloudRole, IReadOnlySet<string>>
    {
        [CloudRole.Fast] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "groq", "cerebras" },
        [CloudRole.Reason] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "google", "groq", "cerebras", "mistral" },
        [CloudRole.Vision] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "google" },
        [CloudRole.Research] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "google", "openrouter", "groq" },
    };

    public static string ChainName(CloudRole role) => "aldren-" + role.ToString().ToLowerInvariant();

    public bool Exists => File.Exists(path);

    public string? UnifiedKey()
    {
        using var db = Open(SqliteOpenMode.ReadOnly);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = 'unified_api_key';";
        return command.ExecuteScalar() as string;
    }

    /// <summary>Creates any missing <c>aldren-*</c> chain, empty. Returns how many were created.</summary>
    public int EnsureChains()
    {
        using var db = Open(SqliteOpenMode.ReadWrite);
        var created = 0;
        foreach (var role in Enum.GetValues<CloudRole>())
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                INSERT INTO profiles (name, type)
                SELECT $name, 'custom' WHERE NOT EXISTS (SELECT 1 FROM profiles WHERE lower(name) = $name);
                """;
            command.Parameters.AddWithValue("$name", ChainName(role));
            created += command.ExecuteNonQuery();
        }

        return created;
    }

    /// <summary>The read-only drift check (ADR-0012): every chain's contents and every enabled key against the allowlists.</summary>
    public DriftReport Check()
    {
        using var db = Open(SqliteOpenMode.ReadOnly);
        var chains = new List<ChainCheck>();
        foreach (var role in Enum.GetValues<CloudRole>())
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                SELECT p.id, m.platform, m.model_id
                FROM profiles p
                LEFT JOIN profile_models pm ON pm.profile_id = p.id AND pm.enabled = 1
                LEFT JOIN models m ON m.id = pm.model_db_id
                WHERE lower(p.name) = $name
                ORDER BY pm.priority;
                """;
            command.Parameters.AddWithValue("$name", ChainName(role));
            using var reader = command.ExecuteReader();
            var found = false;
            var models = new List<string>();
            var outside = new List<string>();
            while (reader.Read())
            {
                found = true;
                if (reader.IsDBNull(1))
                {
                    continue;
                }

                var platform = reader.GetString(1);
                models.Add($"{platform}/{reader.GetString(2)}");
                if (!ChainProviders[role].Contains(platform) || !Allowlist.Contains(platform))
                {
                    outside.Add(platform);
                }
            }

            chains.Add(!found
                ? new ChainCheck(role, ChainName(role), ChainState.Missing, [], "the chain doesn't exist")
                : models.Count == 0
                    ? new ChainCheck(role, ChainName(role), ChainState.Empty, [], "no models chosen yet (gateway dashboard > Fallback chains)")
                    : outside.Count > 0
                        ? new ChainCheck(role, ChainName(role), ChainState.Disallowed, models, $"providers not allowed here: {string.Join(", ", outside.Distinct())}")
                        : new ChainCheck(role, ChainName(role), ChainState.Ok, models, null));
        }

        using var keys = db.CreateCommand();
        keys.CommandText = "SELECT DISTINCT platform FROM api_keys WHERE enabled = 1;";
        using var keyReader = keys.ExecuteReader();
        var blocked = new List<string>();
        while (keyReader.Read())
        {
            if (!Allowlist.Contains(keyReader.GetString(0)))
            {
                blocked.Add(keyReader.GetString(0));
            }
        }

        return new DriftReport(chains, blocked, DateTimeOffset.UtcNow);
    }

    private SqliteConnection Open(SqliteOpenMode mode)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, DefaultTimeout = 5 }.ToString());
        db.Open();
        return db;
    }
}
