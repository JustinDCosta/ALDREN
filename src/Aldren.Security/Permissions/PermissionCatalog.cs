namespace Aldren.Security.Permissions;

public sealed record PermissionDefinition(string Name, string Description, bool GrantedByDefault);

/// <summary>The permissions ALDREN knows. Anything outside the catalog is denied.</summary>
public sealed class PermissionCatalog
{
    public const string SystemClock = "system.clock";
    public const string DiagnosticsTest = "diagnostics.test";

    private readonly Dictionary<string, PermissionDefinition> _definitions;

    public PermissionCatalog(IEnumerable<PermissionDefinition> definitions)
    {
        _definitions = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (!_definitions.TryAdd(definition.Name, definition))
            {
                throw new ArgumentException($"Permission '{definition.Name}' is defined twice.", nameof(definitions));
            }
        }
    }

    public static PermissionCatalog Phase1 { get; } = new(
    [
        new PermissionDefinition(SystemClock, "Read the system clock", GrantedByDefault: true),
        new PermissionDefinition(DiagnosticsTest, "Run the confirmation test", GrantedByDefault: true),
    ]);

    public IReadOnlyCollection<PermissionDefinition> Definitions => _definitions.Values;

    public bool Contains(string name) => _definitions.ContainsKey(name);

    public bool TryGet(string name, out PermissionDefinition definition) =>
        _definitions.TryGetValue(name, out definition!);
}
