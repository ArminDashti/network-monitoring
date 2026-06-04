#if WINDOWS
using NetworkMonitor.Storage;

namespace NetworkMonitor.Services;

internal static class TrafficCollectionRunner
{
    public static async Task RunAsync(
        string dbPath,
        int intervalSeconds,
        CancellationToken cancellationToken,
        Action<string>? logInfo = null,
        Action<string>? logWarning = null)
    {
        if (intervalSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Interval must be at least 1 second.");

        using var store = new TrafficStore(dbPath);
        var collector = new TrafficCollector(new NicResolver(), new HostNameCache());

        logInfo?.Invoke($"Collecting TCP traffic → {Path.GetFullPath(dbPath)}");
        logInfo?.Invoke($"Interval: {intervalSeconds}s.");

        var emptySamples = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var deltas = collector.CollectDeltas();
            if (deltas.Count == 0)
            {
                emptySamples++;
                if (emptySamples == 5)
                {
                    logWarning?.Invoke(
                        "No traffic recorded yet. Run the service as Local System with sufficient rights, or check elevation if usage stays empty.");
                    emptySamples = 0;
                }
            }
            else
            {
                emptySamples = 0;
            }

            store.ApplyDeltas(deltas);
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
        }
    }
}
#endif
