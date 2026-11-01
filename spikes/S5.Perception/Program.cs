using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using S5.Perception.Capture;
using S5.Perception.Grounding;
using S5.Perception.Ocr;
using S5.Perception.Uia;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Point = S5.Perception.Grounding.Point;

namespace S5.Perception;

internal sealed record CaptureRecord(string Process, int Width, int Height, double UiaMilliseconds, List<UiElementInfo> Elements,
    List<UiElementInfo> Texts);

// S5 perception probe. Modes:
//   capture            Ctrl+Alt+S saves the foreground window (screenshot + UI Automation tree) to E:\AldrenData\bench\s5;
//                      Ctrl+Alt+Q stops. Nothing leaves the PC.
//   analyze            UI Automation timing and coverage, OCR timing and agreement with UIA names, per app
//   ground [--n 5]     asks the 4B model to find elements: numbered boxes vs raw coordinates, with latency
//   selftest           runs the whole pipeline on a synthetic window of its own (no personal content)
internal static class Program
{
    private const string Root = @"E:\AldrenData\bench\s5";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [STAThread]
    private static int Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(-4); // per-monitor v2: UI Automation and capture agree on physical pixels
        var report = new StringBuilder();
        var code = (args.FirstOrDefault() ?? "") switch
        {
            "capture" => CaptureLoop(),
            "analyze" => Analyze(report),
            "ground" => Ground(report, int.Parse(Option(args, "--n") ?? "5", CultureInfo.InvariantCulture),
                int.Parse(Option(args, "--max-marks") ?? "60", CultureInfo.InvariantCulture), int.Parse(Option(args, "--max-width") ?? "0", CultureInfo.InvariantCulture)),
            "selftest" => SelfTest(report),
            "testwindow" => TestWindow(),
            _ => Usage(),
        };
        if (report.Length > 0)
        {
            Directory.CreateDirectory(Root);
            File.AppendAllText(Path.Combine(Root, "results.md"), report + Environment.NewLine);
        }

