#if WINDOWS
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Netvan.Storage;

namespace Netvan.Services;

internal static class ServiceHostRunner
{
    public static async Task<int> RunAsync()
    {
        SettingsManager.Initialize();

        var config = NetvanConfig.Load();
        var host = Host.CreateApplicationBuilder();
        host.Services.AddWindowsService(options => options.ServiceName = WindowsServiceManager.ServiceName);
        host.Services.AddSingleton(config);
        host.Services.AddHostedService<CollectorHostedService>();

        if (OperatingSystem.IsWindows())
            host.Logging.AddEventLog(settings => settings.SourceName = WindowsServiceManager.ServiceName);

        await host.Build().RunAsync();
        return 0;
    }
}
#endif
