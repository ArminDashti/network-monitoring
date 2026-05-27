using System.Text.Json;

namespace NetworkMonitor.Storage;

internal static class SettingsManager
{
    private const string SettingsFileName = "settings.json";
    private const string DatabaseFileName = "traffic.db";
    private const string DataDirEnvVar = "NETM_HOME";
    private const string DataPathEnvVar = "NETM_DATA_PATH";

    public static string GetDefaultDataDirectory()
    {
        var fromEnv = Environment.GetEnvironmentVariable(DataDirEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetM");
    }

    public static string GetDefaultDatabasePath() =>
        Path.Combine(GetDefaultDataDirectory(), DatabaseFileName);

    public static void Initialize()
    {
        var dataDir = GetDefaultDataDirectory();
        Directory.CreateDirectory(dataDir);

        var settingsPath = Path.Combine(dataDir, SettingsFileName);
        var dbPath = GetDefaultDatabasePath();

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

        Environment.SetEnvironmentVariable(DataPathEnvVar, dataDir, EnvironmentVariableTarget.Process);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataDirEnvVar)))
            Environment.SetEnvironmentVariable(DataDirEnvVar, dataDir, EnvironmentVariableTarget.Process);
    }

    public static string GetSettingsPath() =>
        Path.Combine(GetDefaultDataDirectory(), SettingsFileName);

    public static string? GetDataPathEnvVar() =>
        Environment.GetEnvironmentVariable(DataPathEnvVar);
}
