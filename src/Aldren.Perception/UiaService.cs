using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Aldren.Perception;

/// <summary>
/// UI Automation over the window you're using (ARCHITECTURE §11, ADR-0013): a scoped snapshot of its controls with IDs
/// tied to the snapshot, and actions on those controls through their automation patterns, each checked afterwards.
/// ALDREN's own windows are never the target.
/// </summary>
public sealed class UiaService : IDisposable
{
    private static readonly HashSet<ControlType> Interactive =
    [
        ControlType.Button, ControlType.CheckBox, ControlType.ComboBox, ControlType.Edit, ControlType.Hyperlink,
        ControlType.ListItem, ControlType.MenuItem, ControlType.RadioButton, ControlType.SplitButton, ControlType.TabItem,
        ControlType.TreeItem, ControlType.Slider, ControlType.Spinner, ControlType.DataItem, ControlType.Document, ControlType.MenuBar,
    ];

    private static readonly HashSet<string> OwnProcesses = new(StringComparer.OrdinalIgnoreCase) { "Aldren.App", "Aldren.Core" };

    private readonly UIA3Automation _automation = new();
    private readonly Lock _lock = new();
    private Dictionary<int, AutomationElement> _live = [];
    private int _version;

    public ScreenSnapshot? Latest { get; private set; }

