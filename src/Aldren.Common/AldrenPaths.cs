namespace Aldren.Common;

/// <summary>Where ALDREN keeps its settings (roaming) and its database and logs (local). ARCHITECTURE §18.</summary>
public sealed record AldrenPaths(string SettingsDirectory, string DataDirectory)
{
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public string DatabasePath => Path.Combine(DataDirectory, "aldren.db");
    public string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");
    public string PermissionsFile => Path.Combine(DataDirectory, "permissions.json");

    /// <summary><c>ALDREN_HOME</c> moves both folders under one root, for tests and development.</summary>
    public static AldrenPaths FromEnvironment()
    {
        var home = Environment.GetEnvironmentVariable("ALDREN_HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return new AldrenPaths(Path.Combine(home, "settings"), Path.Combine(home, "data"));
        }

        return new AldrenPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aldren"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aldren"));
    }
}
