using Aldren.Bench.Llama;

namespace Aldren.Bench.Tests;

public class StreamingProbeTests
{
    [Fact]
    public void Tokens_per_second_excludes_time_to_first_token()
    {
        var timing = StreamingProbe.Compute(ttftMs: 200, totalMs: 1200, outputTokens: 100, text: "x");

        Assert.Equal(200, timing.TtftMs);
        Assert.Equal(100, timing.TokensPerSecond, 6);
    }

    [Fact]
    public void No_generation_time_gives_zero_rate()
    {
        var timing = StreamingProbe.Compute(ttftMs: 300, totalMs: 300, outputTokens: 1, text: "x");

        Assert.Equal(0, timing.TokensPerSecond);
    }

    [Theory]
    [InlineData("{\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}", "Hi")]
    [InlineData("{\"choices\":[{\"delta\":{\"reasoning_content\":\"hmm\"}}]}", "hmm")]
    [InlineData("{\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}", "")]
    public void Chunk_text_comes_from_content_or_reasoning(string json, string expected)
    {
        Assert.Equal(expected, StreamingProbe.ParseChunk(json).Text);
    }

    [Fact]
    public void Usage_chunk_reports_completion_tokens()
    {
        var chunk = StreamingProbe.ParseChunk("{\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":42}}");

        Assert.Equal(42, chunk.CompletionTokens);
        Assert.Equal("", chunk.Text);
    }
}
