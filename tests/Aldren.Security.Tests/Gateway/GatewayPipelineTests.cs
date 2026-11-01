using Aldren.Security.Audit;
using Aldren.Security.Gateway;

namespace Aldren.Security.Tests.Gateway;

public sealed class GatewayPipelineTests
{
    private readonly GatewayFixture _f = new();

    [Fact]
    public async Task Forbidden_tier_never_runs_even_with_permission()
    {
        var outcome = await _f.InvokeAsync("fake_forbidden");

        Assert.Equal(ToolOutcomeStatus.Forbidden, outcome.Status);
        Assert.Equal(0, _f.Forbidden.Runs);
        Assert.Empty(_f.Broker.Requests);
    }

    [Fact]
    public async Task Unknown_tool_is_refused_and_audited()
    {
        var outcome = await _f.InvokeAsync("no_such_tool");

        Assert.Equal(ToolOutcomeStatus.UnknownTool, outcome.Status);
        var record = Assert.Single(_f.Audit.Finished);
        Assert.Equal("no_such_tool", record.Tool);
        Assert.Equal(ToolOutcomeStatus.UnknownTool, record.Outcome);
    }

    [Theory]
    [InlineData("""{"text":"hi","extra":1}""")]
    [InlineData("{}")]
    [InlineData("""{"text":5}""")]
    [InlineData("""{"text":null}""")]
    [InlineData("[]")]
    public async Task Invalid_arguments_never_reach_the_tool(string json)
    {
        var outcome = await _f.InvokeAsync("fake_read", json);

        Assert.Equal(ToolOutcomeStatus.InvalidArguments, outcome.Status);
        Assert.Equal(0, _f.Read.Runs);
    }

    [Fact]
    public async Task Missing_permission_is_refused()
    {
        var outcome = await _f.InvokeAsync("fake_locked");

        Assert.Equal(ToolOutcomeStatus.PermissionDenied, outcome.Status);
        Assert.Equal(0, _f.Locked.Runs);
    }

