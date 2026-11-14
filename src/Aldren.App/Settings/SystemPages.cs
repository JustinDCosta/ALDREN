using Aldren.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

/// <summary>The local model, memory use and uptime, refreshed every two seconds while the page is open.</summary>
public sealed class SystemPage() : SettingsPage("Models & performance")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        Header("Local model");
        var model = Card("", null, glyph: "");
        var gaming = Card("Gaming mode", null, glyph: "");
        Header("Resources");
        var memory = Card("Memory", null, glyph: "");
        var uptime = Card("Running", null, glyph: "");
        Paragraph("The main model (Qwen3.5-4B) lives in the graphics memory and unloads after 30 idle minutes (5 on battery) or when a game starts; a small CPU model answers meanwhile. It loads again in a few seconds when you ask something.");

        async Task ShowAsync()
        {
            var s = await core.GetSystemInfoAsync(CancellationToken.None);
            model.Title.Text = s.GpuLoaded ? $"{s.Model} on the GPU" : s.CpuLoaded ? $"{s.Model} on the CPU (fallback)" : "No model loaded";
            model.Describe((s.GpuLoaded || s.CpuLoaded ? "Loaded and ready." : "It loads on the next request.")
                + (s.FreeVramMiB is { } free ? $"  ·  {free:N0} MiB of graphics memory free" : "")
                + (s.OnAc ? "  ·  on AC power" : "  ·  on battery"));
            gaming.Describe(s.GamingActive ? $"Active ({(s.Gaming == "on" ? "you switched it on" : "detected")}): the GPU is free." : $"Not active ({s.Gaming}). Change it in Modes.");
            memory.Describe($"ALDREN's service {s.CoreMemoryMb:N0} MB  ·  this app {Environment.WorkingSet / (1024 * 1024):N0} MB  ·  model process {s.ModelMemoryMb:N0} MB");
            uptime.Describe(s.UptimeHours < 1 ? $"{s.UptimeHours * 60:N0} minutes" : $"{s.UptimeHours:N1} hours");
        }

        await ShowAsync();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += async (_, _) =>
        {
            try
            {
                await ShowAsync();
            }
            catch (Exception)
            {
                timer.Stop(); // Core went away; the page reloads when reopened
            }
        };
        timer.Start();
        Unloaded += (_, _) => timer.Stop();
    }
}

/// <summary>Where your data lives, how much there is, and the ways to delete it.</summary>
public sealed class PrivacyPage() : SettingsPage(
    "Privacy & data",
    "Everything ALDREN knows about you is in the folders below, on this PC. Nothing is uploaded, synced or used for training.")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var info = await core.GetSystemInfoAsync(CancellationToken.None);

        Header("What is stored");
        var open = Named(new Button { Content = "Open folder" }, "Open the data folder");
        open.Click += (_, _) => ShellApp.Instance.OpenFolder(info.DataFolder);
        Card("Database and logs", $"{info.DataFolder}\nDatabase {info.DatabaseKb:N0} KB  ·  logs {info.LogsKb:N0} KB (logs never hold passwords or keys)", open, "");

        var wipe = Named(new Button { Content = "Delete all…" }, "Delete all memories");
        var memories = Card("Memories", Count(info.Memories, "memory", "memories") + " you asked ALDREN to keep.", wipe, "");
        wipe.IsEnabled = info.Memories > 0;
        wipe.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Delete all memories?",
                Content = $"ALDREN forgets all {info.Memories} of them. This can't be undone.",
                PrimaryButtonText = "Delete all",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var deleted = await core.DeleteAllMemoriesAsync(CancellationToken.None);
                memories.Describe($"Deleted {deleted}. Nothing is remembered now.");
                wipe.IsEnabled = false;
            }
        };

        Card("Reminders, tasks and routines", $"{Count(info.Reminders, "reminder", "reminders")}, {Count(info.Tasks, "open task", "open tasks")}, {Count(info.Routines, "routine", "routines")}. Manage them on their own pages.", glyph: "");

        var clear = Named(new Button { Content = "Clear now" }, "Clear the conversation");
        var conversation = Card("This session's conversation", "Kept in memory only; it is gone when ALDREN closes.", clear, "");
        clear.Click += async (_, _) =>
        {
            await ShellApp.Instance.NewConversationAsync();
            conversation.Describe("Cleared.");
        };

        Header("What never happens without you");
        Card("Going online", "Only for research you ask for, through the gateway on this PC. Your screen, files, clipboard and memories need a separate yes each time; passwords and keys are never sent.", glyph: "");
        Card("Screen and capture", "ALDREN reads the screen only when a request needs it, and its own windows stay out of screenshots and streams unless you allow it in General.", glyph: "");
        Card("Activity history", "Every action is recorded here on this PC for 180 days, then deleted.", glyph: "");
    }

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
}
