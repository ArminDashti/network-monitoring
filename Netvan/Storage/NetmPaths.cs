namespace Netvan.Storage;

internal static class NetmPaths
{
    public static string Home =>
        Environment.GetEnvironmentVariable("NETM_HOME")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetM");

    public static string ConfigFile => Path.Combine(Home, "configs.toml");

    public static string PidFile => Path.Combine(Home, "netm.pid");

    public static string SettingsFile => Path.Combine(Home, "settings.json");

    public static string Expand(string path)
    {
        var home = Home;
        return path
            .Replace("%NETM_HOME%", home, StringComparison.OrdinalIgnoreCase)
            .Replace("$NETM_HOME", home, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveDatabasePath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return Path.Combine(Home, "traffic.db");

        return Path.GetFullPath(Expand(configuredPath.Trim().Trim('"')));
    }
}
