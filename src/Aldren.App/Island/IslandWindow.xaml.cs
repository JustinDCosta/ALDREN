using System.Numerics;
using Aldren.App.Island.Native;
using Aldren.Common.Shell;
using Aldren.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using VirtualKey = Windows.System.VirtualKey;

namespace Aldren.App.Island;

/// <summary>
/// The island: a pre-created, hidden, topmost window that never takes focus unless you type or confirm, is click-through
/// outside the pill, and is hidden from screen capture unless "Show ALDREN on stream" is on.
/// The pill is one shape that hugs whatever it holds: XAML lays the content out, and the Composition shape (with its
/// shadow and the content's clip) springs to the content's size, so every change of state is a single smooth morph.
/// </summary>
public sealed partial class IslandWindow : Window
{
    // The window is a fixed, transparent canvas in DIPs; the pill animates inside it, so the window never resizes.
    public const int CanvasWidth = 660, CanvasHeight = 480;
    public const float PillTop = 10;

    private const double TypingWidth = 520;
    private const float MaxRadius = 26;
    private static readonly TimeSpan EmptyFieldIdle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TypedFieldIdle = TimeSpan.FromMinutes(2);

    private enum Sign
    {
        Mark,
        Spinner,
        Listening,
        Speaking,
        Glyph,
    }

    private readonly nint _hwnd;
    private readonly WindowStyles.SubclassProc _wndProc; // held so the GC never collects the native callbacks
    private readonly DispatcherQueueTimer _cursorTimer;
    private readonly DispatcherQueueTimer _idleTimer;
    private readonly DispatcherQueueTimer _confirmTimer;
    private readonly Compositor _compositor;
    private readonly Visual _rootVisual;
    private readonly CompositionRoundedRectangleGeometry _pill;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private readonly ILogger _log;
    private readonly List<string> _history = [];
    private readonly SolidColorBrush _text;
    private readonly SolidColorBrush _textSecondary;
    private int _historyIndex = -1;
    private int _morphId;
    private bool _visible;
    private bool _hiding;
    private bool _interactive;
    private bool _busy;
    private bool _voiceTurn;
    private bool? _lastWanted;
    private nint _previousForeground;
    private Vector2 _target;
    private string? _voiceReply;
    private DateTimeOffset _confirmDeadline;
    private TaskCompletionSource<bool>? _confirmation;

