using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using S2.Island.Native;
using Windows.Graphics;
using Windows.UI;

namespace S2.Island;

public enum IslandState { Hidden, Compact, Listening, Expanded }

public sealed partial class IslandWindow : Window
{
    // The window is a fixed canvas in DIPs; the pill animates inside it, so the window never resizes mid-morph.
    public const float CanvasWidth = 480, CanvasHeight = 200, PillTop = 10;

    private const int HotkeyCycle = 1, HotkeyCapture = 2, HotkeyMonitor = 3, HotkeyQuit = 4;
    private const uint VkSpace = 0x20, VkC = 0x43, VkM = 0x4D, VkQ = 0x51;

    private static readonly Dictionary<IslandState, (Vector2 Size, float Radius, string Text)> Shapes = new()
    {
        [IslandState.Hidden] = (Vector2.Zero, 0, ""),
        [IslandState.Compact] = (new Vector2(200, 38), 19, "ALDREN"),
        [IslandState.Listening] = (new Vector2(300, 52), 26, "Listening…"),
        [IslandState.Expanded] = (new Vector2(420, 150), 30, "Reminder set for 19:00.\nDinner with Sam at 21:00."),
    };

    private readonly nint _hwnd;
    private readonly bool _hitTestMode;
    private readonly WindowStyles.SubclassProc _wndProc; // held so the GC never collects the native callbacks
    private readonly WindowStyles.SubclassProc _childProc;
    private readonly DispatcherQueueTimer _cursorTimer;
    private readonly Compositor _compositor;
    private readonly CompositionRoundedRectangleGeometry _pill;
    private int _morphId;
    private bool _visible;
    private long _hotkeyAt;

    public IslandWindow(bool hitTestMode)
    {
        _hitTestMode = hitTestMode;
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

        // WinUI renders into a child "bridge" window, which is where mouse input and hit tests arrive.
        _childProc = ChildProc;
        foreach (var child in WindowStyles.Children(_hwnd))
        {
            WindowStyles.SetWindowSubclass(child, _childProc, 2, 0);
            ChildWindows++;
        }

        SetCaptureExcluded(true);

        // Ctrl+Alt+Space may already belong to another app (the Claude desktop app uses it), hence a fallback chord.
        const uint chord = WindowStyles.MOD_CONTROL | WindowStyles.MOD_ALT;
        if (WindowStyles.IsHotkeyRegistered(_hwnd, HotkeyCycle, chord, VkSpace))
        {
            HotkeyChord = "Ctrl+Alt+Space";
        }
        else if (WindowStyles.IsHotkeyRegistered(_hwnd, HotkeyCycle, chord | WindowStyles.MOD_SHIFT, VkSpace))
        {
            HotkeyChord = "Ctrl+Alt+Shift+Space";
        }

        WindowStyles.IsHotkeyRegistered(_hwnd, HotkeyCapture, chord, VkC);
        WindowStyles.IsHotkeyRegistered(_hwnd, HotkeyMonitor, chord, VkM);
        WindowStyles.IsHotkeyRegistered(_hwnd, HotkeyQuit, chord, VkQ);

        _compositor = ElementCompositionPreview.GetElementVisual(PillHost).Compositor;
        _pill = _compositor.CreateRoundedRectangleGeometry();
        var shape = _compositor.CreateSpriteShape(_pill);
        shape.FillBrush = _compositor.CreateColorBrush(Color.FromArgb(255, 15, 15, 15));
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

        Caption.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
        Caption.Opacity = 0;

        _cursorTimer = DispatcherQueue.CreateTimer();
        _cursorTimer.Interval = TimeSpan.FromMilliseconds(33);
        _cursorTimer.Tick += (_, _) => UpdateClickThrough();
        if (!hitTestMode)
        {
            UpdateClickThrough();
            _cursorTimer.Start();
        }

        PlaceOnMonitor(0);
        Closed += (_, _) =>
        {
            foreach (var id in new[] { HotkeyCycle, HotkeyCapture, HotkeyMonitor, HotkeyQuit })
            {
                WindowStyles.UnregisterHotKey(_hwnd, id);
            }
        };
    }

    public IslandState State { get; private set; } = IslandState.Hidden;
    public int MonitorIndex { get; private set; }
    public bool CaptureExcluded { get; private set; }
    public string? HotkeyChord { get; }
    public bool HotkeysRegistered => HotkeyChord is not null;
    public int PillClicks { get; private set; }
    public int ChildWindows { get; }
    public nint Handle => _hwnd;

    /// <summary>Mouse-button messages that reached the island's own windows (top-level or WinUI's child).</summary>
    public int MouseDownMessages { get; private set; }

    /// <summary>How often the click-through style had to be put back because something else changed it.</summary>
    public int ClickThroughRepairs { get; private set; }

    public double LastHotkeyToFrameMs { get; private set; }

    public string Mode => _hitTestMode
        ? $"WM_NCHITTEST returns HTTRANSPARENT outside the pill (window + {ChildWindows} child windows)"
        : "WS_EX_LAYERED | WS_EX_TRANSPARENT, toggled from the cursor position every 33 ms";

    /// <summary>Raised when the springs of the latest morph have settled.</summary>
    public event Action? MorphCompleted;

    /// <summary>Raised with a Stopwatch timestamp on the first frame rendered after the hotkey made the island appear.</summary>
    public event Action<long>? FirstFrameAfterShow;

