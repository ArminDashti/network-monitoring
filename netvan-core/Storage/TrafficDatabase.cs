namespace Netvan.Storage;

internal static class TrafficDatabase
{
    public static bool DeleteFiles(string databasePath)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(databasePath));
        var removedAny = false;

        foreach (var path in SqliteSidecarPaths(fullPath))
        {
            if (!File.Exists(path))
                continue;

            File.Delete(path);
            removedAny = true;
        }

        return removedAny;
    }

    public static IEnumerable<string> SqliteSidecarPaths(string databasePath)
    {
        yield return databasePath;
        yield return databasePath + "-wal";
        yield return databasePath + "-shm";
        yield return databasePath + "-journal";
    }
}
