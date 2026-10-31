using System.Text;
using Aldren.Bench.Llama;

namespace Aldren.Bench.Tests;

public class SseParserTests
{
    [Fact]
    public async Task Sse_parser_yields_chunks_then_done()
    {
        // Anything after [DONE] must be ignored.
        var stream = Utf8("data: {\"a\":1}\n\ndata: {\"a\":2}\n\ndata: [DONE]\n\ndata: {\"late\":true}\n\n");

        var events = await Collect(stream);

        Assert.Equal(new[] { "{\"a\":1}", "{\"a\":2}" }, events.Select(e => e.Data));
    }

    [Fact]
    public async Task Comment_lines_and_crlf_line_endings_are_handled()
    {
        var stream = Utf8(": keep-alive\r\n\r\ndata: {\"a\":1}\r\n\r\ndata: [DONE]\r\n\r\n");

        var events = await Collect(stream);

        Assert.Equal(new[] { "{\"a\":1}" }, events.Select(e => e.Data));
    }

    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<List<SseEvent>> Collect(Stream stream)
    {
        var events = new List<SseEvent>();
        await foreach (var e in SseParser.ParseAsync(stream, CancellationToken.None))
        {
            events.Add(e);
        }
        return events;
    }
}
