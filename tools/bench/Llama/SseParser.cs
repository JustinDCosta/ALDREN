using System.Runtime.CompilerServices;
using System.Text;

namespace Aldren.Bench.Llama;

public record SseEvent(string Data);

/// <summary>Reads the data fields of a server-sent event stream until the OpenAI-style [DONE] marker.</summary>
public static class SseParser
{
    public static async IAsyncEnumerable<SseEvent> ParseAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length == 0)
                {
                    continue;
                }

                var payload = data.ToString();
                data.Clear();
                if (payload == "[DONE]")
                {
                    yield break;
                }

                yield return new SseEvent(payload);
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                // The field value starts after "data:" and one optional space.
                data.Append(line.Length > 5 && line[5] == ' ' ? line.AsSpan(6) : line.AsSpan(5));
            }
            // Comment lines (":") and the event/id/retry fields are not used by the servers we call.
        }

        if (data.Length > 0 && data.ToString() != "[DONE]")
        {
            yield return new SseEvent(data.ToString());
        }
    }
}
