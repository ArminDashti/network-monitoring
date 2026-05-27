#if WINDOWS
namespace NetworkMonitor.Services;

internal sealed class CollectorOptions
{
    public string DatabasePath { get; set; } = string.Empty;

    public int IntervalSeconds { get; set; } = 5;
}
#endif
