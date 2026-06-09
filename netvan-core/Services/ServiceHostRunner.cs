#if WINDOWS
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Netvan.Cli;
using Netvan.Storage;

namespace Netvan.Services;

internal static class ServiceHostRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        SettingsManager.Initialize();

        var dbPath = DefaultDbPath;
        var intervalSeconds = 5;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--db" or "-d" && i + 1 < args.Length)
            {
                dbPath = args[++i];
                continue;
            }

            if (args[i] is "--interval" or "-i" && i + 1 < args.Length && int.TryParse(args[++i], out var parsed))
            {
                intervalSeconds = parsed;
                continue;
            }
        }

        if (intervalSeconds < 1)
        {
            ConsoleUi.WriteError("Interval must be at least 1 second.");
            return 1;
        }

        var config = NetvanConfig.Load().WithCollectionSettings(dbPath, intervalSeconds);

        var host = Host.CreateApplicationBuilder(args);
        host.Services.AddWindowsService(options => options.ServiceName = WindowsServiceManager.ServiceName);
        host.Services.AddSingleton(config);
        host.Services.AddHostedService<CollectorHostedService>();

        if (OperatingSystem.IsWindows())
            host.Logging.AddEventLog(settings => settings.SourceName = WindowsServiceManager.ServiceName);

        await host.Build().RunAsync();
        return 0;
    }

    private static string DefaultDbPath => SettingsManager.GetDefaultDatabasePath();
}
#endif
