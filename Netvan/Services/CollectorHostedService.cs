#if WINDOWS

using Microsoft.Extensions.Hosting;

using Microsoft.Extensions.Logging;

using Netvan.Storage;



namespace Netvan.Services;



internal sealed class CollectorHostedService : BackgroundService

{

    private readonly NetmConfig _config;

    private readonly ILogger<CollectorHostedService> _logger;



    public CollectorHostedService(NetmConfig config, ILogger<CollectorHostedService> logger)

    {

        _config = config;

        _logger = logger;

    }



    protected override async Task ExecuteAsync(CancellationToken stoppingToken)

    {

        _logger.LogInformation(

            "NetM collector started. Database={DatabasePath}, interval={IntervalSeconds}s",

            _config.ResolvedDatabasePath,

            _config.SamplingIntervalSeconds);



        try

        {

            using var loop = new CollectionLoop(_config);

            await loop.RunAsync(stoppingToken);

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

