#if WINDOWS

using Microsoft.Extensions.Hosting;

using Microsoft.Extensions.Logging;

using Netvan.Storage;



namespace Netvan.Services;



internal sealed class CollectorHostedService : BackgroundService

{

    private readonly NetvanConfig _config;

    private readonly ILogger<CollectorHostedService> _logger;



    public CollectorHostedService(NetvanConfig config, ILogger<CollectorHostedService> logger)

    {

        _config = config;

        _logger = logger;

    }



    protected override async Task ExecuteAsync(CancellationToken stoppingToken)

    {

        _logger.LogInformation(
            "Netvan collector started. Database={DatabasePath}",
            _config.ResolvedDatabasePath);



        try

        {

            using var loop = new CollectionLoop(_config);

            await loop.RunAsync(stoppingToken);

        }

        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)

        {

            _logger.LogInformation("Netvan collector stopped.");

        }

        catch (Exception ex)

        {

            _logger.LogError(ex, "Netvan collector failed.");

            throw;

        }

    }

}

#endif

