using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using S2.Island.Native;

namespace S2.Island.Diagnostics;

/// <summary>
/// Runs the automatable parts of the S2 checklist against a stand-in app window and appends the results to
/// E:\AldrenData\bench\s2\results.md. Input is only ever sent to that window or to the island itself.
/// </summary>
internal sealed class Checklist(IslandWindow island)
{
    private const string ResultsFolder = @"E:\AldrenData\bench\s2";
    private const string Sentence = "The quick brown fox jumps over the lazy dog, 0123456789.";
    private static readonly (int R, int G, int B) TargetColour = (32, 96, 160);
    private static readonly (int R, int G, int B) PillColour = (15, 15, 15);
    private static readonly IslandState[] Cycle = [IslandState.Compact, IslandState.Listening, IslandState.Expanded, IslandState.Hidden];

    private readonly StringBuilder _report = new();
    private nint _target;
    private Process? _targetProcess;

    public async Task RunAndExitAsync()
    {
        try
        {
            await RunAsync();
        }
        catch (Exception ex)
        {
            Line($"Run aborted: {ex}");
        }
        finally
        {
            CloseTarget();
            Directory.CreateDirectory(ResultsFolder);
            await File.AppendAllTextAsync(Path.Combine(ResultsFolder, "results.md"), _report + Environment.NewLine);
            await File.WriteAllTextAsync(Path.Combine(ResultsFolder, "last-run.md"), _report.ToString());
            Application.Current.Exit();
        }
    }

