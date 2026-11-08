using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Aldren.Windows.Apps;

/// <param name="AppId">The AppsFolder parsing name (an AppUserModelID or a known-folder path); launched as <c>shell:AppsFolder\{AppId}</c>.</param>
public sealed record AppEntry(string Name, string AppId);

public sealed record AppMatch(AppEntry App, double Score);

/// <summary>
/// The installed apps, as the Start menu's "All apps" list shows them (desktop and Store apps alike), read through the
/// Shell's AppsFolder. Refreshed at most every few minutes; matched by alias, then fuzzily by name.
/// </summary>
public sealed class AppIndex(ILogger<AppIndex> log, TimeProvider time)
{
    private const string AppsFolder = "shell:::{4234d49b-0245-4df3-b780-3893943456e1}";
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    /// <summary>What people say → words in the Start menu name.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["vscode"] = "visual studio code",
        ["vs code"] = "visual studio code",
        ["code"] = "visual studio code",
        ["chrome"] = "google chrome",
        ["edge"] = "microsoft edge",
        ["browser"] = "google chrome",
        ["terminal"] = "terminal",
        ["cmd"] = "command prompt",
        ["explorer"] = "file explorer",
        ["files"] = "file explorer",
        ["file manager"] = "file explorer",
        ["settings"] = "settings",
        ["calc"] = "calculator",
        ["obs"] = "obs studio",
        ["word"] = "word",
        ["excel"] = "excel",
        ["task manager"] = "task manager",
        ["notepad"] = "notepad",
        ["spotify"] = "spotify",
        ["discord"] = "discord",
        ["steam"] = "steam",
        ["epic"] = "epic games launcher",
        ["epic games"] = "epic games launcher",
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<AppEntry> _apps = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<AppEntry>> GetAppsAsync(CancellationToken ct)
    {
        if (time.GetUtcNow() - _loadedAt < MaxAge && _apps.Count > 0)
        {
            return _apps;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (time.GetUtcNow() - _loadedAt >= MaxAge || _apps.Count == 0)
            {
                _apps = await ReadAppsFolderAsync();
                _loadedAt = time.GetUtcNow();
                log.LogInformation("App index: {Count} apps", _apps.Count);
            }

            return _apps;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppMatch?> FindAsync(string query, CancellationToken ct)
    {
        var apps = await GetAppsAsync(ct);
        return Match(apps, query);
    }

    public static AppMatch? Match(IReadOnlyList<AppEntry> apps, string query)
    {
        var q = Normalize(query);
        if (q.Length == 0)
        {
            return null;
        }

        if (Aliases.TryGetValue(q, out var alias))
        {
            var aliased = Best(apps, alias);
            if (aliased is { Score: >= 0.9 })
            {
                return aliased;
            }
        }

        var best = Best(apps, q);
        return best is { Score: >= 0.6 } ? best : null;
    }

    private static AppMatch? Best(IReadOnlyList<AppEntry> apps, string q)
    {
        AppMatch? best = null;
        foreach (var app in apps)
        {
            var score = Score(Normalize(app.Name), q);
            // Prefer shorter names on ties ("Chrome" over "Chrome Remote Desktop").
            if (best is null || score > best.Score || (score == best.Score && app.Name.Length < best.App.Name.Length))
            {
                best = new AppMatch(app, score);
            }
        }

        return best;
    }

    public static double Score(string name, string q)
    {
        if (name == q)
        {
            return 1.0;
        }

        if (name.StartsWith(q + " ", StringComparison.Ordinal) || name.EndsWith(" " + q, StringComparison.Ordinal))
        {
            return 0.95;
        }

        var nameWords = name.Split(' ');
        var queryWords = q.Split(' ');
        if (queryWords.All(w => nameWords.Contains(w)))
        {
            return 0.9;
        }

        if (name.StartsWith(q, StringComparison.Ordinal))
        {
            return 0.85;
        }

        if (name.Contains(q, StringComparison.Ordinal) && q.Length >= 4)
        {
            return 0.75;
        }

        // Typos: compare with the closest word run of the same length.
        var best = Similarity(name, q);
        for (var i = 0; i + queryWords.Length <= nameWords.Length; i++)
        {
            best = Math.Max(best, Similarity(string.Join(' ', nameWords, i, queryWords.Length), q));
        }

        return best * 0.85;
    }

    private static double Similarity(string a, string b)
    {
        var max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1 : 1.0 - (double)Levenshtein(a, b) / max;
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>Shell COM objects want a single-threaded apartment.</summary>
    private static Task<IReadOnlyList<AppEntry>> ReadAppsFolderAsync()
    {
        var result = new TaskCompletionSource<IReadOnlyList<AppEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application")
                                ?? throw new InvalidOperationException("Shell.Application is unavailable.");
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic folder = shell.NameSpace(AppsFolder);
                var apps = new List<AppEntry>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in folder.Items())
                {
                    string name = item.Name;
                    string id = item.Path;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id) && seen.Add(id))
                    {
                        apps.Add(new AppEntry(name, id));
                    }
                }

                result.SetResult(apps);
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "ALDREN app index",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }
}
