namespace Netvan.Storage;

internal static class TrafficDatabase
{
    private const int DeleteRetryCount = 10;
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(500);

    public static bool DeleteFiles(string databasePath) =>
        TryDeleteFiles(databasePath, out _);

    public static bool TryDeleteFiles(string databasePath, out string? errorMessage)
    {
        errorMessage = null;
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(databasePath));
        var hadFiles = false;
        var removedAny = false;

        foreach (var path in SqliteSidecarPaths(fullPath))
        {
            if (!File.Exists(path))
                continue;

            hadFiles = true;
            if (!TryDeleteFile(path, out errorMessage))
                return removedAny;

            removedAny = true;
        }

        errorMessage = null;
        return hadFiles ? removedAny : true;
    }

    private static bool TryDeleteFile(string path, out string? errorMessage)
    {
        errorMessage = null;
        for (var attempt = 1; attempt <= DeleteRetryCount; attempt++)
        {
            try
            {
                File.Delete(path);
                errorMessage = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errorMessage = ex.Message;
                if (attempt < DeleteRetryCount)
                    Thread.Sleep(DeleteRetryDelay);
            }
        }

        errorMessage ??= $"Could not delete {path}.";
        return false;
    }

    public static IEnumerable<string> SqliteSidecarPaths(string databasePath)
    {
        yield return databasePath;
        yield return databasePath + "-wal";
        yield return databasePath + "-shm";
        yield return databasePath + "-journal";
    }
}
