using System.Numerics;
using Aldren.App.Island.Native;
using Aldren.Common.Shell;
using Aldren.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI;

namespace Aldren.App.Island;

/// <summary>
/// The island, ported from the S2 spike: a pre-created, hidden, topmost window that never takes focus unless you type
/// or confirm, is click-through outside the pill, and is hidden from screen capture unless "Show ALDREN on stream" is on.
/// </summary>
public sealed partial class IslandWindow : Window
{
    // The window is a fixed canvas in DIPs; the pill animates inside it, so the window never resizes mid-morph.
    public const int CanvasWidth = 600, CanvasHeight = 260;
    public const float PillTop = 8;

    public static readonly TimeSpan IdleCollapse = TimeSpan.FromSeconds(8);

    private readonly nint _hwnd;
    private readonly WindowStyles.SubclassProc _wndProc; // held so the GC never collects the native callbacks
    private readonly DispatcherQueueTimer _cursorTimer;
    private readonly DispatcherQueueTimer _idleTimer;
    private readonly Compositor _compositor;
    private readonly CompositionRoundedRectangleGeometry _pill;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private readonly ILogger _log;
    private int _morphId;
    private bool _visible;
    private bool _interactive;
    private bool? _lastWanted;
    private nint _previousForeground;
    private TaskCompletionSource<bool>? _confirmation;

    public IslandWindow(ILogger log)
    {
        _log = log;
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

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

        _compositor = ElementCompositionPreview.GetElementVisual(PillHost).Compositor;
        _pill = _compositor.CreateRoundedRectangleGeometry();
        var shape = _compositor.CreateSpriteShape(_pill);
        shape.FillBrush = _compositor.CreateColorBrush(Color.FromArgb(255, 15, 15, 15));
        shape.StrokeBrush = _compositor.CreateColorBrush(Color.FromArgb(40, 255, 255, 255));
        shape.StrokeThickness = 1;
        var visual = _compositor.CreateShapeVisual();
        visual.Size = new Vector2(CanvasWidth, CanvasHeight);
        visual.Shapes.Add(shape);
        ElementCompositionPreview.SetElementChildVisual(PillHost, visual);

        // Keeps the pill horizontally centred while its size springs.
        var centre = _compositor.CreateExpressionAnimation("Vector2((width - pill.Size.X) / 2, top)");
        centre.SetScalarParameter("width", CanvasWidth);
        centre.SetScalarParameter("top", PillTop);
        centre.SetReferenceParameter("pill", _pill);
        _pill.StartAnimation("Offset", centre);

        Panel.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
        Panel.Opacity = 0;

        // Runs only while the island is visible: hidden means nothing ticks.
        _cursorTimer = DispatcherQueue.CreateTimer();
        _cursorTimer.Interval = TimeSpan.FromMilliseconds(33);
        _cursorTimer.Tick += (_, _) => UpdateClickThrough();

        _idleTimer = DispatcherQueue.CreateTimer();
        _idleTimer.Interval = IdleCollapse;
        _idleTimer.IsRepeating = false;
        _idleTimer.Tick += (_, _) =>
        {
            if (State is not IslandState.Confirm and not IslandState.Hidden)
            {
                Dismiss();
            }
        };

        Activated += (_, e) =>
        {
            // Clicking elsewhere while typing closes the island; a confirmation stays until answered.
            if (e.WindowActivationState == WindowActivationState.Deactivated && State is IslandState.Text or IslandState.Reply)
            {
                Dismiss(restoreFocus: false);
            }
        };
    }

    public IslandState State { get; private set; } = IslandState.Hidden;

    /// <summary>Submits text and returns the reply.</summary>
    public Func<string, Task<string>>? Submit { get; set; }

    public event Action? ExpandRequested;

    public void SetShowOnStream(bool show) => WindowStyles.SetCaptureExcluded(_hwnd, !show);