    /// <summary>The topmost visible, restored, non-ALDREN app window in z-order: the one you're using.</summary>
    public static nint TargetWindow()
    {
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindow(hwnd, 4) != 0 || (GetWindowLongPtr(hwnd, -20) & 0x80) != 0
                || GetWindowTextLength(hwnd) == 0)
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                using var p = Process.GetProcessById(pid);
                if (OwnProcesses.Contains(p.ProcessName))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            found = hwnd;
            return false;
        }, 0);
        return found;
    }

    public ScreenSnapshot Snapshot(nint hwnd = 0, int maxElements = 400)
    {
        lock (_lock)
        {
            var clock = Stopwatch.StartNew();
            hwnd = hwnd == 0 ? TargetWindow() : hwnd;
            if (hwnd == 0)
            {
                throw new InvalidOperationException("There's no app window to look at.");
            }

            var window = _automation.FromHandle(hwnd);
            var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.Full };
            cache.Add(_automation.PropertyLibrary.Element.Name);
            cache.Add(_automation.PropertyLibrary.Element.ControlType);
            cache.Add(_automation.PropertyLibrary.Element.BoundingRectangle);
            cache.Add(_automation.PropertyLibrary.Element.IsEnabled);
            cache.Add(_automation.PropertyLibrary.Element.IsOffscreen);
            cache.Add(_automation.PropertyLibrary.Element.AutomationId);
            cache.Add(_automation.PropertyLibrary.Element.IsPassword);

            var elements = new List<ScreenElement>();
            var texts = new List<string>();
            var live = new Dictionary<int, AutomationElement>();
            using (cache.Activate())
            {
                foreach (var e in window.FindAllDescendants())
                {
                    try
                    {
                        var type = e.Properties.ControlType.ValueOrDefault;
                        var name = (e.Properties.Name.ValueOrDefault ?? "").Trim();
                        var r = e.Properties.BoundingRectangle.ValueOrDefault;
                        if (e.Properties.IsOffscreen.ValueOrDefault || r.Width < 3 || r.Height < 3)
                        {
                            continue;
                        }

                        if (type == ControlType.Text)
                        {
                            if (name.Length > 0 && texts.Count < 200)
                            {
                                texts.Add(name);
                            }

                            continue;
                        }

                        if (!Interactive.Contains(type) || elements.Count >= maxElements)
                        {
                            continue;
                        }

                        var id = elements.Count + 1;
                        elements.Add(new ScreenElement(id, name, type.ToString(), e.Properties.AutomationId.ValueOrDefault ?? "",
                            new Box(r.X, r.Y, r.Width, r.Height), e.Properties.IsEnabled.ValueOrDefault, e.Properties.IsPassword.ValueOrDefault));
                        live[id] = e;
                    }
                    catch (Exception ex) when (ex is COMException or InvalidOperationException or FlaUI.Core.Exceptions.PropertyNotSupportedException)
                    {
                        // The element went away while the tree was being read.
                    }
                }
            }

            var bounds = window.BoundingRectangle;
            _live = live;
            _version++;
            var snapshot = new ScreenSnapshot(_version, hwnd, window.Name ?? "", ProcessName(hwnd),
                new Box(bounds.X, bounds.Y, bounds.Width, bounds.Height), elements, texts.Distinct().ToList(),
                Match(_automation.FocusedElement(), elements), UnderCursor(elements), clock.Elapsed);
            Latest = snapshot;
            return snapshot;
        }
    }

    /// <summary>Runs an action on a control from the given snapshot. Stale IDs are refused rather than guessed.</summary>
    public string Act(int snapshotVersion, int id, ElementAction action, string? text = null)
    {
        lock (_lock)
        {
            if (snapshotVersion != _version)
            {
                throw new InvalidOperationException("The screen has changed since that control was seen; look again first.");
            }

            if (!_live.TryGetValue(id, out var e) || Latest?.Find(id) is not { } info)
            {
                throw new InvalidOperationException($"There's no control [{id}] in the current snapshot.");
            }

            if (!info.Enabled)
            {
                throw new InvalidOperationException($"\"{info.Name}\" is disabled.");
            }

            return action switch
            {
                ElementAction.Invoke => Invoke(e, info),
                ElementAction.SetText => SetText(e, info, text ?? ""),
                ElementAction.Select => Select(e, info),
                ElementAction.Toggle => Toggle(e, info),
                ElementAction.Expand => Expand(e, info),
                ElementAction.ScrollDown or ElementAction.ScrollUp => Scroll(e, info, action == ElementAction.ScrollDown),
                ElementAction.Focus => FocusOn(e, info),
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
        }
    }

    private static string Invoke(AutomationElement e, ScreenElement info)
    {
        if (e.Patterns.Invoke.TryGetPattern(out var invoke))
        {
            invoke.Invoke();
            return $"Pressed \"{info.Name}\".";
        }

        if (e.Patterns.Toggle.IsSupported)
        {
            return Toggle(e, info);
        }

        if (e.Patterns.SelectionItem.TryGetPattern(out var item))
        {
            item.Select();
            return $"Selected \"{info.Name}\".";
        }

        if (e.Patterns.ExpandCollapse.IsSupported)
        {
            return Expand(e, info);
        }

        if (e.Patterns.LegacyIAccessible.TryGetPattern(out var legacy))
        {
            legacy.DoDefaultAction();
            return $"Activated \"{info.Name}\".";
        }

        throw new InvalidOperationException($"\"{info.Name}\" can't be pressed through UI Automation.");
    }

    private static string SetText(AutomationElement e, ScreenElement info, string text)
    {
        if (info.IsPassword)
        {
            throw new InvalidOperationException("That's a password field. ALDREN never types passwords.");
        }

        if (!e.Patterns.Value.TryGetPattern(out var value) || value.IsReadOnly.ValueOrDefault)
        {
            throw new InvalidOperationException($"\"{info.Name}\" doesn't accept text.");
        }

        value.SetValue(text);
        var now = value.Value.ValueOrDefault ?? "";
        return now == text ? $"Typed into \"{info.Name}\"." : $"Set the text, but \"{info.Name}\" now shows something else.";
    }

    private static string Select(AutomationElement e, ScreenElement info)
    {
        if (!e.Patterns.SelectionItem.TryGetPattern(out var item))
        {
            return Invoke(e, info);
        }

        item.Select();
        return item.IsSelected.ValueOrDefault ? $"Selected \"{info.Name}\"." : $"Asked to select \"{info.Name}\", but it isn't selected.";
    }

    private static string Toggle(AutomationElement e, ScreenElement info)
    {
        if (!e.Patterns.Toggle.TryGetPattern(out var toggle))
        {
            throw new InvalidOperationException($"\"{info.Name}\" isn't something that switches on and off.");
        }

        var before = toggle.ToggleState.ValueOrDefault;
        toggle.Toggle();
        var after = toggle.ToggleState.ValueOrDefault;
        return before == after ? $"\"{info.Name}\" didn't change." : $"\"{info.Name}\" is now {(after == ToggleState.On ? "on" : "off")}.";
    }

    private static string Expand(AutomationElement e, ScreenElement info)
    {
        if (!e.Patterns.ExpandCollapse.TryGetPattern(out var expand))
        {
            throw new InvalidOperationException($"\"{info.Name}\" doesn't open or close.");
        }

        if (expand.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Expanded)
        {
            expand.Collapse();
            return $"Closed \"{info.Name}\".";
        }

        expand.Expand();
        return $"Opened \"{info.Name}\".";
    }

    private static string Scroll(AutomationElement e, ScreenElement info, bool down)
    {
        var target = e;
        while (target is not null && !target.Patterns.Scroll.IsSupported)
        {
            target = target.Parent;
        }

        if (target is null || !target.Patterns.Scroll.TryGetPattern(out var scroll))
        {
            throw new InvalidOperationException($"Nothing around \"{info.Name}\" scrolls.");
        }

        scroll.Scroll(ScrollAmount.NoAmount, down ? ScrollAmount.LargeIncrement : ScrollAmount.LargeDecrement);
        return down ? "Scrolled down." : "Scrolled up.";
    }

    private static string FocusOn(AutomationElement e, ScreenElement info)
    {
        e.Focus();
        return $"Focused \"{info.Name}\".";
    }

    private int? Match(AutomationElement? element, List<ScreenElement> elements)
    {
        try
        {
            if (element is null)
            {
                return null;
            }

            var r = element.BoundingRectangle;
            var name = element.Name ?? "";
            return elements.FirstOrDefault(x => x.Name == name && Math.Abs(x.Bounds.Left - r.X) < 2 && Math.Abs(x.Bounds.Top - r.Y) < 2)?.Id;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static int? UnderCursor(List<ScreenElement> elements)
    {
        if (!GetCursorPos(out var p))
        {
            return null;
        }

        // The smallest control under the pointer is the specific one.
        return elements.Where(e => e.Bounds.Contains(p.X, p.Y)).OrderBy(e => e.Bounds.Width * e.Bounds.Height).FirstOrDefault()?.Id;
    }

    private static string ProcessName(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (ArgumentException)
        {
            return "unknown";
        }
    }

    public void Dispose() => _automation.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out int pid);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
}

public enum ElementAction
{
    Invoke,
    SetText,
    Select,
    Toggle,
    Expand,
    ScrollDown,
    ScrollUp,
    Focus,
}