    private async Task RunAsync()
    {
        Line($"## Automated run {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line($"- Click-through: {island.Mode}");
        Line($"- Hotkey: {island.HotkeyChord ?? "none (Ctrl+Alt+Space and Ctrl+Alt+Shift+Space are both taken)"}");
        Line($"- WinUI child windows: {island.ChildWindows}");
        foreach (var m in Monitors.All())
        {
            Line($"- Monitor {m.Device}: {m.Bounds}, {m.Dpi} dpi ({m.Scale:P0}), {m.RefreshHz} Hz{(m.IsPrimary ? ", primary" : "")}");
        }

        island.PlaceOnMonitor(0);
        await MorphAsync(IslandState.Compact);
        Line($"- Island window after showing: {WindowStyles.Describe(island.Handle)}");
        await StartTargetAsync();

        await CheckAsync("1 + 5. Transparency and capture exclusion", TransparencyAsync);
        await CheckAsync("2. Focus while typing", FocusAsync);
        await CheckAsync("3. Click-through", ClickThroughAsync);
        await CheckAsync("8. Hotkey to first frame", HotkeyLatencyAsync);
        await CheckAsync("4. Above borderless fullscreen", TopmostAsync);
        CloseTarget();
        await CheckAsync("6 + 7. Placement and smoothness per monitor", SmoothnessAsync);
        await CheckAsync("8. Idle cost", IdleAsync);
        await MorphAsync(IslandState.Hidden);
    }

    private async Task CheckAsync(string name, Func<Task<string>> run)
    {
        Line($"### {name}");
        try
        {
            Line(await run());
        }
        catch (Exception ex)
        {
            Line($"ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task StartTargetAsync()
    {
        var title = $"ALDREN S2 target {Environment.ProcessId}";
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "test-target.ps1"), "-Title", title })
        {
            psi.ArgumentList.Add(arg);
        }

        _targetProcess = Process.Start(psi);
        for (var i = 0; i < 150 && _target == 0; i++)
        {
            await Task.Delay(100);
            _target = Desktop.FindTopLevel(title);
        }

        if (_target == 0)
        {
            throw new InvalidOperationException("The test target window did not appear.");
        }

        // The target's solid-colour area covers the whole island window; its text box sits below it.
        var w = WindowStyles.WindowRect(island.Handle);
        WindowStyles.MoveTo(_target, new RECT(w.Left - 150, w.Top - 10, w.Right + 150, w.Bottom + 240));
        await Task.Delay(400);
        await FocusTargetAsync();
    }

    /// <summary>Clicks into the target's text box, which gives it the foreground through real input.</summary>
    private async Task FocusTargetAsync()
    {
        var r = WindowStyles.WindowRect(_target);
        var (x, y) = (r.Left + 60, r.Bottom - 90);
        if (Desktop.RootWindowAt(x, y) != _target)
        {
            throw new InvalidOperationException($"The text box point is over \"{Desktop.Title(Desktop.RootWindowAt(x, y))}\", not the test target.");
        }

        Desktop.ClickAt(x, y);
        await Task.Delay(300);
    }

    private async Task<string> TransparencyAsync()
    {
        await MorphAsync(IslandState.Compact);
        await Task.Delay(300);
        var window = WindowStyles.WindowRect(island.Handle);
        var pill = island.PillRect();

        island.SetCaptureExcluded(false);
        await Task.Delay(300);
        var visible = Desktop.Capture(window);
        island.SetCaptureExcluded(true);
        await Task.Delay(300);
        var excluded = Desktop.Capture(window);

        // Outside the pill (plus 3 px of anti-aliasing) every pixel must be the target behind the island.
        int outside = 0, wrong = 0;
        uint firstWrong = 0;
        var span = new RECT(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
        for (var y = 0; y < window.Height; y++)
        {
            for (var x = 0; x < window.Width; x++)
            {
                if (pill.Inflate(3).Contains(window.Left + x, window.Top + y))
                {
                    continue;
                }

                outside++;
                var p = visible[y * window.Width + x];
                if (!Desktop.Near(p, TargetColour, 4))
                {
                    firstWrong = wrong++ == 0 ? p : firstWrong;
                    span = new RECT(Math.Min(span.Left, x), Math.Min(span.Top, y), Math.Max(span.Right, x + 1), Math.Max(span.Bottom, y + 1));
                }
            }
        }

        // A vertical line inside the pill, left of the caption (the corner radius is half the height).
        var sampleX = pill.Left + pill.Height - window.Left;
        int line = 0, pillShown = 0, targetShown = 0;
        for (var y = pill.Top + 4 - window.Top; y < pill.Bottom - 4 - window.Top; y++)
        {
            line++;
            pillShown += Desktop.Near(visible[y * window.Width + sampleX], PillColour, 8) ? 1 : 0;
            targetShown += Desktop.Near(excluded[y * window.Width + sampleX], TargetColour, 4) ? 1 : 0;
        }

        var captured = pillShown >= line * 0.95;
        var hidden = targetShown >= line * 0.95;
        return $"- Outside the pill: {wrong} of {outside} pixels differ from the window behind it{(wrong > 0 ? $" (first {Desktop.Rgb(firstWrong)}, within window-relative {span})" : "")} → **{Verdict(wrong == 0)}** (item 1)\n" +
               $"- Capture allowed: {pillShown}/{line} sample pixels inside the pill have the pill colour\n" +
               $"- Capture excluded: {targetShown}/{line} sample pixels show the window behind instead → **{Verdict(captured && hidden)}** (item 5, GDI screen capture; OBS and Snipping Tool by hand)";
    }

    private async Task<string> FocusAsync()
    {
        await FocusTargetAsync();
        if (Desktop.Foreground() != _target)
        {
            return "SKIPPED: the test target did not get the foreground.";
        }

        var before = Desktop.EditText(_target);
        int typed = 0, morphs = 0;
        using var watch = new Desktop.ForegroundWatch();

        async Task MorphLoop()
        {
            for (var i = 0; i < 20; i++)
            {
                island.SetState(Cycle[i % Cycle.Length]);
                morphs++;
                await Task.Delay(300);
            }
        }

        async Task TypeLoop()
        {
            foreach (var c in Sentence)
            {
                if (Desktop.Foreground() != _target)
                {
                    return; // never type into anything but the test target
                }

                Desktop.TypeChar(c);
                typed++;
                await Task.Delay(100);
            }
        }

        await Task.WhenAll(MorphLoop(), TypeLoop());
        await Task.Delay(500);
        var text = Desktop.EditText(_target);
        var complete = text == before + Sentence;
        var kept = Desktop.Foreground() == _target;
        return $"- Island morphs while typing: {morphs}; characters typed: {typed}/{Sentence.Length}\n" +
               $"- Text box received every character: {complete}{(complete ? "" : $" (got \"{text}\")")}\n" +
               $"- Foreground changes: {watch.Changes.Count}; focus still in the target: {kept} → **{Verdict(complete && kept && watch.Changes.Count == 0)}** (item 2)";
    }

    private async Task<string> ClickThroughAsync()
    {
        await MorphAsync(IslandState.Compact);
        var window = WindowStyles.WindowRect(island.Handle);
        var pill = island.PillRect();
        var targetClicks = TargetClicks();
        var pillClicks = island.PillClicks;

        // Inside the island window but outside the pill: must reach the window underneath.
        var (ox, oy) = (window.Left + 20, pill.Top + pill.Height / 2);
        Desktop.MoveCursor(ox, oy);
        await Task.Delay(150);
        var under = Desktop.RootWindowAt(ox, oy);
        if (under != _target && under != island.Handle)
        {
            return $"SKIPPED: the point outside the pill is over \"{Desktop.Title(under)}\".";
        }

        var outsideState = $"window at the point: {Who(under)}, island click-through: {WindowStyles.IsClickThrough(island.Handle)}";
        var messages = island.MouseDownMessages;
        Desktop.ClickAt(ox, oy);
        await Task.Delay(300);
        var outsideReachedTarget = TargetClicks() == targetClicks + 1;
        var outsideHitIsland = island.PillClicks != pillClicks;
        var outsideMessages = island.MouseDownMessages - messages;

        // On the pill: must reach the island and leave the foreground where it was.
        var (px, py) = (pill.Left + pill.Height, pill.Top + pill.Height / 2);
        Desktop.MoveCursor(px, py);
        await Task.Delay(150);
        var onPill = Desktop.RootWindowAt(px, py);
        if (onPill != _target && onPill != island.Handle)
        {
            return $"SKIPPED: the pill point is over \"{Desktop.Title(onPill)}\".";
        }

        var pillState = $"window at the point: {Who(onPill)}, island click-through: {WindowStyles.IsClickThrough(island.Handle)}";
        var clicksBefore = TargetClicks();
        var pillsBefore = island.PillClicks;
        messages = island.MouseDownMessages;
        Desktop.ClickAt(px, py);
        await Task.Delay(300);
        var pillReachedIsland = island.PillClicks == pillsBefore + 1;
        var leaked = TargetClicks() != clicksBefore;
        var pillMessages = island.MouseDownMessages - messages;
        var kept = Desktop.Foreground() == _target;
        Desktop.MoveCursor(window.Left + 20, window.Bottom + 60);

        return $"- Click outside the pill reached the window underneath: {outsideReachedTarget} (island XAML got it: {outsideHitIsland}; button messages at the island: {outsideMessages}; {outsideState})\n" +
               $"- Click on the pill reached the island: {pillReachedIsland} (reached the window underneath: {leaked}; button messages at the island: {pillMessages}; {pillState})\n" +
               $"- Foreground stayed with the other window after clicking the pill: {kept}; click-through style repairs so far: {island.ClickThroughRepairs}\n" +
               $"→ **{Verdict(outsideReachedTarget && !outsideHitIsland && pillReachedIsland && !leaked)}** (item 3)";
    }

    private async Task<string> HotkeyLatencyAsync()
    {
        if (!island.HotkeysRegistered)
        {
            return "SKIPPED: no hotkey chord could be registered.";
        }

        ushort[] chord = island.HotkeyChord == "Ctrl+Alt+Space"
            ? [Desktop.VK_CONTROL, Desktop.VK_MENU, Desktop.VK_SPACE]
            : [Desktop.VK_CONTROL, Desktop.VK_MENU, Desktop.VK_SHIFT, Desktop.VK_SPACE];

        var fromInput = new List<double>();
        var fromMessage = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            await MorphAsync(IslandState.Hidden);
            await Task.Delay(250);
            if (Desktop.Foreground() != _target)
            {
                await FocusTargetAsync(); // if the chord ever leaked, it would only reach the test target
            }

            var frame = new TaskCompletionSource<long>();
            void OnFrame(long at) => frame.TrySetResult(at);
            island.FirstFrameAfterShow += OnFrame;
            var sent = Stopwatch.GetTimestamp();
            Desktop.Chord(chord);
            try
            {
                var at = await frame.Task.WaitAsync(TimeSpan.FromSeconds(2));
                fromInput.Add(Stopwatch.GetElapsedTime(sent, at).TotalMilliseconds);
                fromMessage.Add(island.LastHotkeyToFrameMs);
            }
            finally
            {
                island.FirstFrameAfterShow -= OnFrame;
            }

            await Task.Delay(600);
        }

        fromInput.Sort();
        var refreshMs = 1000.0 / Math.Max(1, Monitors.All()[island.MonitorIndex].RefreshHz);
        var p90 = fromInput[(int)Math.Ceiling(0.9 * fromInput.Count) - 1];
        return $"- Injected {island.HotkeyChord} → first rendered frame: p50 {fromInput[fromInput.Count / 2]:F1} ms, p90 {p90:F1} ms, max {fromInput[^1]:F1} ms (10 runs; WM_HOTKEY → frame median {fromMessage.Order().ElementAt(fromMessage.Count / 2):F1} ms)\n" +
               $"- Plus at most one refresh ({refreshMs:F1} ms) until it is on screen → **{Verdict(p90 + refreshMs <= 100)}** (item 8, ≤ 100 ms)";
    }

