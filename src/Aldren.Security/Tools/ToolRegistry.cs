using System.Text.RegularExpressions;
using Aldren.Security.Permissions;

namespace Aldren.Security.Tools;

/// <summary>The tools ALDREN has. A bad definition fails at startup, not at the first call.</summary>
public sealed partial class ToolRegistry
{
    private static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(120);

    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    [GeneratedRegex("^[a-z][a-z0-9_]{2,47}$")]
    private static partial Regex ValidName();

    public ToolRegistry(IEnumerable<ITool> tools, PermissionCatalog catalog)
    {
        foreach (var tool in tools)
        {
            var d = tool.Descriptor;
            if (!ValidName().IsMatch(d.Name))
            {
                throw new ArgumentException($"Tool name '{d.Name}' must match {ValidName()}.", nameof(tools));
            }

            if (!catalog.Contains(d.Permission))
            {
                throw new ArgumentException($"Tool '{d.Name}' needs unknown permission '{d.Permission}'.", nameof(tools));
            }

            if (d.Timeout < MinTimeout || d.Timeout > MaxTimeout)
            {
                throw new ArgumentException($"Tool '{d.Name}' has a timeout of {d.Timeout}, outside 1–120 s.", nameof(tools));
            }

            if (!_tools.TryAdd(d.Name, tool))
            {
                throw new ArgumentException($"Tool '{d.Name}' is registered twice.", nameof(tools));
            }
        }
    }

    public IReadOnlyList<ToolDescriptor> Descriptors => _tools.Values.Select(t => t.Descriptor).ToList();

    public bool TryGet(string name, out ITool tool) => _tools.TryGetValue(name, out tool!);
}
