using System.Diagnostics;
using Microsoft.Playwright;

// S6 Chrome DevTools Protocol checks through Playwright, using the installed Chrome.
//   attach                 attaches to your running Chrome once remote debugging is on at chrome://inspect/#remote-debugging
//                          and you approve Chrome's prompt; reads the active tab's title and disconnects
//   profile [--headless]   starts Chrome on the separate profile in E:\AldrenData\browser-profile and drives the test page
const string Results = @"E:\AldrenData\bench\s6\results.md";

return args.FirstOrDefault() switch
{
    "attach" => await AttachAsync(),
    "profile" => await ProfileAsync(args.Contains("--headless")),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("CdpAttach attach | profile [--headless]");
    return 1;
}

static async Task<int> AttachAsync()
{
    var activePort = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\DevToolsActivePort");
    if (!File.Exists(activePort))
    {
        Console.Error.WriteLine("Remote debugging is off: turn it on at chrome://inspect/#remote-debugging, then run this again.");
        return 1;
    }

    // First line: the port; second: the browser's WebSocket path.
    var lines = await File.ReadAllLinesAsync(activePort);
    using var playwright = await Playwright.CreateAsync();
    Console.WriteLine("Connecting. Chrome asks whether to allow remote debugging: click Allow.");
    var clock = Stopwatch.StartNew();
    var browser = await playwright.Chromium.ConnectOverCDPAsync($"ws://127.0.0.1:{lines[0].Trim()}{lines[1].Trim()}", new() { Timeout = 120_000 });
    var connectSeconds = clock.Elapsed.TotalSeconds;
    try
    {
        var pages = browser.Contexts.SelectMany(c => c.Pages).ToList();
        string? title = null;
        foreach (var page in pages)
        {
            var visible = page.EvaluateAsync<string>("document.visibilityState");
            if (await Task.WhenAny(visible, Task.Delay(2000)) == visible && visible.Result == "visible")
            {
                title = await page.TitleAsync();
                break;
            }
        }

        Console.WriteLine($"Active tab title: {title ?? "(no visible tab found)"}");
        // The title can be personal, so results.md only records that it was read.
        await AppendAsync($"- CDP attach to your Chrome: connected {connectSeconds:F1} s after starting (includes your approval), {pages.Count} tabs visible to " +
                          $"Playwright, active tab title {(title is null ? "not found" : $"read ({title.Length} characters)")}; disconnected, Chrome left running");
    }
    finally
    {
        await browser.CloseAsync(); // for a browser Playwright attached to, this only disconnects
    }

    return 0;
}

static async Task<int> ProfileAsync(bool headless)
{
    using var playwright = await Playwright.CreateAsync();
    var clock = Stopwatch.StartNew();
    await using var context = await playwright.Chromium.LaunchPersistentContextAsync(@"E:\AldrenData\browser-profile",
        new() { Channel = "chrome", Headless = headless });
    var launchMs = clock.Elapsed.TotalMilliseconds;
    var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
    await page.GotoAsync(new Uri(Path.Combine(AppContext.BaseDirectory, "testpage", "index.html")).AbsoluteUri);
    clock.Restart();
    await page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
    var clickMs = clock.Elapsed.TotalMilliseconds;
    var logged = await page.Locator("#results").InnerTextAsync();
    var version = context.Browser?.Version ?? "unknown";
    await AppendAsync($"- Separate profile through Playwright: installed Chrome {version} started on E:\\AldrenData\\browser-profile in {launchMs:F0} ms " +
                      $"({(headless ? "headless" : "windowed")}), test page \"{await page.TitleAsync()}\", Playwright click (real input events) in {clickMs:F0} ms, " +
                      $"page logged it: {logged.Contains("Clicked Save draft", StringComparison.Ordinal)}");
    await context.CloseAsync();
    return 0;
}

static async Task AppendAsync(string line)
{
    Console.WriteLine(line);
    Directory.CreateDirectory(Path.GetDirectoryName(Results)!);
    await File.AppendAllTextAsync(Results, line + Environment.NewLine);
}
