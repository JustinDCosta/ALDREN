using Microsoft.Win32;

namespace Aldren.Core.Startup;

public sealed class CurrentUserRegistry : IRegistryValues
{
    public object? Get(string key, string name)
    {
        using var subKey = Registry.CurrentUser.OpenSubKey(key);
        return subKey?.GetValue(name);
    }

    public void Set(string key, string name, string value)
    {
        using var subKey = Registry.CurrentUser.CreateSubKey(key, writable: true);
        subKey.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string key, string name)
    {
        using var subKey = Registry.CurrentUser.OpenSubKey(key, writable: true);
        subKey?.DeleteValue(name, throwOnMissingValue: false);
    }
}
