#if WINDOWS
using Netvan.Storage;

namespace Netvan.Services;

internal sealed class CollectionLoop : IDisposable
{
    private readonly NetmConfig _config;
    private readonly TrafficStore _store;
    private readonly TrafficCollector _collector;
    private DateTime _lastMaintenanceUtc = DateTime.MinValue;

    public CollectionLoop(NetmConfig config)
    {
        _config = config;
        _store = new TrafficStore(config.ResolvedDatabasePath);
        _collector = new TrafficCollector(new NicResolver(), new HostNameCache());
        NetmLog.Configure(config);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        NetmLog.Info($"Collector started (interval={_config.SamplingIntervalSeconds}s).");
        var interval = TimeSpan.FromSeconds(_config.SamplingIntervalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_config.MonitoringEnabled)
            {
                await Task.Delay(interval, cancellationToken);
                continue;
            }

            try
            {
                var deltas = _collector.CollectDeltas();
                if (deltas.Count > 0)
                    _store.ApplyDeltas(deltas);

                MaybeRunMaintenance();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                NetmLog.Error($"Sample failed: {ex.Message}");
            }

            await Task.Delay(interval, cancellationToken);
        }

        NetmLog.Info("Collector stopped.");
    }

    private void MaybeRunMaintenance()
    {
        var now = DateTime.UtcNow;
        if (now - _lastMaintenanceUtc < TimeSpan.FromHours(1))
            return;

        _lastMaintenanceUtc = now;
        try
        {
            var cutoff = now.AddDays(-_config.RetentionDays).ToString("O");
            var deleted = _store.PruneOlderThanUtc(cutoff);
            if (deleted > 0)
                NetmLog.Info($"Pruned {deleted:N0} rows older than {_config.RetentionDays} days.");

            EnforceMaxDatabaseSize();
        }
        catch (Exception ex)
        {
            NetmLog.Warning($"Maintenance failed: {ex.Message}");
        }
    }

    private void EnforceMaxDatabaseSize()
    {
        var dbPath = _config.ResolvedDatabasePath;
        if (!File.Exists(dbPath))
            return;

        var maxBytes = (long)_config.MaxSizeMb * 1024 * 1024;
        var size = new FileInfo(dbPath).Length;
        if (size <= maxBytes)
            return;

        var deleted = _store.PruneOldestFraction(0.10);
        _store.Vacuum();
        NetmLog.Warning($"Database exceeded {_config.MaxSizeMb} MB; pruned ~{deleted:N0} oldest rows and vacuumed.");
    }

    public void Dispose() => _store.Dispose();
}
#endif
