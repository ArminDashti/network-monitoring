#if WINDOWS
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetworkMonitor.Services;

internal sealed class CollectorHostedService : BackgroundService
{
    private readonly CollectorOptions _options;
    private readonly ILogger<CollectorHostedService> _logger;

    public CollectorHostedService(IOptions<CollectorOptions> options, ILogger<CollectorHostedService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NetM collector started. Database={DatabasePath}, interval={IntervalSeconds}s",
            Path.GetFullPath(_options.DatabasePath),
            _options.IntervalSeconds);

        try
        {
            await TrafficCollectionRunner.RunAsync(
                _options.DatabasePath,
                _options.IntervalSeconds,
                stoppingToken,
                logInfo: message => _logger.LogInformation("{Message}", message),
                logWarning: message => _logger.LogWarning("{Message}", message));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("NetM collector stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NetM collector failed.");
            throw;
        }
    }
}
#endif