        return code;
    }

    private static int Usage()
    {
        Console.WriteLine("S5.Perception capture | analyze | ground [--n 5] | selftest");
        return 1;
    }

    private static int CaptureLoop()
    {
        const int save = 1, quit = 2;
        if (!Native.RegisterHotKey(0, save, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 'S')
            || !Native.RegisterHotKey(0, quit, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 'Q'))
        {
            Console.Error.WriteLine("Ctrl+Alt+S or Ctrl+Alt+Q is already taken by another app.");
            return 1;
        }

        Console.WriteLine($"Ctrl+Alt+S saves the window you're in; Ctrl+Alt+Q stops. Captures go to {Path.Combine(Root, "captures")}.");
        var count = 0;
        while (Native.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.Message != Native.WM_HOTKEY)
            {
                continue;
            }

            if ((int)msg.WParam == quit)
            {
                break;
            }

            var hwnd = Native.GetForegroundWindow();
            try
            {
                var (record, png) = CaptureWindow(hwnd).GetAwaiter().GetResult();
                var folder = Path.Combine(Root, "captures", $"{DateTime.Now:yyyyMMdd-HHmmss}-{record.Process}");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "screen.png"), png);
                File.WriteAllText(Path.Combine(folder, "uia.json"), JsonSerializer.Serialize(record, Json));
                Console.WriteLine($"{++count}. {record.Process}: {record.Elements.Count} interactive elements, UI Automation {record.UiaMilliseconds:F0} ms");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Capture failed: {ex.Message}");
            }
        }

        return 0;
    }

    private static async Task<(CaptureRecord Record, byte[] Png)> CaptureWindow(nint hwnd)
    {
        var bounds = Native.VisibleBounds(hwnd);
        using var bitmap = await WindowCapture.GrabAsync(hwnd, CancellationToken.None);
        var (elements, texts, ms) = UiaSnapshot.Take(hwnd, bounds);
        var png = await WindowCapture.ToPngAsync(bitmap);
        return (new CaptureRecord(Native.ProcessName(hwnd), bitmap.PixelWidth, bitmap.PixelHeight, ms, elements, texts), png);
    }

    private static IEnumerable<(string Folder, CaptureRecord Record)> Captures() =>
        Directory.Exists(Path.Combine(Root, "captures"))
            ? Directory.GetDirectories(Path.Combine(Root, "captures")).Order()
                .Select(f => (f, JsonSerializer.Deserialize<CaptureRecord>(File.ReadAllText(Path.Combine(f, "uia.json")))!))
            : [];

    private static bool NoCaptures()
    {
        if (Captures().Any())
        {
            return false;
        }

        Console.Error.WriteLine($"No captures in {Path.Combine(Root, "captures")} yet: run the capture mode first.");
        return true;
    }

    private static int Analyze(StringBuilder report)
    {
        if (NoCaptures())
        {
            return 1;
        }

        var rows = new List<(string Process, double UiaMs, int Named, double OcrMs, int Agree, int Asked)>();
        var misses = new List<(string Process, string Kind)>();
        var offsets = new List<(string Process, double Dx, double Dy, bool Inside)>();
        foreach (var (folder, record) in Captures())
        {
            using var bitmap = Decode(File.ReadAllBytes(Path.Combine(folder, "screen.png"))).GetAwaiter().GetResult();
            var (words, ocrMs) = OcrRunner.ReadAsync(bitmap).GetAwaiter().GetResult();
            var pixels = new byte[4 * bitmap.PixelWidth * bitmap.PixelHeight];
            bitmap.CopyToBuffer(pixels.AsBuffer());
            var checkedTexts = record.Texts.Select(e => (Element: e, Verdict: OcrRunner.Agrees(e, words))).Where(t => t.Verdict is not null)
                .Select(t => (t.Element, t.Verdict, Miss: t.Verdict == true ? null
                    : !OcrRunner.NothingInside(t.Element, words) ? "other text"
                    : BlankBox(pixels, bitmap.PixelWidth, bitmap.PixelHeight, t.Element.Bounds) ? "not drawn" : "nothing read"))
                .ToList();
            // Labels UI Automation reports but nothing draws (clipped by a scroll area, covered by another element) aren't OCR's to read.
            var drawn = checkedTexts.Where(t => t.Miss != "not drawn").ToList();
            rows.Add((record.Process, record.UiaMilliseconds, record.Elements.Count(e => e.Name.Length > 0), ocrMs,
                drawn.Count(t => t.Verdict == true), drawn.Count));
            misses.AddRange(checkedTexts.Where(t => t.Miss is not null).Select(t => (record.Process, t.Miss!)));
            offsets.AddRange(checkedTexts.Select(t => OcrRunner.FirstWordOffset(t.Element, words)).OfType<(double Dx, double Dy, bool Inside)>()
                .Select(o => (record.Process, o.Dx, o.Dy, o.Inside)));
        }

        Line(report, $"## S5 analyze, {DateTime.Now:yyyy-MM-dd HH:mm} ({rows.Count} captures)");
        Line(report, "| App | Captures | UI Automation p90 | Named interactive elements (median) | OCR p90 | OCR reads the drawn UIA text labels |");
        Line(report, "|---|---|---|---|---|---|");
        foreach (var app in rows.GroupBy(r => r.Process))
        {
            Line(report, $"| {app.Key} | {app.Count()} | {P90(app.Select(r => r.UiaMs))} ms | {Median(app.Select(r => (double)r.Named))} | " +
                         $"{P90(app.Select(r => r.OcrMs))} ms | {Share(app.Sum(r => r.Agree), app.Sum(r => r.Asked))} |");
        }

        Line(report, $"| All | {rows.Count} | {P90(rows.Select(r => r.UiaMs))} ms | {Median(rows.Select(r => (double)r.Named))} | " +
                     $"{P90(rows.Select(r => r.OcrMs))} ms | {Share(rows.Sum(r => r.Agree), rows.Sum(r => r.Asked))} |");
        Line(report, $"French OCR installed: {OcrRunner.FrenchAvailable}");
        Line(report, "");
        Line(report, "| App | Labels not drawn (blank box) | Misses: nothing read | Misses: other text read | Label first words found nearby | Their offset from the box (median dx, dy) | Of those, centre outside the box |");
        Line(report, "|---|---|---|---|---|---|---|");
        foreach (var app in misses.Select(m => m.Process).Concat(offsets.Select(o => o.Process)).Distinct())
        {
            var m = misses.Where(x => x.Process == app).ToList();
            var o = offsets.Where(x => x.Process == app).ToList();
            Line(report, $"| {app} | {m.Count(x => x.Kind == "not drawn")} | {m.Count(x => x.Kind == "nothing read")} | {m.Count(x => x.Kind == "other text")} | " +
                         $"{o.Count} | {Median(o.Select(x => x.Dx))}, {Median(o.Select(x => x.Dy))} px | {o.Count(x => !x.Inside)} |");
        }

        return 0;
    }

    private static int Ground(StringBuilder report, int perCapture, int maxMarks, int maxWidth)
    {
        if (NoCaptures())
        {
            return 1;
        }

        var captures = Captures().Select(c => (c.Folder, c.Record, Png: File.ReadAllBytes(Path.Combine(c.Folder, "screen.png")))).ToList();
        Line(report, $"## S5 ground, {DateTime.Now:yyyy-MM-dd HH:mm} ({captures.Count} captures, up to {perCapture} questions each, " +
                     $"at most {maxMarks} boxes, {(maxWidth > 0 ? $"images scaled to {maxWidth} px wide at most" : "full-size images")})");
        return RunGrounding(report, captures, perCapture, maxMarks, maxWidth);
    }

    // Each capture's numbered image is saved next to it as marks.png, so a wrong answer can be checked against what the model saw.
    // maxWidth > 0 scales both images down first, so the vision encoder sees the number tags at the size they were drawn.
    private static int RunGrounding(StringBuilder report, List<(string Folder, CaptureRecord Record, byte[] Png)> captures, int perCapture,
        int maxMarks = 60, int maxWidth = 0, bool showAnswers = false)
    {
        var marks = new List<(UiElementInfo, Point?)>();
        var raw = new List<(UiElementInfo, Point?)>();
        var latencies = new List<double>();
        var rng = new Random(9);
        using var cts = new CancellationTokenSource(TimeSpan.FromHours(1));
        var probe = GroundingProbe.StartAsync(cts.Token).GetAwaiter().GetResult();
        try
        {
            foreach (var (folder, record, png) in captures)
            {
                // Enabled, named controls with unique names: the things a user would ask for by name.
                var candidates = record.Elements.Where(e => e.Name.Trim().Length > 0 && e.IsEnabled && e.ControlType != "Document")
                    .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1).Select(g => g.First())
                    .Take(maxMarks).Select((e, i) => e with { Id = i + 1 }).ToList();
                if (candidates.Count == 0)
                {
                    continue;
                }

                var scale = maxWidth > 0 && record.Width > maxWidth ? maxWidth / (double)record.Width : 1.0;
                var marked = MarksRenderer.Render(png, candidates, scale);
                var plain = scale < 1 ? MarksRenderer.Render(png, [], scale) : png;
                File.WriteAllBytes(Path.Combine(folder, "marks.png"), marked);
                foreach (var target in candidates.OrderBy(_ => rng.Next()).Take(perCapture))
                {
                    var (markAnswer, markMs) = probe.AskAsync(marked, GroundingProbe.MarkQuestion(target), 8, cts.Token).GetAwaiter().GetResult();
                    var chosen = GroundingProbe.ParseMark(markAnswer) is { } id ? candidates.FirstOrDefault(c => c.Id == id) : null;
                    marks.Add((target, chosen?.Bounds.Center));
                    var (pointAnswer, pointMs) = probe.AskAsync(plain, GroundingProbe.PointQuestion(target), 40, cts.Token).GetAwaiter().GetResult();
                    var point = GroundingProbe.ParsePoint(pointAnswer, record.Width, record.Height);
                    raw.Add((target, point));
                    latencies.AddRange([markMs, pointMs]);
                    if (showAnswers)
                    {
                        Console.WriteLine($"  {target.Name} (box {target.Bounds.Left:F0},{target.Bounds.Top:F0} {target.Bounds.Width:F0}x{target.Bounds.Height:F0}): " +
                                          $"mark \"{markAnswer}\" -> {chosen?.Name ?? "none"}; point \"{pointAnswer}\" -> {point}");
                    }
                }
            }
        }
        finally
        {
            probe.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        Line(report, $"- Numbered boxes: {GroundingScore.Accuracy(marks):P1} correct over {marks.Count} questions (target ≥ 80%)");
        Line(report, $"- Raw coordinates: {GroundingScore.Accuracy(raw):P1} correct over {raw.Count} questions (reported)");
        Line(report, $"- Latency per question: p50 {Median(latencies)} ms, p90 {P90(latencies)} ms (target ≤ 4 s)");
        return 0;
    }

    private static int SelfTest(StringBuilder report)
    {
        var (record, png) = CaptureTestWindow();
        var folder = Path.Combine(Root, "selftest");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "screen.png"), png);
        using var bitmap = Decode(png).GetAwaiter().GetResult();
        var (words, ocrMs) = OcrRunner.ReadAsync(bitmap).GetAwaiter().GetResult();
        var verdicts = record.Texts.Select(e => OcrRunner.Agrees(e, words)).Where(v => v is not null).ToList();
        // Button labels are centred, so their OCR text and UI Automation box share a centre when the two coordinate spaces agree.
        var offsets = record.Elements.Where(e => e.ControlType == "Button").Select(e => TextOffset(e, words)).OfType<(double Dx, double Dy)>().ToList();
        Line(report, $"## S5 selftest, {DateTime.Now:yyyy-MM-dd HH:mm} (synthetic window, {record.Width}x{record.Height})");
        Line(report, $"- UI Automation: {record.Elements.Count} interactive elements and {record.Texts.Count} text labels in {record.UiaMilliseconds:F0} ms: " +
                     string.Join(", ", record.Elements.Select(e => $"{e.ControlType} \"{e.Name}\"")));
        Line(report, $"- OCR: {words.Count} words in {ocrMs:F0} ms; reads the UIA text labels on {Share(verdicts.Count(v => v == true), verdicts.Count)}");
        Line(report, offsets.Count == 0
            ? "- Alignment: OCR read no button text"
            : $"- Alignment (button text centre minus UIA box centre): mean dx {offsets.Average(o => o.Dx):F1} px, dy {offsets.Average(o => o.Dy):F1} px over {offsets.Count} buttons");
        return RunGrounding(report, [(folder, record, png)], perCapture: 9, showAnswers: true);
    }

    // The target window lives in a child process, like every window ALDREN reads. It shows for about a second without taking focus.
    private static (CaptureRecord Record, byte[] Png) CaptureTestWindow()
    {
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "testwindow")
            { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false })!;
        try
        {
            var handle = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("The test window process exited before showing its window.");
            Thread.Sleep(300); // let DWM present the first frame
            return CaptureWindow((nint)long.Parse(handle, CultureInfo.InvariantCulture)).GetAwaiter().GetResult();
        }
        finally
        {
            child.StandardInput.Close();
            if (!child.WaitForExit(5000))
            {
                child.Kill();
            }
        }
    }

    private static int TestWindow()
    {
        // Hardware-rendered WPF drew nothing here on the test laptop: every capture path, PrintWindow included, saw a blank
        // client area. Software rendering draws normally, and the window only exists to be captured.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var window = new Window { Title = "ALDREN perception test", Width = 680, Height = 400, Topmost = true, ShowActivated = false,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var panel = new WrapPanel { Margin = new Thickness(24) };
        foreach (var label in new[] { "Save", "Cancel", "Open file", "Settings", "Help", "Delete draft", "Export PDF", "Send" })
        {
            panel.Children.Add(new Button { Content = label, Margin = new Thickness(10), Padding = new Thickness(16, 8, 16, 8), FontSize = 18 });
        }

        panel.Children.Add(new CheckBox { Content = "Remember me", Margin = new Thickness(10), FontSize = 18 });
        window.Content = panel;
        window.ContentRendered += (_, _) => Console.WriteLine(new WindowInteropHelper(window).Handle.ToInt64());
        var dispatcher = Dispatcher.CurrentDispatcher;
        new Thread(() =>
        {
            Console.In.ReadToEnd(); // the parent closes our stdin when it's done
            dispatcher.InvokeShutdown();
        }) { IsBackground = true }.Start();
        window.Show();
        Dispatcher.Run();
        return 0;
    }

    private static (double Dx, double Dy)? TextOffset(UiElementInfo element, IReadOnlyList<OcrWord> words)
    {
        var inside = words.Where(w => GroundingScore.PointInElement(w.Bounds.Center, element)).ToList();
        if (inside.Count == 0)
        {
            return null;
        }

        double left = inside.Min(w => w.Bounds.Left), right = inside.Max(w => w.Bounds.Right);
        double top = inside.Min(w => w.Bounds.Top), bottom = inside.Max(w => w.Bounds.Bottom);
        return ((left + right) / 2 - element.Bounds.Center.X, (top + bottom) / 2 - element.Bounds.Center.Y);
    }

    // Nothing is drawn in the box when under 1% of its pixels are more than 50 (of 255) away from its median brightness; glyphs
    // give a few percent or more.
    private static bool BlankBox(byte[] pixels, int width, int height, Grounding.Rect r)
    {
        int x0 = Math.Max(0, (int)r.Left), y0 = Math.Max(0, (int)r.Top), x1 = Math.Min(width, (int)r.Right), y1 = Math.Min(height, (int)r.Bottom);
        var lum = new List<double>();
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = 4 * (y * width + x);
                lum.Add(0.114 * pixels[i] + 0.587 * pixels[i + 1] + 0.299 * pixels[i + 2]);
            }
        }

        if (lum.Count == 0)
        {
            return true;
        }

        var median = lum.Order().ElementAt(lum.Count / 2);
        return lum.Count(l => Math.Abs(l - median) > 50) < 0.01 * lum.Count;
    }

    private static async Task<SoftwareBitmap> Decode(byte[] png)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private static string P90(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? "-" : sorted[(int)Math.Ceiling(0.9 * sorted.Count) - 1].ToString("F0", CultureInfo.InvariantCulture);
    }

    private static string Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? "-" : sorted[sorted.Count / 2].ToString("F0", CultureInfo.InvariantCulture);
    }

    private static string Share(int part, int whole) => whole == 0 ? "-" : $"{100.0 * part / whole:F1}% ({part}/{whole})";

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Line(StringBuilder report, string text)
    {
        Console.WriteLine(text);
        report.AppendLine(text);
    }
}
