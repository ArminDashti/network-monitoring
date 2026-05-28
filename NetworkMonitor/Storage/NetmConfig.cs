using System.Globalization;
using System.Text.RegularExpressions;

namespace NetworkMonitor.Storage;

internal sealed class NetmConfig
{
    public string DatabasePath { get; init; } = "";
    public bool MonitoringEnabled { get; init; } = true;
    public int SamplingIntervalSeconds { get; init; } = 5;
    public int MaxSizeMb { get; init; } = 500;
    public int RetentionDays { get; init; } = 30;
    public string LogLevel { get; init; } = "Info";
    public string LogFile { get; init; } = "";

    public string ResolvedDatabasePath => NetmPaths.ResolveDatabasePath(DatabasePath);

    public string ResolvedLogFile =>
        string.IsNullOrWhiteSpace(LogFile)
            ? Path.Combine(NetmPaths.Home, "netm.log")
            : NetmPaths.Expand(LogFile);

    public static NetmConfig Load()
    {
        var defaults = CreateDefaults();
        if (!File.Exists(NetmPaths.ConfigFile))
            return defaults;

        try
        {
            return Parse(File.ReadAllLines(NetmPaths.ConfigFile), defaults);
        }
        catch
        {
            return defaults;
        }
    }

    private static NetmConfig CreateDefaults()
    {
        return new NetmConfig
        {
            DatabasePath = Path.Combine(NetmPaths.Home, "traffic.db"),
            MonitoringEnabled = true,
            SamplingIntervalSeconds = 5,
            MaxSizeMb = 500,
            RetentionDays = 30,
            LogLevel = "Info",
            LogFile = Path.Combine(NetmPaths.Home, "netm.log"),
        };
    }

    private static NetmConfig Parse(string[] lines, NetmConfig defaults)
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

        return new NetmConfig
        {
            DatabasePath = Get(values, "database_path", defaults.DatabasePath),
            MonitoringEnabled = GetBool(values, "monitoring.enabled", defaults.MonitoringEnabled),
            SamplingIntervalSeconds = Math.Max(1, GetInt(values, "monitoring.sampling_interval", defaults.SamplingIntervalSeconds)),
            MaxSizeMb = Math.Max(1, GetInt(values, "storage.max_size_mb", defaults.MaxSizeMb)),
            RetentionDays = Math.Max(1, GetInt(values, "storage.retention_days", defaults.RetentionDays)),
            LogLevel = Get(values, "logging.level", defaults.LogLevel),
            LogFile = Get(values, "logging.log_file", defaults.LogFile),
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
