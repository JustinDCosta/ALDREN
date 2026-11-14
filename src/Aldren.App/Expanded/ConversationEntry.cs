using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Aldren.App.Expanded;

/// <summary>One turn of this session's conversation, typed or spoken. Kept in memory only.</summary>
public sealed class ConversationEntry(bool fromUser, string text, bool voice = false, bool pending = false) : INotifyPropertyChanged
{
    private string _text = text;
    private bool _pending = pending;

    public bool FromUser { get; } = fromUser;

    public bool Voice { get; } = voice;

    public DateTimeOffset Time { get; } = DateTimeOffset.Now;

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                Raise(nameof(Text));
                Raise(nameof(Waiting));
            }
        }
    }

    /// <summary>True while the reply is still being written.</summary>
    public bool Pending
    {
        get => _pending;
        set
        {
            if (_pending != value)
            {
                _pending = value;
                Raise(nameof(Pending));
                Raise(nameof(Waiting));
            }
        }
    }

    /// <summary>Nothing written yet: the bubble shows the "working" ring instead of text.</summary>
    public bool Waiting => Pending && Text.Length == 0;

    public string Meta => Time.ToString("t") + (Voice ? "  ·  voice" : "");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Your turns on the right, ALDREN's on the left.</summary>
public sealed class EntryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? User { get; set; }

    public DataTemplate? Assistant { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) => (item is ConversationEntry { FromUser: true } ? User : Assistant)!;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

/// <summary>
/// <c>Linked.Text</c> on a TextBlock: the text with its http(s) addresses as links (research answers list their sources).
/// The address is shown shortened; the link opens in your browser only when you click it.
/// </summary>
public static partial class Linked
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Linked), new PropertyMetadata("", OnTextChanged));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        var text = e.NewValue as string ?? "";
        block.Inlines.Clear();
        var last = 0;
        foreach (Match match in Url().Matches(text))
        {
            var address = match.Value.TrimEnd('.', ',', ')', ']', ';', ':', '!', '?');
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                continue;
            }

            if (match.Index > last)
            {
                block.Inlines.Add(new Run { Text = text[last..match.Index] });
            }

            var link = new Hyperlink { NavigateUri = uri };
            link.Inlines.Add(new Run { Text = Shorten(uri) });
            block.Inlines.Add(link);
            last = match.Index + address.Length;
        }

        if (last < text.Length)
        {
            block.Inlines.Add(new Run { Text = text[last..] });
        }
    }

    /// <summary>"example.com/long/path/to/page" → "example.com/long/path…".</summary>
    private static string Shorten(Uri uri)
    {
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var path = uri.AbsolutePath.TrimEnd('/');
        return path.Length switch
        {
            0 => host,
            <= 28 => host + path,
            _ => host + path[..27] + "…",
        };
    }
}
