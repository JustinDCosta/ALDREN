using System.IO.Pipes;
using System.Security.Principal;

namespace Aldren.Contracts.Rpc;

/// <summary>
/// The named pipe between Core and the App. Both ends use <see cref="PipeOptions.CurrentUserOnly"/>: the server's ACL
/// admits only your account, and the client checks that the server belongs to you.
/// </summary>
public static class AldrenPipe
{
    public static string Name { get; } = "aldren-core-" + WindowsIdentity.GetCurrent().User!.Value;

    private const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    /// <summary>One instance: a second client can't connect while the App is connected.</summary>
    public static NamedPipeServerStream CreateServer(string? name = null) =>
        new(name ?? Name, PipeDirection.InOut, maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte, Options);

    public static NamedPipeClientStream CreateClient(string? name = null) =>
        new(".", name ?? Name, PipeDirection.InOut, Options);
}
