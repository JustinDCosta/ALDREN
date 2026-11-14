using System.Security.AccessControl;
using System.Security.Principal;

namespace Aldren.Security.Audit;

public static class DataFolderAcl
{
    /// <summary>
    /// A protected ACL giving only your account full control, inherited by everything inside. Returns why it couldn't be
    /// applied (a folder someone else owns, a drive without ACLs) instead of throwing: ALDREN still runs, and says so in
    /// its log.
    /// </summary>
    public static string? RestrictToCurrentUser(string directory)
    {
        var info = Directory.CreateDirectory(directory);
        try
        {
            var user = WindowsIdentity.GetCurrent().User!;

            // The owner stays as it is: changing it needs WRITE_OWNER, which the folder's creator doesn't have unless the
            // parent grants full control, and the access rules are what keep others out.
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(security);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
