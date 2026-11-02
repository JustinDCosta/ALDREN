using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Aldren.App.Expanded;

public sealed partial class ExpandedWindow : Window
{
    private readonly ObservableCollection<string> _messages = [];

    public ExpandedWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(640, 560));
        Conversation.ItemsSource = _messages;
        WindowKeys.CloseOnEscape(this, Root);
        Root.Loaded += (_, _) => Input.Focus(FocusState.Programmatic);
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

        Input.Text = "";
        _messages.Add("You: " + text);
        if (ShellApp.Instance.Core is not { } core)
        {
            _messages.Add("ALDREN: I'm not connected to Core right now.");
            return;
        }

        SendButton.IsEnabled = false;
        try
        {
            _messages.Add("ALDREN: " + await core.SubmitTextAsync(text, CancellationToken.None));
        }
        catch (Exception ex)
        {
            _messages.Add("ALDREN: Something went wrong: " + ex.Message);
        }
        finally
        {
            SendButton.IsEnabled = true;
            Conversation.ScrollIntoView(_messages[^1]);
        }
    }
}
