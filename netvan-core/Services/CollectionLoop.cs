#if WINDOWS
using Netvan.Storage;

namespace Netvan.Services;

internal sealed class CollectionLoop : IDisposable
{
    private readonly string _serviceDatabasePath;
    private NetvanConfig _config;
    private readonly TrafficStore _store;
    private readonly TrafficCollector _collector;
    private DateTime _lastMaintenanceUtc = DateTime.MinValue;
    private FileSystemWatcher? _configWatcher;
    private volatile bool _configDirty;

    public CollectionLoop(NetvanConfig config)
    {
        _serviceDatabasePath = config.ResolvedDatabasePath;
        _config = config;
        _store = new TrafficStore(_serviceDatabasePath);
        _collector = new TrafficCollector(new NicResolver(), new HostNameCache())
        {
            DisableVpnTracking = config.DisableVpnTracking,
        };
        NetvanLog.Configure(_config);
        WatchConfigFile();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        NetvanLog.Info("Collector started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            ApplyReloadedConfig(NetvanConfig.Load());

            try
            {
                var deltas = _collector.CollectDeltas();
                if (deltas.Count > 0)
                    _store.ApplyDeltas(deltas);

                MaybeRunMaintenance();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                NetvanLog.Error($"Sample failed: {ex.Message}");
            }

            await DelayWithConfigReloadAsync(cancellationToken);
        }

        NetvanLog.Info("Collector stopped.");
    }

    private async Task DelayWithConfigReloadAsync(CancellationToken cancellationToken)
    {
        var remaining = TimeSpan.FromSeconds(1);

        while (remaining > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
        {
            var step = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
            await Task.Delay(step, cancellationToken);
            remaining -= step;

            if (_configDirty)
            {
                ReloadConfigIfNeeded();
                remaining = TimeSpan.FromSeconds(1);
            }
        }
    }

    private void WatchConfigFile()
    {
        var configPath = NetvanPaths.ConfigFile;
        var directory = Path.GetDirectoryName(configPath);
        var fileName = Path.GetFileName(configPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            return;

        try
        {
            Directory.CreateDirectory(directory);
            _configWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _configWatcher.Changed += (_, _) => _configDirty = true;
            _configWatcher.Created += (_, _) => _configDirty = true;
            _configWatcher.Renamed += (_, _) => _configDirty = true;
        }
        catch (Exception ex)
        {
            NetvanLog.Warning($"Config watcher unavailable: {ex.Message}");
        }
    }

    private void ReloadConfigIfNeeded()
    {
        if (!_configDirty)
            return;

        _configDirty = false;
        ApplyReloadedConfig(NetvanConfig.Load());
    }

    private void ApplyReloadedConfig(NetvanConfig latest)
    {
        var previousVpnTracking = _config.DisableVpnTracking;
        _config = new NetvanConfig
        {
            DatabasePath = _config.DatabasePath,
            DisableVpnTracking = latest.DisableVpnTracking,
            MaxSizeMb = latest.MaxSizeMb,
            RetentionDays = latest.RetentionDays,
            LogLevel = latest.LogLevel,
            LogFile = latest.LogFile,
            TaskbarEnabled = latest.TaskbarEnabled,
        };
        _collector.DisableVpnTracking = _config.DisableVpnTracking;
        NetvanLog.Configure(_config);

        if (_config.DisableVpnTracking != previousVpnTracking)
        {
            NetvanLog.Info(_config.DisableVpnTracking
                ? "VPN tracking disabled from configuration."
                : "VPN tracking enabled from configuration.");
        }
    }

    private void MaybeRunMaintenance()
    {
        var now = DateTime.UtcNow;
        if (now - _lastMaintenanceUtc < TimeSpan.FromHours(1))
            return;

        _lastMaintenanceUtc = now;
        try
        {
            var cutoff = TrafficStore.FormatUtcIso(now.AddDays(-_config.RetentionDays));
            var deleted = _store.PruneOlderThanUtc(cutoff);
            if (deleted > 0)
                NetvanLog.Info($"Pruned {deleted:N0} rows older than {_config.RetentionDays} days.");

            EnforceMaxDatabaseSize();
        }
        catch (Exception ex)
        {
            NetvanLog.Warning($"Maintenance failed: {ex.Message}");
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
        NetvanLog.Warning($"Database exceeded {_config.MaxSizeMb} MB; pruned ~{deleted:N0} oldest rows and vacuumed.");
    }

    public void Dispose()
    {
        _configWatcher?.Dispose();
        _store.Dispose();
    }
}
#endif