    [Theory]
    [InlineData("fake_read")]
    [InlineData("fake_reversible")]
    public async Task Read_and_reversible_run_without_confirmation(string tool)
    {
        var outcome = await _f.InvokeAsync(tool);

        Assert.Equal(ToolOutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal("hello", outcome.Result!.GetValue<string>());
        Assert.Empty(_f.Broker.Requests);
    }

    [Fact]
    public async Task Impactful_asks_and_runs_on_yes()
    {
        var outcome = await _f.InvokeAsync("fake_impactful");

        Assert.Equal(ToolOutcomeStatus.Succeeded, outcome.Status);
        Assert.Single(_f.Broker.Requests);
        Assert.Equal(1, _f.Impactful.Runs);
        Assert.True(_f.Audit.Finished.Single().Confirmed);
    }

    [Fact]
    public async Task Declined_confirmation_does_not_run_the_tool()
    {
        _f.Broker.Answer = (_, _) => Task.FromResult(false);

        var outcome = await _f.InvokeAsync("fake_impactful");

        Assert.Equal(ToolOutcomeStatus.Declined, outcome.Status);
        Assert.Equal(0, _f.Impactful.Runs);
    }

    [Fact]
    public async Task Destructive_asks_for_explicit_confirmation()
    {
        await _f.InvokeAsync("fake_destructive", """{"text":"report.docx"}""");

        var request = Assert.Single(_f.Broker.Requests);
        Assert.Equal("Destructive", request.Tier);
        Assert.Contains("Do the fake thing with 'report.docx'", request.Summary);
        Assert.Contains("Details:", request.Summary);
        Assert.Equal(60, request.TimeoutSeconds);
    }

    [Fact]
    public async Task Tainted_model_call_names_its_sources()
    {
        var context = new CallContext(CallOrigin.Model, ["web page: example.com"]);

        await _f.InvokeAsync("fake_impactful", context: context);

        var request = Assert.Single(_f.Broker.Requests);
        Assert.Equal(["web page: example.com"], request.UntrustedSources);
        Assert.True(_f.Audit.Finished.Single().Tainted);
    }

    [Fact]
    public async Task Unanswered_confirmation_is_declined_after_60_seconds()
    {
        var brokerToken = CancellationToken.None;
        _f.Broker.Answer = (_, ct) =>
        {
            brokerToken = ct;
            return new TaskCompletionSource<bool>().Task;
        };

        var pending = _f.InvokeAsync("fake_impactful");
        _f.Time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(pending.IsCompleted);
        _f.Time.Advance(TimeSpan.FromSeconds(1));
        var outcome = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ToolOutcomeStatus.Declined, outcome.Status);
        Assert.Equal(0, _f.Impactful.Runs);
        Assert.True(brokerToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Confirmation_failure_counts_as_declined()
    {
        _f.Broker.Answer = (_, _) => throw new InvalidOperationException("No App is connected.");

        var outcome = await _f.InvokeAsync("fake_impactful");

        Assert.Equal(ToolOutcomeStatus.Declined, outcome.Status);
        Assert.Equal(0, _f.Impactful.Runs);
    }

    [Fact]
    public async Task Tool_timeout_cancels_and_reports_timed_out()
    {
        _f.Read.Behaviour = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        };

        var pending = _f.InvokeAsync("fake_read");
        _f.Time.Advance(TimeSpan.FromSeconds(10));
        var outcome = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ToolOutcomeStatus.TimedOut, outcome.Status);
        Assert.True(_f.Read.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Failed_postcondition_is_reported()
    {
        _f.Read.PostconditionHolds = false;

        var outcome = await _f.InvokeAsync("fake_read");

        Assert.Equal(ToolOutcomeStatus.PostconditionFailed, outcome.Status);
        Assert.Null(outcome.Result);
    }

    [Fact]
    public async Task Tool_exception_becomes_a_short_message()
    {
        _f.Read.Behaviour = (_, _) => throw new InvalidOperationException("Disk said no.\nat Some.Stack.Frame()");

        var outcome = await _f.InvokeAsync("fake_read");

        Assert.Equal(ToolOutcomeStatus.Failed, outcome.Status);
        Assert.Contains("Disk said no.", outcome.Message);
        Assert.DoesNotContain("\n", outcome.Message);
        Assert.DoesNotContain("GatewayPipelineTests", outcome.Message);
    }

    [Theory]
    [InlineData("fake_reversible")]
    [InlineData("fake_impactful")]
    [InlineData("fake_destructive")]
    public async Task Audit_failure_blocks_tier1_and_above(string tool)
    {
        _f.Audit.Broken = true;

        var outcome = await _f.InvokeAsync(tool);

        Assert.Equal(ToolOutcomeStatus.AuditUnavailable, outcome.Status);
        Assert.Equal(0, _f.Reversible.Runs + _f.Impactful.Runs + _f.Destructive.Runs);
        Assert.Empty(_f.Broker.Requests);
    }

    [Fact]
    public async Task Audit_failure_does_not_block_reads()
    {
        _f.Audit.Broken = true;

        var outcome = await _f.InvokeAsync("fake_read");

        Assert.Equal(ToolOutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(1, _f.Read.Runs);
    }

    [Theory]
    [InlineData(ToolOutcomeStatus.Succeeded)]
    [InlineData(ToolOutcomeStatus.UnknownTool)]
    [InlineData(ToolOutcomeStatus.InvalidArguments)]
    [InlineData(ToolOutcomeStatus.PermissionDenied)]
    [InlineData(ToolOutcomeStatus.Forbidden)]
    [InlineData(ToolOutcomeStatus.Declined)]
    [InlineData(ToolOutcomeStatus.TimedOut)]
    [InlineData(ToolOutcomeStatus.PostconditionFailed)]
    [InlineData(ToolOutcomeStatus.Failed)]
    public async Task Every_outcome_is_audited(ToolOutcomeStatus expected)
    {
        var tool = "fake_read";
        var json = """{"text":"hello"}""";
        switch (expected)
        {
            case ToolOutcomeStatus.UnknownTool: tool = "no_such_tool"; break;
            case ToolOutcomeStatus.InvalidArguments: json = "{}"; break;
            case ToolOutcomeStatus.PermissionDenied: tool = "fake_locked"; break;
            case ToolOutcomeStatus.Forbidden: tool = "fake_forbidden"; break;
            case ToolOutcomeStatus.Declined:
                tool = "fake_impactful";
                _f.Broker.Answer = (_, _) => Task.FromResult(false);
                break;
            case ToolOutcomeStatus.TimedOut:
                _f.Read.Behaviour = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; };
                break;
            case ToolOutcomeStatus.PostconditionFailed: _f.Read.PostconditionHolds = false; break;
            case ToolOutcomeStatus.Failed: _f.Read.Behaviour = (_, _) => throw new InvalidOperationException("boom"); break;
        }

        var pending = _f.InvokeAsync(tool, json);
        if (expected == ToolOutcomeStatus.TimedOut)
        {
            _f.Time.Advance(TimeSpan.FromSeconds(10));
        }

        var outcome = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expected, outcome.Status);
        var record = Assert.Single(_f.Audit.Finished);
        Assert.Equal(expected, record.Outcome);
        Assert.Equal(tool, record.Tool);
        Assert.All(_f.Audit.Records, r => Assert.Equal(record.RequestId, r.RequestId));
    }

    [Fact]
    public async Task Large_results_are_capped()
    {
        _f.Read.Behaviour = (_, _) => Task.FromResult<object?>(new string('x', 20_000));

        var outcome = await _f.InvokeAsync("fake_read");

        Assert.Equal(ToolOutcomeStatus.Succeeded, outcome.Status);
        Assert.True(outcome.Result!["truncated"]!.GetValue<bool>());
        Assert.Equal(ToolGateway.PreviewLength, outcome.Result["preview"]!.GetValue<string>().Length);
    }

    [Fact]
    public async Task Started_and_finished_are_both_recorded_for_a_run()
    {
        await _f.InvokeAsync("fake_read");

        Assert.Equal([AuditStage.Started, AuditStage.Finished], _f.Audit.Records.Select(r => r.Stage));
    }
}