    /// <summary>The hotkey: opens the text box on the monitor you're working on, or closes it.</summary>
    public void ToggleText()
    {
        if (State is IslandState.Text or IslandState.Reply)
        {
            Dismiss();
            return;
        }

        if (State == IslandState.Confirm)
        {
            return;
        }

        Open(IslandState.Text);
        Caption.Text = "";
        Input.Text = "";
        Input.Visibility = Visibility.Visible;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        Input.Focus(FocusState.Programmatic);
        RestartIdle();
    }

    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct)
    {
        _confirmation?.TrySetResult(false);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirmation = answer;

        Open(IslandState.Confirm);
        _idleTimer.Stop();
        Caption.Text = "";
        Input.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = Visibility.Visible;
        ConfirmSummary.Text = request.Summary;
        ConfirmTier.Text = $"Risk: {request.Tier} · {request.Tool} · declined automatically after {request.TimeoutSeconds} s";
        ConfirmSources.Visibility = request.UntrustedSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ConfirmSources.Text = $"Suggested while reading {string.Join(", ", request.UntrustedSources)}";
        NoButton.Focus(FocusState.Programmatic);

        var registration = ct.Register(() => DispatcherQueue.TryEnqueue(() => Answer(answer, false)));
        answer.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        return answer.Task;
    }

    /// <summary>Shows a state with sample content, for Diagnostics.</summary>
    public void Preview(IslandState state)
    {
        if (State == IslandState.Confirm)
        {
            return;
        }

        PlaceOnForegroundMonitor();
        SetInteractive(false);
        Input.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        Caption.Text = IslandShapes.Sample(state);
        SetState(state);
        RestartIdle();
    }

    public void Dismiss(bool restoreFocus = true)
    {
        _idleTimer.Stop();
        var previous = _previousForeground;
        _previousForeground = 0;
        SetInteractive(false);
        SetState(IslandState.Hidden);
        if (restoreFocus && previous != 0 && Focus.IsWindow(previous))
        {
            Focus.SetForegroundWindow(previous);
        }
    }

    private void Open(IslandState state)
    {
        if (!_interactive)
        {
            var foreground = Focus.GetForegroundWindow();
            _previousForeground = foreground == _hwnd ? 0 : foreground;
            PlaceOnForegroundMonitor();
        }

        SetState(state);
        SetInteractive(true);
    }

    private void SetInteractive(bool on)
    {
        _interactive = on;
        Focus.SetNoActivate(_hwnd, !on);
        if (on)
        {
            Focus.SetForegroundWindow(_hwnd);
            Activate();
        }
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
            ConfirmPanel.Visibility = Visibility.Collapsed;
            Dismiss();
        }
    }

    private void OnYesClick(object sender, RoutedEventArgs e)
    {
        if (_confirmation is { } answer) Answer(answer, true);
    }

    private void OnNoClick(object sender, RoutedEventArgs e)
    {
        if (_confirmation is { } answer) Answer(answer, false);
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

    private void OnInputTextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e) => RestartIdle();

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        RestartIdle();
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl)
        {
            Dismiss(restoreFocus: false);
            ExpandRequested?.Invoke();
            return;
        }

        _ = SendAsync(Input.Text.Trim());
    }

    private async Task SendAsync(string text)
    {
        if (text.Length == 0 || Submit is null)
        {
            return;
        }

        Input.Text = "";
        Caption.Text = "Thinking…";
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

        if (State is IslandState.Text or IslandState.Reply)
        {
            Caption.Text = reply;
            SetState(IslandState.Reply);
            Input.Focus(FocusState.Programmatic);
            RestartIdle();
        }
    }

    private void RestartIdle()
    {
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void SetState(IslandState state)
    {
        State = state;
        var (size, radius) = IslandShapes.For(state);
        if (state != IslandState.Hidden && !_visible)
        {
            WindowStyles.ShowNoActivate(_hwnd);
            _visible = true;
            UpdateClickThrough();
            _cursorTimer.Start();
        }

        LayoutPanel(size, state);
        var id = ++_morphId;
        if (!_uiSettings.AnimationsEnabled)
        {
            // Windows' "Animation effects" is off: no morphs.
            _pill.Size = size;
            _pill.CornerRadius = new Vector2(radius);
            Settle(id);
            return;
        }

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _pill.StartAnimation("Size", Spring(size));
        _pill.StartAnimation("CornerRadius", Spring(new Vector2(radius)));
        batch.End();
        batch.Completed += (_, _) => Settle(id);
    }

    private void Settle(int id)
    {
        if (id != _morphId)
        {
            return; // a newer morph took over
        }

        if (State == IslandState.Hidden)
        {
            _cursorTimer.Stop();
            WindowStyles.Hide(_hwnd);
            _visible = false;
        }
    }

    private void LayoutPanel(Vector2 size, IslandState state)
    {
        if (state == IslandState.Hidden)
        {
            Panel.Opacity = 0;
            return;
        }

        Panel.Width = Math.Max(0, size.X - 40);
        Panel.Height = Math.Max(0, size.Y - 20);
        Panel.Margin = new Thickness(0, PillTop + 10, 0, 0);
        Caption.Visibility = Caption.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Caption.TextAlignment = state == IslandState.Reply ? Microsoft.UI.Xaml.TextAlignment.Left : Microsoft.UI.Xaml.TextAlignment.Center;
        Panel.Opacity = 1;
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
        var size = IslandShapes.For(State).Size;
        var scale = WindowStyles.GetDpiForWindow(_hwnd) / 96.0;
        var window = WindowStyles.WindowRect(_hwnd);
        var left = window.Left + (int)Math.Round((CanvasWidth - size.X) / 2 * scale);
        var top = window.Top + (int)Math.Round(PillTop * scale);
        return new RECT(left, top, left + (int)Math.Round(size.X * scale), top + (int)Math.Round(size.Y * scale));
    }

    private SpringVector2NaturalMotionAnimation Spring(Vector2 to)
    {
        var spring = _compositor.CreateSpringVector2Animation();
        spring.FinalValue = to;
        spring.DampingRatio = 0.78f;
        spring.Period = TimeSpan.FromMilliseconds(45);
        return spring;
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
