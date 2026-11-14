using System.Diagnostics;
using Aldren.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

/// <summary>The cloud gateway (ARCHITECTURE §8.6): status, the chain check, the dashboard link and the cloud switches.</summary>
public sealed class CloudPage() : SettingsPage(
    "Cloud",
    "Cloud models are used only when you ask for research (\"look up…\", \"research…\") or say yes to \"want me to look that up online?\". Your screen, files, clipboard and memories never go online without a separate yes, and passwords or keys never do. Every cloud request shows an indicator and is listed in Activity history.")
{
    protected override async Task LoadAsync(IAldrenCore core)
    {
        var settings = await core.GetSettingsAsync(CancellationToken.None);

        var dashboard = Named(new Button { Content = "Open dashboard" }, "Open gateway dashboard");
        var recheck = Named(new Button { Content = "Check again" }, "Check the chains again");
        var status = Card("Gateway", null, Row(dashboard, recheck), "");
        var chains = new StackPanel { Spacing = 6 };
        var chainsCard = AddCard(chains);
        Fill(status, chains, chainsCard, await core.GetCloudStatusAsync(CancellationToken.None));

        dashboard.Click += async (_, _) =>
        {
            var info = await core.GetCloudStatusAsync(CancellationToken.None);
            if (info.Port is { } port)
            {
                // The gateway accepts only the "localhost" host name (DNS-rebinding guard).
                Process.Start(new ProcessStartInfo($"http://localhost:{port}/") { UseShellExecute = true })?.Dispose();
            }
            else
            {
                Note("The gateway isn't running, so there is no dashboard to open. Switch cloud access on below and wait a few seconds.", InfoBarSeverity.Warning);
            }
        };
        recheck.Click += async (_, _) => Fill(status, chains, chainsCard, await core.RecheckCloudAsync(CancellationToken.None));

        Header("Switches");
        var enabled = Switch(settings.Cloud.Enabled, "Cloud access");
        Card("Cloud access", "Runs the gateway on this PC. Nothing is sent unless you ask for research.", enabled);
        SaveOnToggle(enabled, core, (s, on) => s with { Cloud = s.Cloud with { Enabled = on } });

        var fallback = Switch(settings.Cloud.QuickFallback, "Quick Brain cloud fallback");
        Card(
            "Answer simple questions online when the local model is unavailable",
            "Off by default. When on, only the question and ALDREN's persona go to the aldren-fast chain, and only while no local model can answer (for example while a game holds the graphics memory).",
            fallback);
        SaveOnToggle(fallback, core, (s, on) => s with { Cloud = s.Cloud with { QuickFallback = on } });

        Header("Setting it up (once)");
        Paragraph("1. Open the dashboard and create its account.\n2. Add keys only for Google (Gemini), Groq, Cerebras, Mistral, OpenRouter or GitHub Models, and one search engine key (Tavily or Brave) under Models > Search.\n3. Under Fallback chains, fill the four aldren-* chains: aldren-fast with Groq or Cerebras models; aldren-reason and aldren-research with Gemini, Groq, Cerebras or Mistral; aldren-vision with Gemini.\n4. Save changes, then click \"Check again\" here: each chain should say ready.");
    }

    private static void Fill(SettingCard status, StackPanel chains, Border chainsCard, CloudStatusInfo info)
    {
        status.Title.Text = !info.Enabled ? "Cloud access is off"
            : info.Running ? "Gateway running"
            : "Gateway not running";
        status.Describe(!info.Enabled ? "Research and online answers are unavailable until you switch it on."
            : info.Running ? $"On 127.0.0.1:{info.Port}, reachable from this PC only (FreeLLMAPI 0.16.14)."
            : info.Problem ?? "It is starting, or it stopped; ALDREN restarts it on its own.");

        chains.Children.Clear();
        if (info.KeysOutsideAllowlist.Count > 0)
        {
            chains.Children.Add(new TextBlock
            {
                Text = $"Cloud use is blocked: keys are enabled for providers ALDREN doesn't allow ({string.Join(", ", info.KeysOutsideAllowlist)}). Switch them off in the dashboard.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            });
        }

        foreach (var line in info.Chains)
        {
            var ready = line.Contains(": ready", StringComparison.Ordinal);
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new FontIcon
            {
                Glyph = ready ? "" : "",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(ready ? Microsoft.UI.Colors.MediumSeaGreen : Microsoft.UI.Colors.Orange),
            });
            var text = new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            chains.Children.Add(row);
        }

        chainsCard.Visibility = chains.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
