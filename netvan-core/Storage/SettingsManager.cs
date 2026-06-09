namespace Netvan.Storage;

internal static class SettingsManager
{
    private const string EnvVarName = "NETVAN_DATA_PATH";

    public static void Initialize()
    {
        var home = NetvanPaths.Home;
        Directory.CreateDirectory(home);
        EnsureDefaultConfig(home);
        NetvanConfig.MigrateLegacySettings();

        var config = NetvanConfig.Load();
        var dbPath = config.ResolvedDatabasePath;

        if (!File.Exists(dbPath))
        {
            using var store = new TrafficStore(dbPath);
        }

        Environment.SetEnvironmentVariable(EnvVarName, home, EnvironmentVariableTarget.Process);
    }

    public static string GetSettingsPath() => NetvanPaths.ConfigFile;

    public static string GetDefaultDatabasePath() => NetvanConfig.Load().ResolvedDatabasePath;

    public static string GetDatabasePath() => NetvanConfig.Load().ResolvedDatabasePath;

    public static string? GetDataPathEnvVar() => Environment.GetEnvironmentVariable(EnvVarName);

    private static void EnsureDefaultConfig(string home)
    {
        var dest = Path.Combine(home, "configs.toml");
        if (File.Exists(dest))
            return;

        var bundled = Path.Combine(AppContext.BaseDirectory, "configs.toml");
        if (File.Exists(bundled))
        {
            File.Copy(bundled, dest);
            return;
        }

        NetvanConfig.Load().Save();
    }
}
