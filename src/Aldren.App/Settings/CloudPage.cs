using System.Diagnostics;
using Aldren.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

/// <summary>The cloud gateway (ARCHITECTURE §8.6): status, the chain check, the dashboard link and the cloud switches.</summary>
public sealed class CloudPage() : SettingsPage("Cloud (FreeLLMAPI)")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var settings = await core.GetSettingsAsync(CancellationToken.None);
        Paragraph("Cloud models are used only when you ask for research (\"look up…\", \"research…\") or say yes to \"want me to look that up online?\". Your screen, files, clipboard and memories never go online without a separate yes, and passwords or keys never do. Every cloud request shows an indicator and is listed in Activity history.");

        var status = new StackPanel { Spacing = 4 };
        Body.Children.Add(status);
        Fill(status, await core.GetCloudStatusAsync(CancellationToken.None));

        var dashboard = Named(new Button { Content = "Open gateway dashboard" }, "Open gateway dashboard");
        dashboard.Click += async (_, _) =>
        {
            var info = await core.GetCloudStatusAsync(CancellationToken.None);
            if (info.Port is { } port)
            {
                // The gateway accepts only the "localhost" host name (DNS-rebinding guard).
                Process.Start(new ProcessStartInfo($"http://localhost:{port}/") { UseShellExecute = true })?.Dispose();
            }
        };
        var recheck = Named(new Button { Content = "Check the chains again" }, "Check the chains again");
        recheck.Click += async (_, _) => Fill(status, await core.RecheckCloudAsync(CancellationToken.None));
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { dashboard, recheck } });

        Paragraph("Setting it up (once): in the dashboard, create your account, add keys only for Google (Gemini), Groq, Cerebras, Mistral, OpenRouter or GitHub Models, add a search engine key (for example Tavily or Brave) under Models > Search, and fill the four aldren-* chains under Fallback chains: aldren-fast with Groq or Cerebras models, aldren-reason and aldren-research with Gemini, Groq, Cerebras or Mistral, aldren-vision with Gemini. Click Save changes, then \"Check the chains again\" here.");

        var enabled = Named(new ToggleSwitch { Header = "Cloud access (runs the gateway; nothing is sent unless you ask)", IsOn = settings.Cloud.Enabled }, "Cloud access");
        var fallback = Named(new ToggleSwitch { Header = "Answer simple questions online when the local model is unavailable", IsOn = settings.Cloud.QuickFallback }, "Quick Brain cloud fallback");
        Body.Children.Add(enabled);
        Body.Children.Add(fallback);
        Paragraph("Off by default. When on, only the question and ALDREN's persona go to aldren-fast, and only when no local model can answer (for example while a game holds the graphics memory).");

        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var save = Named(new Button { Content = "Save" }, "Save cloud settings");
        save.Click += async (_, _) =>
        {
            var updated = settings with { Cloud = new CloudSettings(enabled.IsOn, fallback.IsOn) };
            var saved = await core.UpdateSettingsAsync(updated, CancellationToken.None);
            result.Text = saved.Saved ? "Saved. The gateway starts or stops within a few seconds." : string.Join(" ", saved.Errors);
            if (saved.Saved)
            {
                settings = updated;
            }
        };
        Body.Children.Add(save);
        Body.Children.Add(result);
    }

    private static void Fill(StackPanel panel, CloudStatusInfo info)
    {
        panel.Children.Clear();
        panel.Children.Add(new TextBlock
        {
            Text = !info.Enabled ? "Cloud access is off."
                : info.Running ? $"Gateway running on 127.0.0.1:{info.Port} (FreeLLMAPI 0.16.14)."
                : $"Gateway not running: {info.Problem}",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        if (info.KeysOutsideAllowlist.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Cloud use is blocked: keys are enabled for providers ALDREN doesn't allow ({string.Join(", ", info.KeysOutsideAllowlist)}). Switch them off in the dashboard.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            });
        }

        foreach (var line in info.Chains)
        {
            panel.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
        }
    }
}
