using System.Diagnostics;
using System.Text.Json;
using Aldren.Windows.Apps;

namespace Aldren.Windows.Layouts;

/// <param name="App">The app as you'd say it ("Visual Studio Code"), used to find its window and to launch it.</param>
/// <param name="Monitor">1 = primary, then left to right.</param>
/// <param name="X">Position and size as fractions of the monitor's work area.</param>
public sealed record LayoutWindow(string App, int Monitor, double X, double Y, double Width, double Height, bool Maximized);

public sealed record Layout(string Name, string Configuration, IReadOnlyList<LayoutWindow> Windows);

public sealed record LayoutResult(int Placed, int Launched, IReadOnlyList<string> Missing);

/// <summary>
/// Named window layouts, remembered per monitor configuration (ARCHITECTURE §11): saving records where each app's window
/// is; applying moves them back, launching apps that aren't open. Stored as JSON in ALDREN's data folder.
/// </summary>
public sealed class LayoutStore(string path, AppIndex apps)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Built in until you save your own: VS Code 60% left, Chrome 40% right, Terminal bottom right (ARCHITECTURE §11).</summary>
    public static Layout Coding => new("coding", "*",
    [
        new LayoutWindow("Visual Studio Code", 1, 0, 0, 0.6, 1, false),
        new LayoutWindow("Google Chrome", 1, 0.6, 0, 0.4, 0.6, false),
        new LayoutWindow("Terminal", 1, 0.6, 0.6, 0.4, 0.4, false),
    ]);

    public async Task<IReadOnlyList<Layout>> ListAsync(CancellationToken ct)
    {
        var all = await LoadAsync(ct);
        return all.Any(l => l.Name == "coding") ? all : [.. all, Coding];
    }

    public async Task<Layout> SaveCurrentAsync(string name, CancellationToken ct)
    {
        var key = WindowManager.ConfigurationKey();
        var monitors = WindowManager.Monitors();
        var windows = new List<LayoutWindow>();
        foreach (var (window, bounds, monitorIndex, maximized, minimized) in WindowManager.Describe())
        {
            if (minimized || window.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && window.Title.Length == 0)
            {
                continue;
            }

            var area = monitors[monitorIndex - 1].WorkArea;
            windows.Add(new LayoutWindow(
                WindowFinder.Describe(window), monitorIndex,
                Math.Round((double)(bounds.Left - area.Left) / area.Width, 3), Math.Round((double)(bounds.Top - area.Top) / area.Height, 3),
                Math.Round((double)bounds.Width / area.Width, 3), Math.Round((double)bounds.Height / area.Height, 3), maximized));
        }

        var layout = new Layout(Normalize(name), key, windows.DistinctBy(w => w.App).ToList());
        await _gate.WaitAsync(ct);
        try
        {
            var all = (await LoadAsync(ct)).Where(l => !(l.Name == layout.Name && l.Configuration == key)).ToList();
            all.Add(layout);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(all, Json), ct);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }

        return layout;
    }

    /// <summary>The saved layout for this monitor configuration, else one saved for any configuration, else the built-in.</summary>
    public async Task<Layout?> FindAsync(string name, CancellationToken ct)
    {
        var key = WindowManager.ConfigurationKey();
        var n = Normalize(name);
        var all = await ListAsync(ct);
        return all.FirstOrDefault(l => l.Name == n && l.Configuration == key) ?? all.FirstOrDefault(l => l.Name == n);
    }

    public async Task<LayoutResult> ApplyAsync(Layout layout, CancellationToken ct)
    {
        var monitors = WindowManager.Monitors();
        int placed = 0, launched = 0;
        var missing = new List<string>();
        foreach (var w in layout.Windows)
        {
            var window = WindowFinder.Find(w.App).FirstOrDefault();
            if (window is null && await apps.FindAsync(w.App, ct) is { } app)
            {
                using (Process.Start(new ProcessStartInfo($@"shell:AppsFolder\{app.App.AppId}") { UseShellExecute = true }))
                {
                }

                launched++;
                for (var i = 0; i < 40 && window is null; i++)
                {
                    await Task.Delay(250, ct);
                    window = WindowFinder.Find(w.App).FirstOrDefault();
                }
            }

            if (window is null)
            {
                missing.Add(w.App);
                continue;
            }

            var monitor = monitors[Math.Clamp(w.Monitor, 1, monitors.Count) - 1];
            if (w.Maximized ? WindowManager.Place(window.Handle, Placement.Maximize, monitor) : WindowManager.PlaceFraction(window.Handle, monitor, w.X, w.Y, w.Width, w.Height))
            {
                placed++;
            }
        }

        return new LayoutResult(placed, launched, missing);
    }

    private async Task<List<Layout>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Layout>>(await File.ReadAllTextAsync(path, ct), Json) ?? [];
        }
        catch (JsonException)
        {
            // A damaged file: keep it for inspection, start empty.
            File.Move(path, path + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            return [];
        }
    }

    private static string Normalize(string name) => AppIndex.Normalize(name).Replace(" layout", "").Trim();
}
