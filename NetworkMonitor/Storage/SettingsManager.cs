using System.Text.Json;

namespace NetworkMonitor.Storage;

internal static class SettingsManager
{
    private const string EnvVarName = "NETM_DATA_PATH";

    public static void Initialize()
    {
        var home = NetmPaths.Home;
        Directory.CreateDirectory(home);
        EnsureDefaultConfig(home);

        var config = NetmConfig.Load();
        var settingsPath = NetmPaths.SettingsFile;
        var dbPath = config.ResolvedDatabasePath;

        if (!File.Exists(settingsPath))
        {
            var defaultSettings = new
            {
                databasePath = dbPath,
                createdAt = DateTime.UtcNow.ToString("O"),
                version = "1.0",
            };

            var json = JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(settingsPath, json);
        }

        if (!File.Exists(dbPath))
        {
            using var store = new TrafficStore(dbPath);
        }

        Environment.SetEnvironmentVariable(EnvVarName, home, EnvironmentVariableTarget.Process);
    }

    public static string GetSettingsPath() => NetmPaths.SettingsFile;

    public static string GetDefaultDatabasePath() => NetmConfig.Load().ResolvedDatabasePath;

    public static string GetDatabasePath() => NetmConfig.Load().ResolvedDatabasePath;

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

        File.WriteAllText(dest, """
            database_path = "%NETM_HOME%\\traffic.db"

            [monitoring]
            enabled = true
            sampling_interval = 1

            [storage]
            max_size_mb = 500
            retention_days = 30

            [logging]
            level = "Info"
            log_file = "%NETM_HOME%\\netm.log"
            """);
    }
}
