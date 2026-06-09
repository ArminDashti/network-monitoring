namespace Netvan.Storage;

internal static class NetvanLog
{
    private static readonly object Gate = new();
    private static NetvanConfig? _config;

    public static void Configure(NetvanConfig config) => _config = config;

    public static void Info(string message) => Write("Info", message);

    public static void Warning(string message) => Write("Warning", message);

    public static void Error(string message) => Write("Error", message);

    public static void Debug(string message) => Write("Debug", message);

    private static void Write(string level, string message)
    {
        var config = _config ?? NetvanConfig.Load();
        if (!ShouldLog(level, config.LogLevel))
            return;

        var line = $"{DateTime.UtcNow:O} [{level}] {message}{Environment.NewLine}";
        var path = config.ResolvedLogFile;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? NetvanPaths.Home);
            lock (Gate)
                File.AppendAllText(path, line);
        }
        catch
        {
            // Logging must not crash the collector.
        }
    }

    private static bool ShouldLog(string level, string configured)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Debug"] = 0,
            ["Info"] = 1,
            ["Warning"] = 2,
            ["Error"] = 3,
        };

        if (!order.TryGetValue(configured.Trim(), out var min))
            min = 1;
        if (!order.TryGetValue(level, out var current))
            current = 1;

        return current >= min;
    }
}
