using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using S5.Perception.Grounding;

namespace S5.Perception.Uia;

/// <summary>Interactive elements of a window from UI Automation, with bounds relative to the captured image.</summary>
internal static class UiaSnapshot
{
    private static readonly HashSet<ControlType> Interactive =
    [
        ControlType.Button, ControlType.CheckBox, ControlType.ComboBox, ControlType.Edit, ControlType.Hyperlink,
        ControlType.ListItem, ControlType.MenuItem, ControlType.RadioButton, ControlType.SplitButton, ControlType.TabItem,
        ControlType.TreeItem, ControlType.Slider, ControlType.Spinner, ControlType.DataItem, ControlType.Document,
    ];

    /// <summary>
    /// Interactive elements, plus the named Text elements (labels and captions: visible text by definition, which makes them the
    /// reference set for OCR). All properties come back in one cached batch, far faster than reading them element by element.
    /// </summary>
    public static (List<UiElementInfo> Elements, List<UiElementInfo> Texts, double Milliseconds) Take(nint hwnd, Native.Rect windowBounds)
    {
        var clock = Stopwatch.StartNew();
        using var automation = new UIA3Automation();
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(automation.PropertyLibrary.Element.Name);
        cache.Add(automation.PropertyLibrary.Element.ControlType);
        cache.Add(automation.PropertyLibrary.Element.BoundingRectangle);
        cache.Add(automation.PropertyLibrary.Element.IsEnabled);
        cache.Add(automation.PropertyLibrary.Element.IsOffscreen);

        // FlaUI 5 cache requests default to AutomationElementMode.None: elements fetched under one hold only cached values and
        // can't be searched from, so the window is fetched before the cache is activated.
        var window = automation.FromHandle(hwnd);
        var elements = new List<UiElementInfo>();
        var texts = new List<UiElementInfo>();
        using (cache.Activate())
        {
            foreach (var e in window.FindAllDescendants())
            {
                try
                {
                    var type = e.Properties.ControlType.ValueOrDefault;
                    var name = e.Properties.Name.ValueOrDefault ?? "";
                    var target = Interactive.Contains(type) ? elements : type == ControlType.Text && name.Trim().Length > 0 ? texts : null;
                    var bounds = e.Properties.BoundingRectangle.ValueOrDefault;
                    if (target is null || e.Properties.IsOffscreen.ValueOrDefault || bounds.Width < 3 || bounds.Height < 3)
                    {
                        continue;
                    }

                    var rect = new Grounding.Rect(bounds.X - windowBounds.Left, bounds.Y - windowBounds.Top, bounds.Width, bounds.Height);
                    if (rect.Right <= 0 || rect.Bottom <= 0 || rect.Left >= windowBounds.Width || rect.Top >= windowBounds.Height)
                    {
                        continue;
                    }

                    target.Add(new UiElementInfo(target.Count + 1, name, type.ToString(), rect, e.Properties.IsEnabled.ValueOrDefault));
                }
                catch (Exception)
                {
                    // The element disappeared while the tree was being read.
                }
            }
        }

        return (elements, texts, clock.Elapsed.TotalMilliseconds);
    }
}
