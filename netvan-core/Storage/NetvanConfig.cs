using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Netvan.Storage;

internal sealed class NetvanConfig
{
    public string DatabasePath { get; init; } = "";
    public bool DisableVpnTracking { get; init; }
    public int MaxSizeMb { get; init; } = 500;
    public int RetentionDays { get; init; } = 30;
    public string LogLevel { get; init; } = "Info";
    public string LogFile { get; init; } = "";
    public bool TaskbarEnabled { get; init; }

    public string ResolvedDatabasePath => NetvanPaths.ResolveDatabasePath(DatabasePath);

    public string ResolvedLogFile =>
        string.IsNullOrWhiteSpace(LogFile)
            ? Path.Combine(NetvanPaths.Home, "netvan.log")
            : NetvanPaths.Expand(LogFile);

    public static NetvanConfig Load()
    {
        var defaults = CreateDefaults();
        if (!File.Exists(NetvanPaths.ConfigFile))
            return defaults;

        try
        {
            return Parse(File.ReadAllLines(NetvanPaths.ConfigFile), defaults);
        }
        catch
        {
            return defaults;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(NetvanPaths.Home);
        File.WriteAllText(NetvanPaths.ConfigFile, ToToml(), Encoding.UTF8);
    }

    public static void MigrateLegacySettings()
    {
        var legacyPath = Path.Combine(NetvanPaths.Home, "settings.json");
        if (!File.Exists(legacyPath))
            return;

        var config = Load();
        try
        {
            var json = File.ReadAllText(legacyPath);
            var node = JsonNode.Parse(json)?.AsObject();
            if (node is null)
                return;

            var taskbarEnabled = node["taskbarEnabled"]?.GetValue<bool>() ?? config.TaskbarEnabled;
            var databasePath = node["databasePath"]?.GetValue<string>();

            config = new NetvanConfig
            {
                DatabasePath = string.IsNullOrWhiteSpace(databasePath) ? config.DatabasePath : databasePath,
                DisableVpnTracking = config.DisableVpnTracking,
                MaxSizeMb = config.MaxSizeMb,
                RetentionDays = config.RetentionDays,
                LogLevel = config.LogLevel,
                LogFile = config.LogFile,
                TaskbarEnabled = taskbarEnabled,
            };
            config.Save();
        }
        catch
        {
            return;
        }

        try
        {
            File.Delete(legacyPath);
        }
        catch
        {
            // Migration succeeded; stale JSON can be removed manually.
        }
    }

    public NetvanConfig WithCollectionSettings(string databasePath) =>
        new()
        {
            DatabasePath = databasePath,
            DisableVpnTracking = DisableVpnTracking,
            MaxSizeMb = MaxSizeMb,
            RetentionDays = RetentionDays,
            LogLevel = LogLevel,
            LogFile = LogFile,
            TaskbarEnabled = TaskbarEnabled,
        };

    public NetvanConfig WithTaskbarEnabled(bool enabled) =>
        new()
        {
            DatabasePath = DatabasePath,
            DisableVpnTracking = DisableVpnTracking,
            MaxSizeMb = MaxSizeMb,
            RetentionDays = RetentionDays,
            LogLevel = LogLevel,
            LogFile = LogFile,
            TaskbarEnabled = enabled,
        };

    private static NetvanConfig CreateDefaults()
    {
        return new NetvanConfig
        {
            DatabasePath = Path.Combine(NetvanPaths.Home, "traffic.db"),
            DisableVpnTracking = false,
            MaxSizeMb = 500,
            RetentionDays = 30,
            LogLevel = "Info",
            LogFile = Path.Combine(NetvanPaths.Home, "netvan.log"),
            TaskbarEnabled = false,
        };
    }

    private string ToToml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Netvan Configuration");
        sb.AppendLine();
        sb.AppendLine("# Database path (default: %LocalAppData%\\Netvan\\traffic.db)");
        sb.AppendLine($"database_path = {TomlString(DatabasePath)}");
        sb.AppendLine();
        sb.AppendLine("# Monitoring settings");
        sb.AppendLine("[monitoring]");
        sb.AppendLine("# Exclude traffic over VPN network adapters");
        sb.AppendLine($"disable_vpn_tracking = {TomlBool(DisableVpnTracking)}");
        sb.AppendLine();
        sb.AppendLine("# Storage settings");
        sb.AppendLine("[storage]");
        sb.AppendLine("# Maximum database size in MB");
        sb.AppendLine($"max_size_mb = {MaxSizeMb}");
        sb.AppendLine("# Retention period in days");
        sb.AppendLine($"retention_days = {RetentionDays}");
        sb.AppendLine();
        sb.AppendLine("# Logging settings");
        sb.AppendLine("[logging]");
        sb.AppendLine("# Log level: Debug, Info, Warning, Error");
        sb.AppendLine($"level = {TomlString(LogLevel)}");
        sb.AppendLine("# Log file path");
        sb.AppendLine($"log_file = {TomlString(LogFile)}");
        sb.AppendLine();
        sb.AppendLine("# Taskbar widget");
        sb.AppendLine("[taskbar]");
        sb.AppendLine("# Show upload/download speeds in the Windows taskbar");
        sb.AppendLine($"enabled = {TomlBool(TaskbarEnabled)}");
        return sb.ToString();
    }

    private static string TomlString(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string TomlBool(bool value) => value ? "true" : "false";

    private static NetvanConfig Parse(string[] lines, NetvanConfig defaults)
    {
        var section = "";
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0)
                continue;

            var sectionMatch = Regex.Match(line, @"^\[(.+)\]$");
            if (sectionMatch.Success)
            {
                section = sectionMatch.Groups[1].Value.Trim();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = Unquote(line[(eq + 1)..].Trim());
            var fullKey = string.IsNullOrEmpty(section) ? key : $"{section}.{key}";
            values[fullKey] = value;
        }

        return new NetvanConfig
        {
            DatabasePath = Get(values, "database_path", defaults.DatabasePath),
            DisableVpnTracking = GetBool(values, "monitoring.disable_vpn_tracking", defaults.DisableVpnTracking),
            MaxSizeMb = Math.Max(1, GetInt(values, "storage.max_size_mb", defaults.MaxSizeMb)),
            RetentionDays = Math.Max(1, GetInt(values, "storage.retention_days", defaults.RetentionDays)),
            LogLevel = Get(values, "logging.level", defaults.LogLevel),
            LogFile = Get(values, "logging.log_file", defaults.LogFile),
            TaskbarEnabled = GetBool(values, "taskbar.enabled", defaults.TaskbarEnabled),
        };
    }

    private static string StripComment(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
                inQuotes = !inQuotes;
            else if (!inQuotes && line[i] == '#')
                return line[..i];
        }

        return line;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : fallback;

    private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool fallback)
    {
        if (!values.TryGetValue(key, out var v))
            return fallback;

        if (bool.TryParse(v, out var b))
            return b;

        return v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v == "1";
    }
}