    private async Task<string> TopmostAsync()
    {
        await MorphAsync(IslandState.Compact);
        var monitor = Monitors.All()[island.MonitorIndex];
        var original = WindowStyles.WindowRect(_target);

        // A borderless window covering the whole monitor is what borderless-fullscreen games and F11 browsers do.
        WindowStyles.MoveTo(_target, monitor.Bounds);
        await Task.Delay(400);
        await FocusTargetAsync();
        var aboveNormal = Desktop.ZIndex(island.Handle) < Desktop.ZIndex(_target);
        var seenNormal = await PillOnScreenAsync();

        // Some games also make themselves topmost; the newest topmost window wins until the island reasserts itself.
        WindowStyles.SetTopmost(_target, true);
        await FocusTargetAsync();
        var aboveTopmost = Desktop.ZIndex(island.Handle) < Desktop.ZIndex(_target);
        WindowStyles.BringToTopmost(island.Handle);
        await Task.Delay(150);
        var aboveAfterReassert = Desktop.ZIndex(island.Handle) < Desktop.ZIndex(_target);
        var seenAfterReassert = await PillOnScreenAsync();

        WindowStyles.SetTopmost(_target, false);
        WindowStyles.MoveTo(_target, original);
        await Task.Delay(300);
        return $"- Over a borderless fullscreen window: above it in z-order {aboveNormal}, pill on screen {seenNormal} → **{Verdict(aboveNormal && seenNormal)}** (item 4)\n" +
               $"- Over a topmost borderless fullscreen window activated later: above it {aboveTopmost}; after the island reasserts topmost: {aboveAfterReassert}, pill on screen {seenAfterReassert}";
    }

