using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Aldren.App.Expanded;

/// <summary>
/// The conversation sidebar: docked to the right edge of the screen you're working on, full height, with every typed and
/// spoken turn of this session. Opened from the tray, with Ctrl+Enter in the text field, or by clicking an answer in the island.
/// </summary>
public sealed partial class ExpandedWindow : Window
{
    private const int WidthDip = 440;

    // Follow new text only while you're at the bottom; scrolled up to read, the view stays where you put it.
    private bool _stickToEnd = true;

    public ExpandedWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this, Root, TitleBar);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        WindowChrome.DockRight(this, WindowChrome.CurrentMonitor(), WidthDip);

        var conversation = ShellApp.Instance.Conversation;
        Messages.ItemsSource = conversation;
        conversation.CollectionChanged += OnConversationChanged;
        ShowEmptyState();
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

    private void ShowEmptyState()
    {
        var empty = ShellApp.Instance.Conversation.Count == 0;
        Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        NewButton.IsEnabled = !empty;
    }

    private void OnSpeakingChanged(bool speaking) =>
        StopSpeakingButton.Visibility = speaking ? Visibility.Visible : Visibility.Collapsed;

    private void OnStopSpeakingClick(object sender, RoutedEventArgs e)
    {
        StopSpeakingButton.Visibility = Visibility.Collapsed;
        ShellApp.Instance.StopSpeaking();
    }

    private void OnConversationChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ShowEmptyState();
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // A new turn always comes into view, wherever you had scrolled to.
            _stickToEnd = true;
        }
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) =>
        _stickToEnd = Scroll.VerticalOffset >= Scroll.ScrollableHeight - 48;

    // New turns and text still being written both change the list's height.
    private void OnMessagesSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_stickToEnd)
        {
            ScrollToEnd();
        }
    }

    private void ScrollToEnd() =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            Scroll.UpdateLayout();
            Scroll.ChangeView(null, Scroll.ScrollableHeight, null, disableAnimation: true);
        });

    private void OnInputTextChanged(object sender, TextChangedEventArgs e) => SendButton.IsEnabled = Input.Text.Trim().Length > 0;

    private void OnInputPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Send(Input.Text);
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => Send(Input.Text);

    private void OnSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string suggestion })
        {
            Send(suggestion);
        }
    }

    private void Send(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Input.Text = "";
        Input.Focus(FocusState.Programmatic);
        // The reply lands in the conversation (and fills in while it is written); nothing to wait for here.
        _ = ShellApp.Instance.AskAsync(text);
    }

    private void OnMicClick(object sender, RoutedEventArgs e)
    {
        if (ShellApp.Instance.Core is { } core)
        {
            _ = core.PushToTalkAsync(CancellationToken.None);
        }
    }

    private void OnNewClick(object sender, RoutedEventArgs e) => _ = ShellApp.Instance.NewConversationAsync();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShellApp.Instance.ShowSettings();
}
