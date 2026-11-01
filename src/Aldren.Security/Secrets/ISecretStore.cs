namespace Aldren.Security.Secrets;

/// <summary>Secrets live in Windows Credential Manager, never in files, logs or prompts.</summary>
public interface ISecretStore
{
    string? Get(string name);

    void Set(string name, string secret);

    /// <summary>False when there was nothing to delete.</summary>
    bool Delete(string name);
}
