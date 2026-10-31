using Aldren.Bench.Eval;

namespace Aldren.Bench.Tests;

public class ResponseParserTests
{
    [Fact]
    public void Tool_call_is_extracted_from_response()
    {
        const string json = """
            {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
              {"id":"1","type":"function","function":{"name":"set_volume","arguments":"{\"level\":40}"}}]}}]}
            """;

        var reply = EvalRunner.ParseResponse(json);

        Assert.Equal("tool", reply.Route);
        Assert.Equal("set_volume", reply.ToolName);
        Assert.Equal(40, reply.ToolArgs!["level"]!.GetValue<int>());
    }

    [Fact]
    public void Malformed_arguments_give_null_args()
    {
        const string json = """
            {"choices":[{"message":{"tool_calls":[{"function":{"name":"set_volume","arguments":"{level:40"}}]}}]}
            """;

        var reply = EvalRunner.ParseResponse(json);

        Assert.Equal("set_volume", reply.ToolName);
        Assert.Null(reply.ToolArgs);
    }

    [Fact]
    public void Plain_answer_has_quick_route_and_text()
    {
        var reply = EvalRunner.ParseResponse("""{"choices":[{"message":{"role":"assistant","content":" Paris. "}}]}""");

        Assert.Equal("quick", reply.Route);
        Assert.Null(reply.ToolName);
        Assert.Equal("Paris.", reply.Text);
    }

    [Theory]
    [InlineData("delegate_to_action", "action")]
    [InlineData("request_research", "research")]
    [InlineData("launch_app", "tool")]
    [InlineData(null, "quick")]
    public void Tools_map_to_routes(string? tool, string expectedRoute)
    {
        Assert.Equal(expectedRoute, EvalScorer.RouteOf(tool));
    }
}
