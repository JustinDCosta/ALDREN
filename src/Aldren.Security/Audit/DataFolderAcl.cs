using System.Security.AccessControl;
using System.Security.Principal;

namespace Aldren.Security.Audit;

public static class DataFolderAcl
{
    /// <summary>A protected ACL giving only your account full control, inherited by everything inside.</summary>
    public static void RestrictToCurrentUser(string directory)
    {
        var info = Directory.CreateDirectory(directory);
        var user = WindowsIdentity.GetCurrent().User!;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);
    }
}
