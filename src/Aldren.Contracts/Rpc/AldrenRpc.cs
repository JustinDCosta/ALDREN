using System.Text.Json;
using System.Text.Json.Serialization;
using StreamJsonRpc;

namespace Aldren.Contracts.Rpc;

public static class AldrenRpc
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    /// <summary>Header-delimited JSON-RPC over <paramref name="stream"/>, serving <paramref name="localTarget"/>. The caller starts listening.</summary>
    public static JsonRpc Attach(Stream stream, object localTarget)
    {
        var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = SerializerOptions };
        var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(stream, stream, formatter));
        rpc.AddLocalRpcTarget(localTarget, new JsonRpcTargetOptions { DisposeOnDisconnect = false });
        return rpc;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
