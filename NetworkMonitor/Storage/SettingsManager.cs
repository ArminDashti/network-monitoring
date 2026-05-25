using System.Text.Json;

namespace NetworkMonitor.Storage;

internal static class SettingsManager
{
    private const string SettingsFileName = "settings.json";
    private const string DatabaseFileName = "traffic.db";
    private const string EnvVarName = "NETM_DATA_PATH";

    public static void Initialize()
    {
        var currentDir = Environment.CurrentDirectory;
        var settingsPath = Path.Combine(currentDir, SettingsFileName);
        var dbPath = Path.Combine(currentDir, DatabaseFileName);

        // Create settings.json if it doesn't exist (no overwrite)
        if (!File.Exists(settingsPath))
        {
            var defaultSettings = new
            {
                databasePath = dbPath,
                createdAt = DateTime.UtcNow.ToString("O"),
                version = "1.0"
            };

            var json = JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(settingsPath, json);
        }

        // Create empty SQLite database if it doesn't exist (no overwrite)
        if (!File.Exists(dbPath))
        {
            using var store = new TrafficStore(dbPath);
            // TrafficStore constructor creates the schema automatically
        }

        // Set environment variable with the data path
        Environment.SetEnvironmentVariable(EnvVarName, currentDir, EnvironmentVariableTarget.Process);
    }

    public static string GetSettingsPath() => Path.Combine(Environment.CurrentDirectory, SettingsFileName);

    public static string GetDatabasePath() => Path.Combine(Environment.CurrentDirectory, DatabaseFileName);

    public static string? GetDataPathEnvVar() => Environment.GetEnvironmentVariable(EnvVarName);
}