    public void SetState(IslandState state)
    {
        State = state;
        var (size, radius, text) = Shapes[state];
        if (state != IslandState.Hidden && !_visible)
        {
            WindowStyles.ShowNoActivate(_hwnd);
            _visible = true;
        }

        Caption.Opacity = 0;
        var id = ++_morphId;
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _pill.StartAnimation("Size", Spring(size));
        _pill.StartAnimation("CornerRadius", Spring(new Vector2(radius)));
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (id != _morphId)
            {
                return; // a newer morph took over
            }

            if (State == IslandState.Hidden)
            {
                WindowStyles.Hide(_hwnd);
                _visible = false;
            }
            else
            {
                Caption.Text = text;
                Caption.Width = Math.Max(0, size.X - 32);
                Caption.Margin = new Thickness(0, PillTop + (state == IslandState.Expanded ? 20 : (size.Y - 20) / 2), 0, 0);
                Caption.Opacity = 1;
            }

            MorphCompleted?.Invoke();
        };
    }

    public Task MorphAsync(IslandState state)
    {
        var done = new TaskCompletionSource();
        void Completed()
        {
            MorphCompleted -= Completed;
            done.TrySetResult();
        }

        MorphCompleted += Completed;
        SetState(state);
        return done.Task;
    }

    public void SetCaptureExcluded(bool excluded)
    {
        CaptureExcluded = excluded;
        WindowStyles.SetCaptureExcluded(_hwnd, excluded);
    }

    /// <summary>Centres the canvas at the top of a monitor, sized for that monitor's DPI.</summary>
    public Native.Monitor PlaceOnMonitor(int index)
    {
        var monitors = Monitors.All();
        MonitorIndex = ((index % monitors.Count) + monitors.Count) % monitors.Count;
        var monitor = monitors[MonitorIndex];

        // Two passes: moving onto a monitor with another DPI makes WinUI rescale the window, so the second pass uses
        // the DPI the window actually ended up with.
        for (var pass = 0; pass < 2; pass++)
        {
            var scale = (pass == 0 ? monitor.Dpi : WindowStyles.GetDpiForWindow(_hwnd)) / 96.0;
            var width = (int)Math.Round(CanvasWidth * scale);
            var height = (int)Math.Round(CanvasHeight * scale);
            AppWindow.MoveAndResize(new RectInt32(monitor.Bounds.Left + (monitor.Bounds.Width - width) / 2, monitor.Bounds.Top, width, height));
        }

        return monitor;
    }

    /// <summary>Screen rectangle (physical pixels) of the pill once the current morph settles.</summary>
    public RECT PillRect()
    {
        var size = Shapes[State].Size;
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
        if (WindowStyles.IsClickThrough(_hwnd) != wanted)
        {
            if (_lastWanted == wanted)
            {
                ClickThroughRepairs++; // we had set it this way and something else changed it
            }

            WindowStyles.SetClickThrough(_hwnd, wanted);
        }

        _lastWanted = wanted;
    }

    private bool? _lastWanted;

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case HotkeyCycle:
                if (State == IslandState.Hidden)
                {
                    _hotkeyAt = Stopwatch.GetTimestamp();
                    CompositionTarget.Rendering += OnFirstFrame;
                }

                SetState(State switch
                {
                    IslandState.Hidden => IslandState.Compact,
                    IslandState.Compact => IslandState.Listening,
                    IslandState.Listening => IslandState.Expanded,
                    _ => IslandState.Hidden,
                });
                break;
            case HotkeyCapture:
                SetCaptureExcluded(!CaptureExcluded);
                Caption.Text = CaptureExcluded ? "Hidden from capture" : "Visible to capture";
                break;
            case HotkeyMonitor:
                PlaceOnMonitor(MonitorIndex + 1);
                break;
            case HotkeyQuit:
                Application.Current.Exit();
                break;
        }
    }

    private void OnFirstFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFirstFrame;
        var now = Stopwatch.GetTimestamp();
        LastHotkeyToFrameMs = Stopwatch.GetElapsedTime(_hotkeyAt, now).TotalMilliseconds;
        FirstFrameAfterShow?.Invoke(now);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PillClicks++;
        Caption.Text = $"Clicked {PillClicks}×";
    }

    private bool OutsidePill(nint lParam) =>
        !PillRect().Contains((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        switch (msg)
        {
            case WindowStyles.WM_NCCALCSIZE when wParam != 0:
                return 0; // the whole window is client area: no frame for anyone to paint
            case WindowStyles.WM_HOTKEY:
                OnHotkey((int)wParam);
                return 0;
            case WindowStyles.WM_ERASEBKGND:
                TransparentBackdrop.EraseBackground(hwnd, wParam);
                return 1;
            case WindowStyles.WM_DWMCOMPOSITIONCHANGED:
                TransparentBackdrop.ConfigureDwm(hwnd);
                break;
            case WindowStyles.WM_MOUSEACTIVATE:
                return WindowStyles.MA_NOACTIVATE;
            case WindowStyles.WM_LBUTTONDOWN or WindowStyles.WM_POINTERDOWN:
                MouseDownMessages++;
                break;
            case WindowStyles.WM_NCHITTEST when _hitTestMode && OutsidePill(lParam):
                return WindowStyles.HTTRANSPARENT;
        }

        return WindowStyles.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private nint ChildProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        switch (msg)
        {
            case WindowStyles.WM_LBUTTONDOWN or WindowStyles.WM_POINTERDOWN:
                MouseDownMessages++;
                break;
            case WindowStyles.WM_NCHITTEST when _hitTestMode && OutsidePill(lParam):
                return WindowStyles.HTTRANSPARENT;
        }

        return WindowStyles.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
}
