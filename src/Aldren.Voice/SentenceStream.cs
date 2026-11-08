using System.Text;
using System.Threading.Channels;

namespace Aldren.Voice;

/// <summary>
/// Turns a reply arriving in fragments into whole sentences for the speaker. A sentence ends at . ! ? or … followed by a
/// space, or at a line break; "3.5" and "e.g." mid-word don't split. Markdown emphasis is dropped.
/// </summary>
public sealed class SentenceStream
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly StringBuilder _pending = new();
    private readonly StringBuilder _all = new();

    public ChannelReader<string> Reader => _channel.Reader;

    /// <summary>Everything pushed so far.</summary>
    public string Text => _all.ToString();

    public void Push(string fragment)
    {
        _all.Append(fragment);
        _pending.Append(fragment.Replace("*", ""));
        while (TryTakeSentence(out var sentence))
        {
            _channel.Writer.TryWrite(sentence);
        }
    }

    /// <summary>Sends whatever is left and ends the stream.</summary>
    public void Complete()
    {
        var rest = _pending.ToString().Trim();
        if (rest.Length > 0)
        {
            _channel.Writer.TryWrite(rest);
        }

        _pending.Clear();
        _channel.Writer.TryComplete();
    }

    private bool TryTakeSentence(out string sentence)
    {
        var text = _pending.ToString();
        for (var i = 0; i < text.Length - 1; i++)
        {
            var end = text[i] is '.' or '!' or '?' or '…' && char.IsWhiteSpace(text[i + 1]) || text[i] == '\n';
            if (!end)
            {
                continue;
            }

            var candidate = text[..(i + 1)].Trim();
            // Too short to be worth a separate synthesis call ("Sure."), unless it's all there is so far.
            if (candidate.Length < 2)
            {
                continue;
            }

            sentence = candidate;
            _pending.Remove(0, i + 1);
            return true;
        }

        sentence = "";
        return false;
    }
}
