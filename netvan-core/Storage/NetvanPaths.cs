namespace Netvan.Storage;

internal static class NetvanPaths
{
    public static string Home
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("NETVAN_HOME");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                var exeDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrWhiteSpace(exeDir))
                    return exeDir;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Netvan");
        }
    }

    public static string ConfigFile => Path.Combine(Home, "configs.toml");

    public static string Expand(string path)
    {
        var home = Home;
        return path
            .Replace("%NETVAN_HOME%", home, StringComparison.OrdinalIgnoreCase)
            .Replace("$NETVAN_HOME", home, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveDatabasePath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return Path.Combine(Home, "traffic.db");

        return Path.GetFullPath(Expand(configuredPath.Trim().Trim('"')));
    }
}
