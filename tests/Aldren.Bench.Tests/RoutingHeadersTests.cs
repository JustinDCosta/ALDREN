using Aldren.Bench.Gateway;

namespace Aldren.Bench.Tests;

public class RoutingHeadersTests
{
    [Fact]
    public void Parses_routed_via() =>
        Assert.Equal(new RoutedVia("groq", "llama-3.3-70b-versatile"), RoutingHeaders.ParseRoutedVia("groq/llama-3.3-70b-versatile"));

    [Fact]
    public void Model_id_may_contain_slashes() =>
        Assert.Equal(new RoutedVia("openrouter", "meta-llama/llama-4-scout:free"), RoutingHeaders.ParseRoutedVia("openrouter/meta-llama/llama-4-scout:free"));

    [Fact]
    public void Decodes_percent_encoded_model_id() =>
        Assert.Equal(new RoutedVia("custom", "模型 100%"), RoutingHeaders.ParseRoutedVia("custom/%E6%A8%A1%E5%9E%8B 100%25"));

    [Fact]
    public void Parses_trail_with_two_hops()
    {
        var hops = RoutingHeaders.ParseTrail("groq/llama-3.3-70b key1=rate_limited; google/gemini-2.5-flash key2=timeout");

        Assert.Equal([new FailedHop("groq", "llama-3.3-70b", "rate_limited"), new FailedHop("google", "gemini-2.5-flash", "timeout")], hops);
    }

    [Fact]
    public void Rejects_platform_outside_allowlist()
    {
        var allowed = new HashSet<string> { "groq", "cerebras" };

        Assert.False(RoutingHeaders.IsAllowed(new RoutedVia("openrouter", "meta-llama/llama-4-scout:free"), allowed));
        Assert.True(RoutingHeaders.IsAllowed(new RoutedVia("groq", "llama-3.3-70b-versatile"), allowed));
    }

    [Fact]
    public void Model_outside_the_chain_is_rejected_even_on_an_allowed_platform()
    {
        var chain = RoutingHeaders.Chain("groq/gpt-oss-120b,groq/gpt-oss-20b");

        Assert.False(RoutingHeaders.IsInChain(new RoutedVia("groq", "llama-3.3-70b-versatile"), chain));
        Assert.True(RoutingHeaders.IsInChain(new RoutedVia("groq", "gpt-oss-120b"), chain));
    }

    [Fact]
    public void Chain_platforms_come_from_its_entries() =>
        Assert.Equal(["google", "groq"], RoutingHeaders.Platforms(RoutingHeaders.Chain("google/gemini-2.5-flash, groq/qwen3.8-27b")).Order());
}
