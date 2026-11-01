using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Aldren.Security.Tools;

public sealed record BindResult<T>(T? Value, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;

    public static BindResult<T> Ok(T value) => new(value, []);

    public static BindResult<T> Fail(IReadOnlyList<string> errors) => new(default, errors);
}

/// <summary>
/// Turns JSON arguments into a tool's record. Unknown properties, missing required ones, wrong types and nulls where
/// the type isn't nullable are all errors.
/// </summary>
public static class ArgumentBinder
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static BindResult<T> Bind<T>(JsonElement json) where T : class
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return BindResult<T>.Fail([$"Arguments must be a JSON object, not {json.ValueKind}."]);
        }

        try
        {
            var value = json.Deserialize<T>(Options);
            return value is null ? BindResult<T>.Fail(["Arguments are missing."]) : BindResult<T>.Ok(value);
        }
        catch (JsonException ex)
        {
            return BindResult<T>.Fail([ex.Message]);
        }
    }

    public static JsonNode SchemaFor<T>() =>
        Options.GetJsonSchemaAsNode(typeof(T), new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }
}
