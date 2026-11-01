using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Credentials;

namespace Aldren.Security.Secrets;

/// <summary>
/// Generic credentials named <c>Aldren/&lt;name&gt;</c>, kept on this PC only. Values are UTF-8, at most 2,560 bytes.
/// </summary>
public sealed partial class CredentialManagerSecretStore : ISecretStore
{
    public const string Prefix = "Aldren/";
    public const int MaxValueBytes = 2_560;

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$")]
    private static partial Regex ValidName();

    public unsafe string? Get(string name)
    {
        var target = Target(name);
        if (!PInvoke.CredRead(target, CRED_TYPE.CRED_TYPE_GENERIC, out var credential))
        {
            var error = (WIN32_ERROR)Marshal.GetLastWin32Error();
            return error == WIN32_ERROR.ERROR_NOT_FOUND ? null : throw Failure("read", error);
        }

        try
        {
            return Encoding.UTF8.GetString(credential->CredentialBlob, (int)credential->CredentialBlobSize);
        }
        finally
        {
            PInvoke.CredFree(credential);
        }
    }

    public unsafe void Set(string name, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var target = Target(name);
        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length > MaxValueBytes)
        {
            throw new ArgumentException($"A secret can be at most {MaxValueBytes} bytes of UTF-8.", nameof(secret));
        }

        try
        {
            fixed (char* targetName = target)
            fixed (char* userName = "Aldren")
            fixed (byte* blob = bytes)
            {
                var credential = new CREDENTIALW
                {
                    Type = CRED_TYPE.CRED_TYPE_GENERIC,
                    TargetName = targetName,
                    UserName = userName,
                    CredentialBlob = blob,
                    CredentialBlobSize = (uint)bytes.Length,
                    Persist = CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE,
                };
                if (!PInvoke.CredWrite(&credential, 0))
                {
                    throw Failure("write", (WIN32_ERROR)Marshal.GetLastWin32Error());
                }
            }
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    public bool Delete(string name)
    {
        if (PInvoke.CredDelete(Target(name), CRED_TYPE.CRED_TYPE_GENERIC))
        {
            return true;
        }

        var error = (WIN32_ERROR)Marshal.GetLastWin32Error();
        return error == WIN32_ERROR.ERROR_NOT_FOUND ? false : throw Failure("delete", error);
    }

    private static string Target(string name)
    {
        if (name is null || !ValidName().IsMatch(name))
        {
            throw new ArgumentException($"Secret names must match {ValidName()}.", nameof(name));
        }

        return Prefix + name;
    }

    // The name, never the value: this message can reach a log.
    private static InvalidOperationException Failure(string action, WIN32_ERROR error) =>
        new($"Credential Manager couldn't {action} the secret (Win32 error {(int)error}).");
}
