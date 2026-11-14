using Aldren.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aldren.App.Settings;

/// <summary>One card on a Settings page: a title, an optional line under it, and an optional control on the right.</summary>
public sealed class SettingCard(Border root, TextBlock title, TextBlock description)
{
    public Border Root { get; } = root;

    public TextBlock Title { get; } = title;

    /// <summary>Sets (or clears) the line under the title.</summary>
    public void Describe(string? text)
    {
        description.Text = text ?? "";
        description.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>A titled column of cards that loads from Core when shown.</summary>
public abstract class SettingsPage : UserControl
{
    private const double SecondaryOpacity = 0.72;
    private InfoBar? _note;
    private bool _reverting;

    protected SettingsPage(string title, string? lead = null)
    {
        Body = new StackPanel { Spacing = 4, MaxWidth = 860 };
        Body.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, lead is null ? 12 : 6),
        });
        if (lead is not null)
        {
            Body.Children.Add(Secondary(lead, new Thickness(0, 0, 0, 12)));
        }

        Content = Body;
        Loaded += async (_, _) =>
        {
            if (ShellApp.Instance.Core is { } core)
            {
                try
                {
                    await LoadAsync(core);
                }
                catch (Exception ex)
                {
                    Note("Couldn't load this page: " + ex.Message, InfoBarSeverity.Error);
                }
            }
            else
            {
                Note("ALDREN's background service isn't connected right now. It restarts on its own; open this page again in a moment.", InfoBarSeverity.Warning);
            }
        };
    }

    protected StackPanel Body { get; }

    protected abstract Task LoadAsync(IAldrenCore core);

    /// <summary>Explanatory text in the page's flow.</summary>
    protected TextBlock Paragraph(string text)
    {
        var block = Secondary(text, new Thickness(2, 4, 2, 4));
        Body.Children.Add(block);
        return block;
    }

    /// <summary>A group heading above the cards that follow.</summary>
    protected TextBlock Header(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(2, 18, 0, 4),
        };
        Body.Children.Add(block);
        return block;
    }

    /// <summary>A setting: title, a line of explanation, and its control on the right.</summary>
    protected SettingCard Card(string title, string? description = null, FrameworkElement? control = null, string? glyph = null)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (glyph is not null)
        {
            grid.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });
        }

        var titleBlock = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        var descriptionBlock = Secondary(description ?? "", default);
        descriptionBlock.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
        descriptionBlock.IsTextSelectionEnabled = true;
        descriptionBlock.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { titleBlock, descriptionBlock } };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 2);
            grid.Children.Add(control);
        }

        return new SettingCard(AddCard(grid), titleBlock, descriptionBlock);
    }

    /// <summary>A card with free content (a list, a form).</summary>
    protected Border AddCard(UIElement content)
    {
        var card = new Border { Style = (Style)Application.Current.Resources["AldrenCardStyle"], Child = content, MinHeight = 60 };
        Body.Children.Add(card);
        return card;
    }

    protected static StackPanel Row(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var item in items)
        {
            row.Children.Add(item);
        }

        return row;
    }

    /// <summary>A compact switch for the right side of a card (no "On"/"Off" label taking room).</summary>
    protected static ToggleSwitch Switch(bool on, string name) =>
        Named(new ToggleSwitch { IsOn = on, OnContent = "", OffContent = "", MinWidth = 0 }, name);

    protected static TextBlock Secondary(string text, Thickness margin) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = SecondaryOpacity, Margin = margin };

    /// <summary>The page's one message bar, under the title: the latest problem or result.</summary>
    protected InfoBar Note(string message, InfoBarSeverity severity)
    {
        if (_note is null)
        {
            _note = new InfoBar { IsOpen = true, IsClosable = true, Margin = new Thickness(0, 0, 0, 8) };
            Body.Children.Insert(1, _note);
        }

        _note.Message = message;
        _note.Severity = severity;
        _note.IsOpen = true;
        return _note;
    }

    protected void ClearNote()
    {
        if (_note is not null)
        {
            _note.IsOpen = false;
        }
    }

    /// <summary>Applies one change to the settings and saves at once; a refusal shows in the page's message bar.</summary>
    protected async Task<bool> SaveAsync(IAldrenCore core, Func<AldrenSettings, AldrenSettings> change)
    {
        try
        {
            var updated = change(await core.GetSettingsAsync(CancellationToken.None));
            var saved = await core.UpdateSettingsAsync(updated, CancellationToken.None);
            if (saved.Saved)
            {
                ShellApp.Instance.ApplySettings(updated);
                ClearNote();
                return true;
            }

            Note(string.Join(" ", saved.Errors), InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            Note("Couldn't save: " + ex.Message, InfoBarSeverity.Error);
        }

        return false;
    }

    /// <summary>A switch that saves when flipped, and flips back if the change is refused.</summary>
    protected void SaveOnToggle(ToggleSwitch toggle, IAldrenCore core, Func<AldrenSettings, bool, AldrenSettings> change)
    {
        toggle.Toggled += async (_, _) =>
        {
            if (_reverting)
            {
                return;
            }

            if (!await SaveAsync(core, s => change(s, toggle.IsOn)))
            {
                _reverting = true;
                toggle.IsOn = !toggle.IsOn;
                _reverting = false;
            }
        };
    }

    protected static T Named<T>(T element, string name) where T : UIElement
    {
        AutomationProperties.SetName(element, name);
        return element;
    }
}
