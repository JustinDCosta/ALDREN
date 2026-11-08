using System.Collections.Specialized;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Aldren.App.Expanded;

/// <summary>
/// The conversation sidebar: docked to the right edge of the screen you're working on, full height, with every typed and
/// spoken turn of this session. Opened from the tray, with Ctrl+Enter in the text box, or by clicking an answer in the island.
/// </summary>
public sealed partial class ExpandedWindow : Window
{
    private const int Width = 440;

    public ExpandedWindow()
    {
        InitializeComponent();
        DockRight();
        var conversation = ShellApp.Instance.Conversation;
        Conversation.ItemsSource = conversation;
        conversation.CollectionChanged += OnConversationChanged;
        WindowKeys.CloseOnEscape(this, Root);
        Root.Loaded += (_, _) =>
        {
            ScrollToEnd();
            Input.Focus(FocusState.Programmatic);
        };

        // While ALDREN speaks, Escape is its stop key system-wide, so here it stops the speech before it closes the window.
        OnSpeakingChanged(ShellApp.Instance.IsSpeaking);
        ShellApp.Instance.SpeakingChanged += OnSpeakingChanged;
        Closed += (_, _) =>
        {
            conversation.CollectionChanged -= OnConversationChanged;
            ShellApp.Instance.SpeakingChanged -= OnSpeakingChanged;
        };
    }

    private void OnSpeakingChanged(bool speaking) =>
        StopSpeakingButton.Visibility = speaking ? Visibility.Visible : Visibility.Collapsed;

    private void OnStopSpeakingClick(object sender, RoutedEventArgs e)
    {
        StopSpeakingButton.Visibility = Visibility.Collapsed;
        ShellApp.Instance.StopSpeaking();
    }

    private void DockRight()
    {
        Windows.Win32.PInvoke.GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + area.Width - Width, area.Y, Width, area.Height));
    }

    private void OnConversationChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    private void ScrollToEnd()
    {
        if (ShellApp.Instance.Conversation is { Count: > 0 } items)
        {
            Conversation.ScrollIntoView(items[^1]);
        }
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = SendAsync();
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => _ = SendAsync();

    private async Task SendAsync()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var conversation = ShellApp.Instance.Conversation;
        Input.Text = "";
        conversation.Add("You: " + text);
        if (ShellApp.Instance.Core is not { } core)
        {
            conversation.Add("ALDREN: I'm not connected to Core right now.");
            return;
        }

        SendButton.IsEnabled = false;
        try
        {
            conversation.Add("ALDREN: " + await core.SubmitTextAsync(text, CancellationToken.None));
        }
        catch (Exception ex)
        {
            conversation.Add("ALDREN: Something went wrong: " + ex.Message);
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }
}