    private async Task<bool> PillOnScreenAsync()
    {
        island.SetCaptureExcluded(false);
        await Task.Delay(250);
        var pill = island.PillRect();
        var line = Desktop.Capture(new RECT(pill.Left + pill.Height, pill.Top + 4, pill.Left + pill.Height + 1, pill.Bottom - 4));
        island.SetCaptureExcluded(true);
        return line.Count(p => Desktop.Near(p, PillColour, 8)) >= line.Length * 0.95;
    }

    private async Task<string> SmoothnessAsync()
    {
        var report = new StringBuilder();
        var monitors = Monitors.All();
        IslandState[] shapes = [IslandState.Compact, IslandState.Listening, IslandState.Expanded];
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = island.PlaceOnMonitor(i);
            await MorphAsync(IslandState.Compact);
            await Task.Delay(400);

            var w = WindowStyles.WindowRect(island.Handle);
            var dpi = WindowStyles.GetDpiForWindow(island.Handle);
            var centred = Math.Abs((w.Left + w.Right) / 2 - (monitor.Bounds.Left + monitor.Bounds.Right) / 2) <= 1 && w.Top == monitor.Bounds.Top;
            var sized = Math.Abs(w.Width - (int)Math.Round(IslandWindow.CanvasWidth * dpi / 96.0)) <= 1;
            report.AppendLine($"- {monitor.Device} ({monitor.RefreshHz} Hz, {dpi} dpi): window {w}; centred at the top {centred}, sized for its DPI {sized} → **{Verdict(centred && sized)}** (item 7 at this scaling)");

            // The capture has to see the island change, so it is visible to capture for the duration.
            island.SetCaptureExcluded(false);
            var tally = new FrameStats.Tally();
            var morphs = new List<(TimeSpan Start, TimeSpan End)>();
            var period = TimeSpan.FromSeconds(1.0 / Math.Max(1, monitor.RefreshHz));
            var span = TimeSpan.FromMilliseconds(300);
            int observed = 0, gaps = 0, onTime = 0;
            double expected = 0;
            var worst = TimeSpan.Zero;
            using (var capture = new CaptureClock(monitor.Handle))
            {
                await Task.Delay(300);
                for (var k = 0; k < 50; k++)
                {
                    var before = FrameStats.Sample();
                    var start = CaptureClock.Now;
                    await MorphAsync(shapes[k % shapes.Length]);
                    morphs.Add((start, CaptureClock.Now));
                    tally.Add(before, FrameStats.Sample());
                    await Task.Delay(60);
                }

                await Task.Delay(100);

                // In the first 300 ms of a morph the pill visibly moves on every refresh, so every refresh should
                // bring a new frame; later the spring's last sub-pixel steps may legitimately produce none.
                foreach (var (start, end) in morphs)
                {
                    var stop = start + span < end ? start + span : end;
                    var frames = capture.FramesBetween(start, stop);
                    observed += frames.Count;
                    expected += (stop - start) / period;
                    for (var f = 1; f < frames.Count; f++)
                    {
                        var gap = frames[f] - frames[f - 1];
                        gaps++;
                        onTime += gap <= period * 1.5 ? 1 : 0;
                        worst = gap > worst ? gap : worst;
                    }
                }
            }

            island.SetCaptureExcluded(true);
            var elapsed = morphs.Sum(m => (m.End - m.Start).TotalSeconds);
            var delivered = expected == 0 ? 0 : Math.Min(1.0, observed / expected);
            var steady = gaps == 0 ? 0 : (double)onTime / gaps;
            report.AppendLine($"  - 50 morphs, {elapsed * 1000 / 50:F0} ms each on average. First 300 ms of each, seen by Windows Graphics Capture: {observed} new frames for ~{expected:F0} refreshes ({delivered:P1}); {onTime}/{gaps} frame-to-frame gaps within 1.5 refreshes ({steady:P1}), worst gap {worst.TotalMilliseconds:F1} ms → **{Verdict(delivered >= 0.95 && steady >= 0.95)}** (item 6)");
            report.AppendLine($"  - DWM composition counters, for the record: {tally.Refreshes} refreshes counted where ~{elapsed * monitor.RefreshHz:F0} elapsed, so they are not usable on this system");
        }

