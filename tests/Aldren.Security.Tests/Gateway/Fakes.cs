using System.Text.Json;
using Aldren.Contracts;
using Aldren.Security.Audit;
using Aldren.Security.Gateway;
using Aldren.Security.Permissions;
using Aldren.Security.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Aldren.Security.Tests.Gateway;

public sealed record FakeArgs(string Text);

/// <summary>A tool whose behaviour each test sets.</summary>
public sealed class FakeTool(string name, RiskTier tier, string permission = "test.allowed") : Tool<FakeArgs, object?>
{
    public int Runs { get; private set; }
    public CancellationToken LastToken { get; private set; }
    public Func<FakeArgs, CancellationToken, Task<object?>> Behaviour { get; set; } = (a, _) => Task.FromResult<object?>(a.Text);
    public bool PostconditionHolds { get; set; } = true;

    public override string Name => name;
    public override string Description => "A fake tool";
    public override RiskTier Tier => tier;
    public override string Permission => permission;

    public override string Summarize(FakeArgs args) => $"Do the fake thing with '{args.Text}'";

    protected override Task<object?> ExecuteAsync(FakeArgs args, CancellationToken ct)
    {
        Runs++;
        LastToken = ct;
        return Behaviour(args, ct);
    }

    protected override Task<bool> VerifyAsync(FakeArgs args, object? result, CancellationToken ct) => Task.FromResult(PostconditionHolds);
}

public sealed class FakeBroker : IConfirmationBroker
{
    public List<ConfirmationRequest> Requests { get; } = [];
    public Func<ConfirmationRequest, CancellationToken, Task<bool>> Answer { get; set; } = (_, _) => Task.FromResult(true);

    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Answer(request, ct);
    }
}

public sealed class InMemoryAuditLog : IAuditLog
{
    public List<AuditRecord> Records { get; } = [];
    public bool Broken { get; set; }

    public Task<long> AppendAsync(AuditRecord record, CancellationToken ct)
    {
        if (Broken)
        {
            throw new IOException("database is locked");
        }

        Records.Add(record);
        return Task.FromResult((long)Records.Count);
    }

    public Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct) => throw new NotSupportedException();

    public Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken ct) => throw new NotSupportedException();

    public IEnumerable<AuditRecord> Finished => Records.Where(r => r.Stage == AuditStage.Finished);
}

public sealed class FakePermissions(params string[] granted) : IPermissionStore
{
    public bool IsGranted(string permission) => granted.Contains(permission);
    public IReadOnlyList<PermissionState> List() => [];
    public string? LoadProblem => null;
    public Task SetAsync(string permission, bool granted, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>A gateway wired to fakes, with tools at every tier.</summary>
public sealed class GatewayFixture
{
    public FakeTool Read { get; } = new("fake_read", RiskTier.Read);
    public FakeTool Reversible { get; } = new("fake_reversible", RiskTier.Reversible);
    public FakeTool Impactful { get; } = new("fake_impactful", RiskTier.Impactful);
    public FakeTool Destructive { get; } = new("fake_destructive", RiskTier.Destructive);
    public FakeTool Forbidden { get; } = new("fake_forbidden", RiskTier.Forbidden);
    public FakeTool Locked { get; } = new("fake_locked", RiskTier.Read, "test.denied");

    public FakeBroker Broker { get; } = new();
    public InMemoryAuditLog Audit { get; } = new();
    public FakeTimeProvider Time { get; } = new();
    public ToolGateway Gateway { get; }

    public GatewayFixture()
    {
        var catalog = new PermissionCatalog(
        [
            new PermissionDefinition("test.allowed", "Allowed", true),
            new PermissionDefinition("test.denied", "Denied", false),
        ]);
        var registry = new ToolRegistry([Read, Reversible, Impactful, Destructive, Forbidden, Locked], catalog);
        Gateway = new ToolGateway(
            registry, new FakePermissions("test.allowed"), [new TierPolicy(), new TaintPolicy()],
            Broker, Audit, Time, NullLogger<ToolGateway>.Instance);
    }

    public static ToolCall Call(string tool, string json = """{"text":"hello"}""") =>
        new(tool, JsonDocument.Parse(json).RootElement.Clone(), Guid.NewGuid().ToString("N"));

    public static CallContext User { get; } = new(CallOrigin.User, []);

    public Task<ToolOutcome> InvokeAsync(string tool, string json = """{"text":"hello"}""", CallContext? context = null) =>
        Gateway.InvokeAsync(Call(tool, json), context ?? User, CancellationToken.None);
}
