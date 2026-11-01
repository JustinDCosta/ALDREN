using Aldren.Bench.Commands;

namespace Aldren.Bench.Tests;

public class GatewayUrlTests
{
    [Fact]
    public void Without_url_the_port_start_gateway_saved_is_used() =>
        Assert.Equal(new Uri("http://127.0.0.1:64102/"), GatewayCommand.ResolveUrl(null, "64102\r\n"));

    [Fact]
    public void An_explicit_url_wins() =>
        Assert.Equal(new Uri("http://127.0.0.1:5000/"), GatewayCommand.ResolveUrl("http://127.0.0.1:5000", "64102"));

    [Fact]
    public void A_placeholder_url_is_refused_rather_than_replaced() =>
        Assert.Null(GatewayCommand.ResolveUrl("http://127.0.0.1:PORT", "64102"));
}