        island.PlaceOnMonitor(0);
        return report.ToString().TrimEnd();
    }

    private async Task<string> IdleAsync()
    {
        await MorphAsync(IslandState.Compact);
        await Task.Delay(3000);
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        await Task.Delay(30_000);
        process.Refresh();
        var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / (clock.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100;
        var (privateWorkingSet, workingSet, privateBytes) = Desktop.Memory();
        return $"- Compact island, 30 s idle: CPU {cpu:F2}% of all cores; private working set {privateWorkingSet:F0} MB (Task Manager's figure), total working set {workingSet:F0} MB (includes shared WinUI DLL pages), private bytes {privateBytes:F0} MB → **{Verdict(privateWorkingSet <= 120 && cpu < 0.5)}** (item 8, ≤ 120 MB and ~0% CPU)";
    }

    private async Task MorphAsync(IslandState state)
    {
        try
        {
            await island.MorphAsync(state).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            Line($"(warning: the morph to {state} did not report completion within 3 s)");
        }
    }

    private int TargetClicks()
    {
        var title = Desktop.Title(_target);
        var at = title.LastIndexOf("clicks=", StringComparison.Ordinal);
        return at < 0 ? 0 : int.Parse(title[(at + 7)..], CultureInfo.InvariantCulture);
    }

    private void CloseTarget()
    {
        if (_target != 0)
        {
            WindowStyles.PostMessage(_target, WindowStyles.WM_CLOSE, 0, 0);
            _target = 0;
        }

        if (_targetProcess is { HasExited: false } p && !p.WaitForExit(3000))
        {
            p.Kill();
        }
    }

    private string Who(nint hwnd) =>
        hwnd == island.Handle ? "island" : hwnd == _target ? "test window" : $"\"{Desktop.Title(hwnd)}\"";

    private static string Verdict(bool pass) => pass ? "PASS" : "FAIL";

    private void Line(string text) => _report.AppendLine(text);
}