    public IslandWindow(ILogger log)
    {
        _log = log;
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _text = (SolidColorBrush)Root.Resources["IslandText"];
        _textSecondary = (SolidColorBrush)Root.Resources["IslandTextSecondary"];

        SystemBackdrop = new TransparentBackdrop();
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        _wndProc = WndProc;
        WindowStyles.SetWindowSubclass(_hwnd, _wndProc, 1, 0);
        WindowStyles.ApplyOverlay(_hwnd);
        TransparentBackdrop.ConfigureDwm(_hwnd);
        SetShowOnStream(false);

        _rootVisual = ElementCompositionPreview.GetElementVisual(Root);
        _compositor = _rootVisual.Compositor;
        _pill = _compositor.CreateRoundedRectangleGeometry();
        BuildPill();

        // handledEventsToo: a press on the reply text would otherwise never reach this handler.
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), handledEventsToo: true);
        Caption.IsTextTrimmedChanged += (_, _) => UpdateVoiceFooter();

        // Runs only while the island is visible: hidden means nothing ticks.
        _cursorTimer = DispatcherQueue.CreateTimer();
        _cursorTimer.Interval = TimeSpan.FromMilliseconds(33);
        _cursorTimer.Tick += (_, _) => UpdateClickThrough();

        _idleTimer = DispatcherQueue.CreateTimer();
        _idleTimer.IsRepeating = false;
        _idleTimer.Tick += (_, _) =>
        {
            if (State is not IslandState.Confirm and not IslandState.Hidden && !_busy)
            {
                Dismiss();
            }
        };

        _confirmTimer = DispatcherQueue.CreateTimer();
        _confirmTimer.Interval = TimeSpan.FromSeconds(1);
        _confirmTimer.Tick += (_, _) => UpdateConfirmDetail();

        Activated += (_, e) =>
        {
            // Clicking elsewhere while typing closes the island (a request already sent still gets its answer shown);
            // a confirmation stays until answered.
            if (e.WindowActivationState == WindowActivationState.Deactivated && _interactive && State is IslandState.Text or IslandState.Reply)
            {
                Dismiss(restoreFocus: false);
            }
        };
    }

    public IslandState State { get; private set; } = IslandState.Hidden;

    /// <summary>Submits text and returns the reply.</summary>
    public Func<string, Task<string>>? Submit { get; set; }

    public event Action? ExpandRequested;

    /// <summary>The Stop button: ALDREN goes quiet and the voice turn ends.</summary>
    public event Action? StopRequested;

    public void SetShowOnStream(bool show) => WindowStyles.SetCaptureExcluded(_hwnd, !show);

    // ------------------------------------------------------------------ typing

    /// <summary>The hotkey: opens the text field on the monitor you're working on, or closes it.</summary>
    public void ToggleText()
    {
        if (State == IslandState.Confirm)
        {
            return;
        }

        if (_interactive && State is IslandState.Text or IslandState.Reply)
        {
            Dismiss();
            return;
        }

        RememberForeground();
        ShowField(placeholder: "Ask ALDREN anything", answer: null);
        if (_busy)
        {
            // Reopened while the last request is still running: its answer lands here.
            Input.IsReadOnly = true;
            Input.PlaceholderText = "Working on your last request…";
            SetSign(Sign.Spinner);
        }

        Arrange(IslandState.Text);
        SetInteractive(true);
        Input.Focus(FocusState.Programmatic);
        RestartIdle(EmptyFieldIdle);
    }

    /// <summary>The reply to the typed request in flight, as far as the model has written it.</summary>
    public void ShowTypedProgress(string text)
    {
        if (!_busy || !_interactive || State is not (IslandState.Text or IslandState.Reply) || text.Length == 0)
        {
            return;
        }

        AnswerText.Text = text;
        AnswerText.Opacity = 1;
        AnswerPane.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Gives keyboard focus back to the window you were in, before ALDREN types or clicks there for you. The island stays
    /// up without focus; the answer still shows in it.
    /// </summary>
    public void YieldFocus()
    {
        if (!_interactive || State == IslandState.Confirm)
        {
            return;
        }

        var previous = _previousForeground;
        _previousForeground = 0;
        SetInteractive(false);
        if (previous != 0 && Focus.IsWindow(previous))
        {
            Focus.SetForegroundWindow(previous);
        }
    }

    private void ShowField(string placeholder, string? answer)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        Line.Visibility = Visibility.Visible;
        Caption.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Collapsed;
        Input.Visibility = Visibility.Visible;
        Input.IsReadOnly = false;
        Input.Foreground = _text;
        Input.PlaceholderText = placeholder;
        Input.Text = "";
        SetSign(Sign.Mark);
        if (answer is null)
        {
            AnswerPane.Visibility = Visibility.Collapsed;
            Footer.Visibility = Visibility.Collapsed;
        }
        else
        {
            AnswerText.Text = answer;
            AnswerText.Opacity = 1;
            AnswerPane.Visibility = Visibility.Visible;
            AnswerScroll.ChangeView(null, 0, null, disableAnimation: true);
            Footer.Text = "Enter sends  ·  Ctrl+Enter opens the conversation  ·  Esc closes";
            Footer.Visibility = Visibility.Visible;
        }

        UpdateFieldActions();
    }

    /// <summary>Right of the field: "esc" while it's empty, the send button once there is something to send.</summary>
    private void UpdateFieldActions()
    {
        var typing = Input.Visibility == Visibility.Visible && !_busy;
        var hasText = Input.Text.Trim().Length > 0;
        SendButton.Visibility = typing && hasText ? Visibility.Visible : Visibility.Collapsed;
        EscHint.Visibility = typing && !hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFieldActions();
        if (!_busy && State is IslandState.Text or IslandState.Reply)
        {
            RestartIdle(Input.Text.Length > 0 ? TypedFieldIdle : EmptyFieldIdle);
        }
    }

    private void OnInputPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                if (Down(VirtualKey.Control))
                {
                    Dismiss(restoreFocus: false);
                    ExpandRequested?.Invoke();
                }
                else
                {
                    _ = SendAsync(Input.Text.Trim());
                }

                break;
            case VirtualKey.Up when !_busy && _history.Count > 0 && (Input.Text.Length == 0 || _historyIndex >= 0):
                e.Handled = true;
                _historyIndex = _historyIndex < 0 ? _history.Count - 1 : Math.Max(0, _historyIndex - 1);
                Recall();
                break;
            case VirtualKey.Down when !_busy && _historyIndex >= 0:
                e.Handled = true;
                _historyIndex++;
                if (_historyIndex >= _history.Count)
                {
                    _historyIndex = -1;
                    Input.Text = "";
                }
                else
                {
                    Recall();
                }

                break;
        }

        void Recall()
        {
            Input.Text = _history[_historyIndex];
            Input.SelectionStart = Input.Text.Length;
        }
    }

    private static bool Down(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnSendClick(object sender, RoutedEventArgs e) => _ = SendAsync(Input.Text.Trim());

    private async Task SendAsync(string text)
    {
        if (text.Length == 0 || Submit is null || _busy)
        {
            return;
        }

        if (_history.Count == 0 || _history[^1] != text)
        {
            _history.Add(text);
            if (_history.Count > 30)
            {
                _history.RemoveAt(0);
            }
        }

        _historyIndex = -1;
        _busy = true;
        // The question stays in the field, dimmed, while ALDREN works on it.
        Input.IsReadOnly = true;
        Input.Foreground = _textSecondary;
        AnswerText.Opacity = 0.45;
        SetSign(Sign.Spinner);
        UpdateFieldActions();
        _idleTimer.Stop();

        string reply;
        try
        {
            reply = await Submit(text);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Submitting text failed: {Message}", ex.Message);
            reply = "Something went wrong: " + ex.Message;
        }

        _busy = false;
        if (State == IslandState.Confirm)
        {
            return; // the conversation window has the reply
        }

        if (_interactive && State is IslandState.Text or IslandState.Reply)
        {
            ShowField(placeholder: "Ask a follow-up", answer: reply);
            Arrange(IslandState.Reply);
            Input.Focus(FocusState.Programmatic);
            RestartIdle(ReadingTime(reply));
        }
        else if (State is IslandState.Hidden or IslandState.Text or IslandState.Reply)
        {
            // You clicked back into your work (or ALDREN handed focus back to act there): the answer still shows.
            ShowPassive(IslandState.Reply, Sign.Mark, reply, secondary: false);
            RestartIdle(ReadingTime(reply));
        }
    }

    // ------------------------------------------------------------------ voice, cloud, previews

    /// <summary>The voice loop's live state, without taking focus: you keep typing in your app while ALDREN listens.</summary>
    public void ShowVoice(VoiceState state, string text)
    {
        if (State == IslandState.Confirm)
        {
            return;
        }

        if (state == VoiceState.Idle)
        {
            if (_voiceTurn)
            {
                _voiceTurn = false;
                _voiceReply = null;
                Dismiss(restoreFocus: false);
            }

            return;
        }

        _voiceTurn = true;
        switch (state)
        {
            case VoiceState.Listening when _voiceReply is { } reply:
                // The follow-up window keeps the reply on screen instead of replacing it with "Anything else?".
                ShowPassive(IslandState.Listening, Sign.Listening, reply, secondary: false, footer: "Listening for a follow-up…");
                break;
            case VoiceState.Listening:
                ShowPassive(IslandState.Listening, Sign.Listening, text, secondary: true);
                break;
            case VoiceState.Transcribing:
                _voiceReply = null;
                ShowPassive(IslandState.Transcribing, Sign.Spinner, text.Trim('…', ' ').Length == 0 ? "Got it…" : text, secondary: true);
                break;
            case VoiceState.Thinking:
                _voiceReply = null;
                ShowPassive(IslandState.Thinking, Sign.Spinner, text, secondary: true);
                break;
            case VoiceState.Speaking:
                _voiceReply = text;
                ShowPassive(IslandState.Speaking, Sign.Speaking, text, secondary: false, stop: true);
                UpdateVoiceFooter();
                break;
            default:
                ShowPassive(IslandState.Error, Sign.Glyph, text, secondary: false, glyph: "", glyphColor: Color.FromArgb(255, 255, 153, 102));
                RestartIdle(ReadingTime(text));
                return;
        }

        // Every voice state stays up until Core sends the next one (the turn always ends with Idle).
        _idleTimer.Stop();
    }

    /// <summary>A long spoken reply is cut after nine lines: say where the rest is.</summary>
    private void UpdateVoiceFooter()
    {
        if (State != IslandState.Speaking)
        {
            return;
        }

        Footer.Text = "Click to read the full reply";
        Footer.Visibility = Caption.IsTextTrimmed ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The cloud indicator in the island: what is going online right now. Left alone while you type or confirm; the tray
    /// tooltip still says it then.
    /// </summary>
    public void ShowCloud(string? activity)
    {
        if (activity is null)
        {
            if (State == IslandState.Researching)
            {
                Dismiss(restoreFocus: false);
            }

            return;
        }

        if (State is IslandState.Text or IslandState.Reply or IslandState.Confirm)
        {
            return;
        }

        ShowPassive(IslandState.Researching, Sign.Glyph, $"{activity}…", secondary: false, glyph: "", footer: "Online");
        _idleTimer.Stop();
    }

    /// <summary>Shows a state with sample content, for Diagnostics.</summary>
    public void Preview(IslandState state)
    {
        if (State == IslandState.Confirm)
        {
            return;
        }

        var sample = IslandSamples.For(state);
        switch (state)
        {
            case IslandState.Listening:
                ShowPassive(state, Sign.Listening, sample, secondary: true);
                break;
            case IslandState.Transcribing or IslandState.Thinking or IslandState.Acting:
                ShowPassive(state, Sign.Spinner, sample, secondary: state != IslandState.Acting);
                break;
            case IslandState.Researching:
                ShowPassive(state, Sign.Glyph, sample, secondary: false, glyph: "", footer: "Online");
                break;
            case IslandState.Speaking:
                ShowPassive(state, Sign.Speaking, sample, secondary: false, stop: true);
                break;
            case IslandState.Reply:
                ShowPassive(state, Sign.Mark, sample, secondary: false);
                break;
            case IslandState.Error:
                ShowPassive(state, Sign.Glyph, sample, secondary: false, glyph: "", glyphColor: Color.FromArgb(255, 255, 153, 102));
                break;
            default:
                ShowPassive(state, Sign.Mark, sample, secondary: false);
                break;
        }

        RestartIdle(TimeSpan.FromSeconds(8));
    }

    /// <summary>A line of text with its sign, shown without taking focus; a click on it opens the conversation.</summary>
    private void ShowPassive(
        IslandState state, Sign sign, string caption, bool secondary, bool stop = false, string? footer = null,
        string? glyph = null, Color? glyphColor = null)
    {
        if (!_visible || _hiding)
        {
            PlaceOnForegroundMonitor();
        }

        SetInteractive(false);
        ConfirmPanel.Visibility = Visibility.Collapsed;
        Line.Visibility = Visibility.Visible;
        Input.Visibility = Visibility.Collapsed;
        AnswerPane.Visibility = Visibility.Collapsed;
        SendButton.Visibility = Visibility.Collapsed;
        EscHint.Visibility = Visibility.Collapsed;
        StopButton.Visibility = stop ? Visibility.Visible : Visibility.Collapsed;
        Caption.Text = caption;
        Caption.Foreground = secondary ? _textSecondary : _text;
        // With nothing to press on the right, the text gets the pill's own margin there.
        Caption.Margin = new Thickness(0, 14, stop ? 0 : 9, 14);
        Caption.Visibility = Visibility.Visible;
        Footer.Text = footer ?? "";
        Footer.Visibility = footer is null ? Visibility.Collapsed : Visibility.Visible;
        SetSign(sign, glyph, glyphColor);
        Arrange(state);
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        _log.LogInformation("Stop button clicked");
        StopButton.Visibility = Visibility.Collapsed;
        StopRequested?.Invoke();
    }

    /// <summary>
    /// A click on the island while it only shows something (a voice turn, an answer) opens the conversation window.
    /// While you type or confirm, clicks keep their usual meaning.
    /// </summary>
    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_interactive || State is IslandState.Hidden or IslandState.Confirm || IsInside(e.OriginalSource as DependencyObject, StopButton))
        {
            return;
        }

        _log.LogInformation("Island clicked in state {State}: opening the conversation window", State);
        ExpandRequested?.Invoke();
    }

    private static bool IsInside(DependencyObject? element, DependencyObject container)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ confirmations

    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct)
    {
        _confirmation?.TrySetResult(false);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirmation = answer;

        RememberForeground();
        _idleTimer.Stop();
        Line.Visibility = Visibility.Collapsed;
        AnswerPane.Visibility = Visibility.Collapsed;
        Footer.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = Visibility.Visible;
        ConfirmSummary.Text = request.Summary;
        (ConfirmTier.Text, ConfirmTierChip.Background) = request.Tier switch
        {
            "Destructive" => ("Can't be undone", new SolidColorBrush(Color.FromArgb(70, 255, 99, 99))),
            "Impactful" => ("Changes something", new SolidColorBrush(Color.FromArgb(60, 255, 190, 92))),
            "Reversible" => ("Easy to undo", new SolidColorBrush(Color.FromArgb(36, 255, 255, 255))),
            _ => (request.Tier, new SolidColorBrush(Color.FromArgb(36, 255, 255, 255))),
        };
        ConfirmSources.Visibility = request.UntrustedSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ConfirmSources.Text = $"Suggested while reading {string.Join(", ", request.UntrustedSources)}";
        _confirmDeadline = DateTimeOffset.UtcNow.AddSeconds(request.TimeoutSeconds);
        UpdateConfirmDetail();
        _confirmTimer.Start();
        Arrange(IslandState.Confirm);
        RunConfirmTimerBar(TimeSpan.FromSeconds(request.TimeoutSeconds));
        SetInteractive(true);
        NoButton.Focus(FocusState.Programmatic);

        var registration = ct.Register(() => DispatcherQueue.TryEnqueue(() => Answer(answer, false)));
        answer.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        return answer.Task;
    }

    private void UpdateConfirmDetail()
    {
        var left = Math.Max(0, (int)Math.Ceiling((_confirmDeadline - DateTimeOffset.UtcNow).TotalSeconds));
        ConfirmDetail.Text = $"Answers No by itself in {left} s";
    }

    /// <summary>The thin line under the prompt shrinks to nothing over the time you have to answer.</summary>
    private void RunConfirmTimerBar(TimeSpan duration)
    {
        var bar = ElementCompositionPreview.GetElementVisual(ConfirmTimer);
        bar.StopAnimation("Scale");
        bar.CenterPoint = Vector3.Zero;
        bar.Scale = Vector3.One;
        if (!_uiSettings.AnimationsEnabled)
        {
            return;
        }

        var shrink = _compositor.CreateVector3KeyFrameAnimation();
        shrink.InsertKeyFrame(0f, Vector3.One);
        shrink.InsertKeyFrame(1f, new Vector3(0, 1, 1), _compositor.CreateLinearEasingFunction());
        shrink.Duration = duration;
        bar.StartAnimation("Scale", shrink);
    }

    private void Answer(TaskCompletionSource<bool> answer, bool yes)
    {
        if (!answer.TrySetResult(yes))
        {
            return;
        }

        if (ReferenceEquals(_confirmation, answer))
        {
            _confirmation = null;
            _confirmTimer.Stop();
            ConfirmPanel.Visibility = Visibility.Collapsed;
            Dismiss();
        }
    }

    private void OnYesClick(object sender, RoutedEventArgs e)
    {
        if (_confirmation is { } answer)
        {
            Answer(answer, true);
        }
    }

    private void OnNoClick(object sender, RoutedEventArgs e)
    {
        if (_confirmation is { } answer)
        {
            Answer(answer, false);
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (State == IslandState.Confirm && _confirmation is { } answer)
        {
            switch (e.Key)
            {
                case VirtualKey.Y:
                    e.Handled = true;
                    Answer(answer, true);
                    break;
                case VirtualKey.N or VirtualKey.Escape:
                    e.Handled = true;
                    Answer(answer, false);
                    break;
            }
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Dismiss();
        }
    }

    // ------------------------------------------------------------------ showing, hiding, morphing

    public void Dismiss(bool restoreFocus = true)
    {
        _idleTimer.Stop();
        var previous = _previousForeground;
        _previousForeground = 0;
        SetInteractive(false);
        Hide();
        if (restoreFocus && previous != 0 && Focus.IsWindow(previous))
        {
            Focus.SetForegroundWindow(previous);
        }
    }

    /// <summary>Before taking focus: note the window you're in (to hand focus back) and move to its monitor.</summary>
    private void RememberForeground()
    {
        if (_interactive)
        {
            return;
        }

        var foreground = Focus.GetForegroundWindow();
        _previousForeground = foreground == _hwnd ? 0 : foreground;
        PlaceOnForegroundMonitor();
    }

    private void SetInteractive(bool on)
    {
        if (_interactive == on)
        {
            return;
        }

        _interactive = on;
        Focus.SetNoActivate(_hwnd, !on);
        if (on)
        {
            Focus.SetForegroundWindow(_hwnd);
            Activate();
        }
    }

    /// <summary>The content is set: take the new state, and show the island if it isn't up.</summary>
    private void Arrange(IslandState state)
    {
        State = state;
        // The field has a fixed width; everything else is as wide as its text, up to the panel's maximum.
        Panel.Width = Input.Visibility == Visibility.Visible ? TypingWidth : double.NaN;
        if (!_visible)
        {
            Reveal();
        }
        else if (_hiding)
        {
            // Asked back while fading out: stay, at full opacity, and take the new content's size.
            _hiding = false;
            _morphId++;
            Fade(to: 1, TimeSpan.FromMilliseconds(90));
            Panel.UpdateLayout();
            MorphTo(PanelSize());
        }
    }

    private Vector2 PanelSize() =>
        Panel.ActualWidth >= 1 && Panel.ActualHeight >= 1 ? new Vector2((float)Panel.ActualWidth, (float)Panel.ActualHeight) : new Vector2(260, 48);

    private void Reveal()
    {
        Panel.UpdateLayout();
        var size = PanelSize();
        _morphId++;
        _hiding = false;
        _pill.StopAnimation("Size");
        _pill.StopAnimation("CornerRadius");
        _rootVisual.StopAnimation("Opacity");
        if (_uiSettings.AnimationsEnabled)
        {
            // A small pop: starts a little smaller and transparent, springs to size while it fades in.
            _pill.Size = new Vector2(size.X * 0.9f, Math.Max(24, size.Y * 0.86f));
            _pill.CornerRadius = new Vector2(Math.Min(_pill.Size.Y / 2, MaxRadius));
            _rootVisual.Opacity = 0;
        }

        WindowStyles.ShowNoActivate(_hwnd);
        _visible = true;
        MorphTo(size);
        Fade(to: 1, TimeSpan.FromMilliseconds(140));
        UpdateClickThrough();
        _cursorTimer.Start();
    }

    private void Hide()
    {
        var was = State;
        State = IslandState.Hidden;
        StopSigns();
        _confirmTimer.Stop();
        var id = ++_morphId;
        if (!_visible || was == IslandState.Hidden && _hiding)
        {
            return;
        }

        if (!_uiSettings.AnimationsEnabled)
        {
            FinishHide(id);
            return;
        }

        _hiding = true;
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        Fade(to: 0, TimeSpan.FromMilliseconds(150));
        batch.End();
        batch.Completed += (_, _) => FinishHide(id);
        // Outside the batch: a spring settles long after the fade is over.
        _pill.StartAnimation("Size", Spring(new Vector2(_target.X * 0.9f, Math.Max(24, _target.Y * 0.86f))));
    }

    private void FinishHide(int id)
    {
        if (id != _morphId || State != IslandState.Hidden)
        {
            return; // asked back in the meantime
        }

        _cursorTimer.Stop();
        WindowStyles.Hide(_hwnd);
        _visible = false;
        _hiding = false;
        Input.Text = "";
        AnswerText.Text = "";
        Caption.Text = "";
    }

    private void Fade(float to, TimeSpan duration)
    {
        if (!_uiSettings.AnimationsEnabled)
        {
            _rootVisual.Opacity = to;
            return;
        }

        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, to, _compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f)));
        fade.Duration = duration;
        _rootVisual.StartAnimation("Opacity", fade);
    }

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_visible && !_hiding && State != IslandState.Hidden)
        {
            MorphTo(new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height));
        }
    }

    private void MorphTo(Vector2 size)
    {
        _target = size;
        var radius = new Vector2(Math.Min(size.Y / 2, MaxRadius));
        if (!_uiSettings.AnimationsEnabled)
        {
            // Windows' "Animation effects" is off: no morphs.
            _pill.Size = size;
            _pill.CornerRadius = radius;
            return;
        }

        _pill.StartAnimation("Size", Spring(size));
        _pill.StartAnimation("CornerRadius", Spring(radius));
    }

    private SpringVector2NaturalMotionAnimation Spring(Vector2 to)
    {
        var spring = _compositor.CreateSpringVector2Animation();
        spring.FinalValue = to;
        spring.DampingRatio = 0.82f;
        spring.Period = TimeSpan.FromMilliseconds(48);
        return spring;
    }

    /// <summary>The pill, its soft shadow, and the clip that keeps the content inside the pill while it morphs.</summary>
    private void BuildPill()
    {
        var canvas = new Vector2(CanvasWidth, CanvasHeight);
        var container = _compositor.CreateContainerVisual();
        container.Size = canvas;

        // The shadow takes its shape from an off-screen copy of the pill, so it follows every morph.
        var maskShape = _compositor.CreateSpriteShape(_pill);
        maskShape.FillBrush = _compositor.CreateColorBrush(Color.FromArgb(255, 255, 255, 255));
        var maskVisual = _compositor.CreateShapeVisual();
        maskVisual.Size = canvas;
        maskVisual.Shapes.Add(maskShape);
        var surface = _compositor.CreateVisualSurface();
        surface.SourceVisual = maskVisual;
        surface.SourceSize = canvas;
        var shadow = _compositor.CreateDropShadow();
        shadow.Mask = _compositor.CreateSurfaceBrush(surface);
        shadow.BlurRadius = 26;
        shadow.Offset = new Vector3(0, 7, 0);
        shadow.Color = Color.FromArgb(150, 0, 0, 0);
        var shadowVisual = _compositor.CreateSpriteVisual();
        shadowVisual.Size = canvas;
        shadowVisual.Shadow = shadow;
        container.Children.InsertAtBottom(shadowVisual);

        var shape = _compositor.CreateSpriteShape(_pill);
        shape.FillBrush = _compositor.CreateColorBrush(Color.FromArgb(255, 13, 14, 17));
        shape.StrokeBrush = _compositor.CreateColorBrush(Color.FromArgb(38, 255, 255, 255));
        shape.StrokeThickness = 1;
        var pillVisual = _compositor.CreateShapeVisual();
        pillVisual.Size = canvas;
        pillVisual.Shapes.Add(shape);
        container.Children.InsertAtTop(pillVisual);
        ElementCompositionPreview.SetElementChildVisual(PillHost, container);

        // Keeps the pill horizontally centred while its size springs.
        var centre = _compositor.CreateExpressionAnimation("Vector2((width - pill.Size.X) / 2, top)");
        centre.SetScalarParameter("width", CanvasWidth);
        centre.SetScalarParameter("top", PillTop);
        centre.SetReferenceParameter("pill", _pill);
        _pill.StartAnimation("Offset", centre);

        Panel.Margin = new Thickness(0, PillTop, 0, 0);
        ElementCompositionPreview.GetElementVisual(ClipHost).Clip = _compositor.CreateGeometricClip(_pill);
    }

    // ------------------------------------------------------------------ the sign on the left

    private void SetSign(Sign sign, string? glyph = null, Color? glyphColor = null)
    {
        StopSigns();
        Mark.Visibility = sign == Sign.Mark ? Visibility.Visible : Visibility.Collapsed;
        Spinner.Visibility = sign == Sign.Spinner ? Visibility.Visible : Visibility.Collapsed;
        Spinner.IsActive = sign == Sign.Spinner;
        Bars.Visibility = sign is Sign.Listening or Sign.Speaking ? Visibility.Visible : Visibility.Collapsed;
        Glyph.Visibility = sign == Sign.Glyph ? Visibility.Visible : Visibility.Collapsed;
        if (sign == Sign.Glyph)
        {
            Glyph.Glyph = glyph ?? "";
            Glyph.Foreground = glyphColor is { } colour ? new SolidColorBrush(colour) : (Brush)Application.Current.Resources["AldrenBrandBrush"];
        }

        if (sign is Sign.Listening or Sign.Speaking)
        {
            StartBars(listening: sign == Sign.Listening);
        }
    }

    /// <summary>Four bars: quick and uneven while ALDREN listens, slower and rolling while it speaks.</summary>
    private void StartBars(bool listening)
    {
        Microsoft.UI.Xaml.Shapes.Rectangle[] bars = [Bar1, Bar2, Bar3, Bar4];
        int[] periods = listening ? [520, 430, 610, 470] : [920, 780, 860, 720];
        float[] lows = listening ? [0.32f, 0.22f, 0.4f, 0.28f] : [0.45f, 0.3f, 0.5f, 0.38f];
        float[] rest = [0.55f, 0.9f, 0.7f, 0.45f];
        var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
        for (var i = 0; i < bars.Length; i++)
        {
            var visual = ElementCompositionPreview.GetElementVisual(bars[i]);
            visual.CenterPoint = new Vector3(1.5f, 8f, 0);
            if (!_uiSettings.AnimationsEnabled)
            {
                visual.Scale = new Vector3(1, rest[i], 1);
                continue;
            }

            var wave = _compositor.CreateVector3KeyFrameAnimation();
            wave.InsertKeyFrame(0f, new Vector3(1, lows[i], 1));
            wave.InsertKeyFrame(0.5f, Vector3.One, ease);
            wave.InsertKeyFrame(1f, new Vector3(1, lows[i], 1), ease);
            wave.Duration = TimeSpan.FromMilliseconds(periods[i]);
            wave.DelayTime = TimeSpan.FromMilliseconds(i * 70);
            wave.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale", wave);
        }
    }

    private void StopSigns()
    {
        foreach (var bar in (Microsoft.UI.Xaml.Shapes.Rectangle[])[Bar1, Bar2, Bar3, Bar4])
        {
            ElementCompositionPreview.GetElementVisual(bar).StopAnimation("Scale");
        }

        Spinner.IsActive = false;
    }

    // ------------------------------------------------------------------ timing, placement, click-through

    /// <summary>How long an answer stays without being touched: longer for longer text; hovering holds it.</summary>
    private static TimeSpan ReadingTime(string text) => TimeSpan.FromSeconds(Math.Clamp(6 + text.Length * 0.06, 10, 90));

    private void RestartIdle(TimeSpan after)
    {
        _idleTimer.Stop();
        _idleTimer.Interval = after;
        _idleTimer.Start();
    }

    /// <summary>Top centre of the work area of the monitor you're working on, sized for its DPI.</summary>
    private void PlaceOnForegroundMonitor()
    {
        var monitor = Focus.ForegroundMonitor();
        var work = new RectInt(monitor.WorkArea.Left, monitor.WorkArea.Top, monitor.WorkArea.Width, monitor.WorkArea.Height);

        // Two passes: moving onto a monitor with another DPI makes WinUI rescale the window, so the second pass uses
        // the DPI the window actually ended up with.
        for (var pass = 0; pass < 2; pass++)
        {
            var scale = (pass == 0 ? monitor.Dpi : WindowStyles.GetDpiForWindow(_hwnd)) / 96.0;
            var r = IslandPlacement.Place(work, scale, CanvasWidth, CanvasHeight, topMarginDip: 0);
            AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
        }
    }

    /// <summary>Screen rectangle (physical pixels) of the pill once the current morph settles.</summary>
    private RECT PillRect()
    {
        var scale = WindowStyles.GetDpiForWindow(_hwnd) / 96.0;
        var window = WindowStyles.WindowRect(_hwnd);
        var left = window.Left + (int)Math.Round((CanvasWidth - _target.X) / 2 * scale);
        var top = window.Top + (int)Math.Round(PillTop * scale);
        return new RECT(left, top, left + (int)Math.Round(_target.X * scale), top + (int)Math.Round(_target.Y * scale));
    }

    // Compares against the window's real style rather than a cached flag, so a style reset by WinUI gets repaired.
    private void UpdateClickThrough()
    {
        var inside = State != IslandState.Hidden && WindowStyles.GetCursorPos(out var p) && PillRect().Contains(p.X, p.Y);
        var wanted = !inside;
        if (WindowStyles.IsClickThrough(_hwnd) != wanted || _lastWanted != wanted)
        {
            WindowStyles.SetClickThrough(_hwnd, wanted);
        }

        _lastWanted = wanted;

        // The pointer resting on an answer means you're reading it: it stays.
        if (inside && _idleTimer.IsRunning && State is IslandState.Reply or IslandState.Error)
        {
            _idleTimer.Stop();
            _idleTimer.Start();
        }
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        switch (msg)
        {
            case WindowStyles.WM_NCCALCSIZE when wParam != 0:
                return 0; // the whole window is client area: no frame for anyone to paint
            case WindowStyles.WM_ERASEBKGND:
                TransparentBackdrop.EraseBackground(hwnd, wParam);
                return 1;
            case WindowStyles.WM_DWMCOMPOSITIONCHANGED:
                TransparentBackdrop.ConfigureDwm(hwnd);
                break;
            case WindowStyles.WM_MOUSEACTIVATE when !_interactive:
                return WindowStyles.MA_NOACTIVATE;
        }

        return WindowStyles.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
