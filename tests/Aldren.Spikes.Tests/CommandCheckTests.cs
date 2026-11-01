using S6.Host;
using Xunit;

namespace Aldren.Spikes.Tests;

public class CommandCheckTests
{
    [Theory]
    [InlineData("{\"id\":1,\"cmd\":\"ping\"}")]
    [InlineData("{\"id\":2,\"cmd\":\"fill\",\"args\":{\"id\":4,\"value\":\"x\"}}")]
    public void Known_commands_pass(string line) => Assert.Null(CommandCheck.Problem(line));

    [Theory]
    [InlineData("{\"id\":1,\"cmd\":\"exec\",\"args\":{\"command\":\"calc\"}}")]
    [InlineData("{\"cmd\":\"ping\"}")]
    [InlineData("{\"id\":\"1\",\"cmd\":\"ping\"}")]
    [InlineData("{\"id\":1,\"cmd\":\"read\",\"args\":\"all\"}")]
    [InlineData("not json")]
    public void Anything_else_is_refused(string line) => Assert.NotNull(CommandCheck.Problem(line));
}
