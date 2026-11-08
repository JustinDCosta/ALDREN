using System.Text.Json;

namespace Aldren.Security.Tools;

public abstract class Tool<TArgs, TResult> : ITool where TArgs : class
{
    private ToolDescriptor? _descriptor;

    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract RiskTier Tier { get; }

    public abstract string Permission { get; }

    public virtual TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public ToolDescriptor Descriptor =>
        _descriptor ??= new ToolDescriptor(Name, Description, Tier, Permission, Timeout, ArgumentBinder.SchemaFor<TArgs>());

    /// <summary>Checks beyond the schema.</summary>
    public virtual IEnumerable<string> Validate(TArgs args) => [];

    /// <summary>A higher tier for particular arguments; the gateway ignores anything lower than <see cref="Tier"/>.</summary>
    public virtual RiskTier TierFor(TArgs args) => Tier;

    /// <summary>The action in plain words, for a confirmation.</summary>
    public virtual string Summarize(TArgs args) => Description;

    protected abstract Task<TResult> ExecuteAsync(TArgs args, CancellationToken ct);

    /// <summary>The post-condition; true by default.</summary>
    protected virtual Task<bool> VerifyAsync(TArgs args, TResult result, CancellationToken ct) => Task.FromResult(true);

    BindResult<object> ITool.Bind(JsonElement arguments)
    {
        var bound = ArgumentBinder.Bind<TArgs>(arguments);
        if (!bound.Success)
        {
            return BindResult<object>.Fail(bound.Errors);
        }

        var errors = Validate(bound.Value!).ToList();
        return errors.Count > 0 ? BindResult<object>.Fail(errors) : BindResult<object>.Ok(bound.Value!);
    }

    string ITool.Summarize(object arguments) => Summarize((TArgs)arguments);

    RiskTier ITool.TierFor(object arguments) => TierFor((TArgs)arguments);

    async Task<object?> ITool.RunAsync(object arguments, CancellationToken ct) => await ExecuteAsync((TArgs)arguments, ct);

    Task<bool> ITool.VerifyAsync(object arguments, object? result, CancellationToken ct) =>
        VerifyAsync((TArgs)arguments, (TResult)result!, ct);
}
