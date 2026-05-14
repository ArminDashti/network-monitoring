using System.CommandLine;
using System.Globalization;
using NetworkMonitor.Services;
using NetworkMonitor.Storage;

namespace NetworkMonitor;

internal static class Program
{
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";

    public static async Task<int> Main(string[] args)
    {
        var dbOption = new Option<string>(
            aliases: new[] { "--db", "-d" },
            getDefaultValue: () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkMonitor", "traffic.db"))
        { Description = "SQLite database path" };

        var intervalOption = new Option<int>(
            aliases: new[] { "--interval", "-i" },
            getDefaultValue: () => 5)
        { Description = "Seconds between TCP samples" };

        var topOption = new Option<int>(
            aliases: new[] { "--top", "-n" },
            getDefaultValue: () => 25)
        { Description = "Maximum rows to print" };

        var fromOption = new Option<string?>(
            aliases: new[] { "--from-datetime" },
            description: $"Start of range (local). Format {DateTimeFormat}. Default: today 00:00:00.")
        { Arity = ArgumentArity.ZeroOrOne };

        var toOption = new Option<string?>(
            aliases: new[] { "--to-datetime" },
            description: $"End of range (local), inclusive. Format {DateTimeFormat}. Default: now.")
        { Arity = ArgumentArity.ZeroOrOne };

        var appOption = new Option<string>(
            aliases: new[] { "--app" },
            getDefaultValue: () => "all",
            description: "Process name filter (exact match), or 'all'.");

#if WINDOWS
        // Live TCP sampling uses Windows IP Helper APIs; only included in the Windows-targeted build.
        var collect = new Command("collect", "Run the monitor loop (TCP per-connection counters; run elevated for full coverage)")
        {
            intervalOption,
            dbOption,
        };

        collect.SetHandler(RunCollectAsync, intervalOption, dbOption);
#endif

        var report = new Command("report", "Print lifetime aggregated usage from the database (all time, not time-ranged)")
        {
            dbOption,
            topOption,
        };

        var mode = new Argument<string>("mode")
        {
            Description = "ip | nic | host",
        };
        var filter = new Argument<string?>("filter") { Arity = ArgumentArity.ZeroOrOne };
        report.AddArgument(mode);
        report.AddArgument(filter);
        report.SetHandler(RunReport, mode, filter, dbOption, topOption);

        var usage = new Command("usage", "Usage in a time range from collected samples (run collect to record data)")
        {
            fromOption,
            toOption,
            dbOption,
        };
        usage.SetHandler(RunUsageTotals, fromOption, toOption, dbOption);

        var usageDownload = new Command("download", "Bytes received (download) in the time range")
        {
            fromOption,
            toOption,
            dbOption,
        };
        usageDownload.SetHandler(RunUsageDownload, fromOption, toOption, dbOption);

        var usageUpload = new Command("upload", "Bytes sent (upload) in the time range")
        {
            fromOption,
            toOption,
            dbOption,
        };
        usageUpload.SetHandler(RunUsageUpload, fromOption, toOption, dbOption);

        var usageApp = new Command("app", "Break down usage by application (process name)")
        {
            fromOption,
            toOption,
            appOption,
            dbOption,
        };
        usageApp.SetHandler(RunUsageByApp, fromOption, toOption, appOption, dbOption);

        var usageIp = new Command("ip", "Break down usage by remote IP")
        {
            fromOption,
            toOption,
            appOption,
            dbOption,
        };
        usageIp.SetHandler(RunUsageByIp, fromOption, toOption, appOption, dbOption);

        usage.AddCommand(usageDownload);
        usage.AddCommand(usageUpload);
        usage.AddCommand(usageApp);
        usage.AddCommand(usageIp);

#if WINDOWS
        const string rootDescription = "Windows TCP usage monitor (by IP, NIC, and host) backed by SQLite";
#else
        const string rootDescription = "Query NetworkMonitor SQLite data (report/usage). Run `collect` on Windows to record samples.";
#endif

        var root = new RootCommand(rootDescription)
        {
#if WINDOWS
            collect,
#endif
            report,
            usage,
        };

        return await root.InvokeAsync(args);
    }

    private static void RunUsageTotals(string? fromRaw, string? toRaw, string dbPath)
    {
        var (fromUtc, toUtc) = ResolveRangeUtc(fromRaw, toRaw);
        using var store = new TrafficStore(dbPath);
        var row = store.UsageTotalsInRangeUtc(fromUtc, toUtc, appNameOrAll: null);
        Console.WriteLine($"Range (UTC): {fromUtc} .. {toUtc} (inclusive)");
        Console.WriteLine($"Upload (sent):   {FormatBytes(row.BytesSent)}");
        Console.WriteLine($"Download (recv): {FormatBytes(row.BytesReceived)}");
        Console.WriteLine($"Total:           {FormatBytes(row.BytesSent + row.BytesReceived)}");
    }

    private static void RunUsageDownload(string? fromRaw, string? toRaw, string dbPath)
    {
        var (fromUtc, toUtc) = ResolveRangeUtc(fromRaw, toRaw);
        using var store = new TrafficStore(dbPath);
        var row = store.UsageTotalsInRangeUtc(fromUtc, toUtc, appNameOrAll: null);
        Console.WriteLine($"Range (UTC): {fromUtc} .. {toUtc} (inclusive)");
        Console.WriteLine($"Download (recv): {FormatBytes(row.BytesReceived)}");
    }

    private static void RunUsageUpload(string? fromRaw, string? toRaw, string dbPath)
    {
        var (fromUtc, toUtc) = ResolveRangeUtc(fromRaw, toRaw);
        using var store = new TrafficStore(dbPath);
        var row = store.UsageTotalsInRangeUtc(fromUtc, toUtc, appNameOrAll: null);
        Console.WriteLine($"Range (UTC): {fromUtc} .. {toUtc} (inclusive)");
        Console.WriteLine($"Upload (sent): {FormatBytes(row.BytesSent)}");
    }

    private static void RunUsageByApp(string? fromRaw, string? toRaw, string app, string dbPath)
    {
        var (fromUtc, toUtc) = ResolveRangeUtc(fromRaw, toRaw);
        using var store = new TrafficStore(dbPath);
        var rows = store.UsageByAppInRangeUtc(fromUtc, toUtc, app);
        Console.WriteLine($"Range (UTC): {fromUtc} .. {toUtc} (inclusive); app filter: {app}");
        Console.WriteLine($"{"Application",-32} {"Sent",12} {"Recv",12} {"Total",12}");
        foreach (var r in rows)
            Console.WriteLine($"{r.AppName,-32} {FormatBytes(r.BytesSent),12} {FormatBytes(r.BytesReceived),12} {FormatBytes(r.BytesSent + r.BytesReceived),12}");
    }

    private static void RunUsageByIp(string? fromRaw, string? toRaw, string app, string dbPath)
    {
        var (fromUtc, toUtc) = ResolveRangeUtc(fromRaw, toRaw);
        using var store = new TrafficStore(dbPath);
        var rows = store.UsageByIpInRangeUtc(fromUtc, toUtc, app);
        Console.WriteLine($"Range (UTC): {fromUtc} .. {toUtc} (inclusive); app filter: {app}");
        Console.WriteLine($"{"Remote IP",-40} {"Sent",12} {"Recv",12} {"Total",12}");
        foreach (var r in rows)
            Console.WriteLine($"{r.RemoteIp,-40} {FormatBytes(r.BytesSent),12} {FormatBytes(r.BytesReceived),12} {FormatBytes(r.BytesSent + r.BytesReceived),12}");
    }

    private static (string FromUtc, string ToUtc) ResolveRangeUtc(string? fromRaw, string? toRaw)
    {
        var fromLocal = ParseBoundaryOrDefault(fromRaw, DateTime.Today);
        var toLocal = ParseBoundaryOrDefault(toRaw, DateTime.Now);
        if (toLocal < fromLocal)
            (fromLocal, toLocal) = (toLocal, fromLocal);

        var fromUtc = fromLocal.ToUniversalTime().ToString("O");
        var toUtc = toLocal.ToUniversalTime().ToString("O");
        return (fromUtc, toUtc);
    }

    private static DateTime ParseBoundaryOrDefault(string? raw, DateTime defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        if (!DateTime.TryParseExact(raw.Trim(), DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            Console.Error.WriteLine($"Invalid datetime '{raw}'. Expected {DateTimeFormat} (local time).");
            Environment.Exit(1);
            return default;
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Local);
    }

#if WINDOWS
    private static async Task RunCollectAsync(int intervalSeconds, string dbPath)
    {
        using var store = new TrafficStore(dbPath);
        var nics = new NicResolver();
        var hosts = new HostNameCache();
        var collector = new TrafficCollector(nics, hosts);

        Console.WriteLine($"Database: {Path.GetFullPath(dbPath)}");
        Console.WriteLine($"Sampling every {intervalSeconds}s. Press Ctrl+C to stop.");
        Console.WriteLine("Note: totals are TCP application data bytes per remote endpoint (not full Ethernet frames). UDP and QUIC-only traffic are not included.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var deltas = collector.CollectDeltas();
                store.ApplyDeltas(deltas);
                var ts = DateTime.Now.ToString("T");
                var sum = deltas.Sum(d => d.DeltaSent + d.DeltaReceived);
                Console.WriteLine($"[{ts}] connections with new data: {deltas.Count}, interval volume: {FormatBytes(sum)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Sample failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
#endif

    private static void RunReport(string mode, string? filter, string dbPath, int top)
    {
        using var store = new TrafficStore(dbPath);
        var m = mode.Trim().ToLowerInvariant();
        switch (m)
        {
            case "ip":
                PrintIp(store.ReportByIp(filter), top);
                break;
            case "nic":
                PrintNic(store.ReportByNic(filter), top);
                break;
            case "host":
            case "website":
            case "site":
                PrintHost(store.ReportByHost(filter), top);
                break;
            default:
                Console.Error.WriteLine("mode must be one of: ip, nic, host");
                Environment.ExitCode = 1;
                break;
        }
    }

    private static void PrintIp(IReadOnlyList<IpReportRow> rows, int top)
    {
        Console.WriteLine($"{"IP",-40} {"Sent",12} {"Recv",12} {"Total",12}");
        foreach (var r in rows.Take(top))
            Console.WriteLine($"{r.RemoteIp,-40} {FormatBytes(r.BytesSent),12} {FormatBytes(r.BytesReceived),12} {FormatBytes(r.BytesSent + r.BytesReceived),12}");
    }

    private static void PrintNic(IReadOnlyList<NicReportRow> rows, int top)
    {
        Console.WriteLine($"{"NIC",-32} {"Sent",12} {"Recv",12} {"Total",12}");
        foreach (var r in rows.Take(top))
            Console.WriteLine($"{r.NicName,-32} {FormatBytes(r.BytesSent),12} {FormatBytes(r.BytesReceived),12} {FormatBytes(r.BytesSent + r.BytesReceived),12}");
    }

    private static void PrintHost(IReadOnlyList<HostReportRow> rows, int top)
    {
        Console.WriteLine($"{"Host / site",-48} {"Sent",12} {"Recv",12} {"Total",12}");
        foreach (var r in rows.Take(top))
            Console.WriteLine($"{r.HostName,-48} {FormatBytes(r.BytesSent),12} {FormatBytes(r.BytesReceived),12} {FormatBytes(r.BytesSent + r.BytesReceived),12}");
    }

    private static string FormatBytes(long value)
    {
        if (value < 0)
            value = 0;

        const double k = 1024d;
        if (value < k)
            return $"{value} B";
        if (value < k * k)
            return $"{value / k:0.##} KB";
        if (value < k * k * k)
            return $"{value / (k * k):0.##} MB";
        if (value < k * k * k * k)
            return $"{value / (k * k * k):0.##} GB";
        return $"{value / (k * k * k * k):0.##} TB";
    }
}
