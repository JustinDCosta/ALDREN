namespace Aldren.Core.Startup;

/// <summary>Values under <c>HKEY_CURRENT_USER</c>; a seam so tests never touch the real registry.</summary>
public interface IRegistryValues
{
    object? Get(string key, string name);

    void Set(string key, string name, string value);

    void Delete(string key, string name);
}
