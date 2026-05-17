using System.CommandLine;
using System.Reflection;
using NetworkMonitor.Cli;
using NetworkMonitor.Storage;

namespace NetworkMonitor;

internal enum UsageMetric
{
  Total,
  Download,
  Upload,
}

internal readonly record struct RtUsageRow(
  string AppName,
  long CurrentDownBytes,
  long CurrentUpBytes,
  long DailyDownBytes,
  long DailyUpBytes,
  long WeeklyDownBytes,
  long WeeklyUpBytes,
  long MonthlyDownBytes,
  long MonthlyUpBytes);

internal static class Program
{
  private static readonly string DefaultDbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "NetworkMonitor",
    "traffic.db");

  public static async Task<int> Main(string[] args)
  {
    var dbOption = new Option<string>(
      aliases: new[] { "--db", "-d" },
      getDefaultValue: () => DefaultDbPath)
    { Description = "SQLite database path" };

    var filterOption = new Option<string?>(
      aliases: new[] { "--filter" },
      description: "Optional substring filter for app names.")
    { Arity = ArgumentArity.ZeroOrOne };

    var usage = new Command("usage", "Usage in a time range from collected samples");
    var usageOpts = CreateUsageOptions();
    RegisterUsageHandler(usage, UsageMetric.Total, usageOpts);

    var usageDownload = new Command("download", "Download (received) bytes in the time range");
    var downloadOpts = CreateUsageOptions();
    RegisterUsageHandler(usageDownload, UsageMetric.Download, downloadOpts);

    var usageUpload = new Command("upload", "Upload (sent) bytes in the time range");
    var uploadOpts = CreateUsageOptions();
    RegisterUsageHandler(usageUpload, UsageMetric.Upload, uploadOpts);

    usage.AddCommand(usageDownload);
    usage.AddCommand(usageUpload);

    var info = new Command("info", "Database path, coverage, and version")
    {
      dbOption,
    };
    info.SetHandler(RunInfo, dbOption);

    var appsList = new Command("list", "List application names seen in the database")
    {
      filterOption,
      dbOption,
    };
    appsList.SetHandler(RunAppsList, filterOption, dbOption);

    var apps = new Command("apps", "Application names from collected traffic")
    {
      appsList,
    };

    var rt = new Command("rt", "Real-time usage table by app with daily/weekly/monthly totals")
    {
      dbOption,
    };
    rt.SetHandler(RunRealtime, dbOption);

    var root = new RootCommand("Windows TCP usage monitor (netm)")
    {
      info,
      usage,
      apps,
      rt,
    };

    return await root.InvokeAsync(args);
  }

  private sealed record UsageOptions(
    Option<string?> Target,
    Option<string?> From,
    Option<string?> To,
    Option<string> IncludePrivate,
    Option<string> Db);

  private static UsageOptions CreateUsageOptions()
  {
    return new UsageOptions(
      new Option<string?>(
        aliases: new[] { "--target" },
        description: "apps | ip | host | <app-name> | <x.x.x.x> | <hostname>. Omit for all apps combined.")
      { Arity = ArgumentArity.ZeroOrOne },
      new Option<string?>(
        aliases: new[] { "--from-datetime" },
        description: $"Start of range (local). Format {CompactDateTime.Format}. Date-only yyMMdd uses T0000.")
      { Arity = ArgumentArity.ZeroOrOne },
      new Option<string?>(
        aliases: new[] { "--to-datetime" },
        description: $"End of range (local), inclusive. Format {CompactDateTime.Format}. Default: now.")
      { Arity = ArgumentArity.ZeroOrOne },
      new Option<string>(
        aliases: new[] { "--include-private" },
        getDefaultValue: () => "no",
        description: "Include private/local IP traffic: yes | no (default: no).")
      { Arity = ArgumentArity.ZeroOrOne },
      new Option<string>(
        aliases: new[] { "--db", "-d" },
        getDefaultValue: () => DefaultDbPath)
      { Description = "SQLite database path" });
  }

  private static void RegisterUsageHandler(Command command, UsageMetric metric, UsageOptions options)
  {
    command.AddOption(options.Target);
    command.AddOption(options.From);
    command.AddOption(options.To);
    command.AddOption(options.IncludePrivate);
    command.AddOption(options.Db);
    command.SetHandler(
      (target, from, to, includePrivate, db) => RunUsage(metric, target, from, to, includePrivate, db),
      options.Target, options.From, options.To, options.IncludePrivate, options.Db);
  }

  private static void RunInfo(string dbPath)
  {
    using var store = new TrafficStore(dbPath);
    var info = store.GetDatabaseInfo(dbPath);
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    Console.WriteLine($"Version:    {version}");
    Console.WriteLine($"Database:   {Path.GetFullPath(info.DatabasePath)}");
    Console.WriteLine($"File size:  {FormatBytes(info.FileBytes)}");
    Console.WriteLine($"Rows:       {info.RowCount:N0}");
    Console.WriteLine($"Apps:       {info.DistinctAppCount:N0}");
    Console.WriteLine($"First UTC:  {info.FirstMinuteUtc ?? "(none)"}");
    Console.WriteLine($"Last UTC:   {info.LastMinuteUtc ?? "(none)"}");
    Console.WriteLine($"Datetime:   local {CompactDateTime.Format} (date-only yyMMdd → T0000)");
  }

  private static void RunAppsList(string? filter, string dbPath)
  {
    using var store = new TrafficStore(dbPath);
    var apps = store.ListAppNames(filter);
    if (filter is not null)
      Console.WriteLine($"Filter: {filter}");
    foreach (var app in apps)
      Console.WriteLine(app);
    Console.WriteLine($"Count: {apps.Count}");
  }

  private static void RunUsage(
    UsageMetric metric,
    string? targetRaw,
    string? fromRaw,
    string? toRaw,
    string includePrivateRaw,
    string dbPath)
  {
    var target = UsageTarget.Parse(targetRaw);
    var includePrivate = ParseIncludePrivate(includePrivateRaw);
    var (fromUtc, toUtc) = CompactDateTime.ResolveRangeUtc(fromRaw, toRaw);

    using var store = new TrafficStore(dbPath);
    PrintRangeHeader(fromUtc, toUtc, target, includePrivate);

    switch (target.Kind)
    {
      case UsageTargetKind.Apps:
        PrintAppRows(store.UsageByAppInRangeUtc(fromUtc, toUtc, includePrivate), metric);
        break;
      case UsageTargetKind.IpTop100:
        PrintIpRows(store.UsageByIpInRangeUtc(fromUtc, toUtc, includePrivate, QueryFilters.TopUsageLimit), metric);
        break;
      case UsageTargetKind.HostTop100:
        PrintHostRows(store.UsageByHostInRangeUtc(fromUtc, toUtc, includePrivate, QueryFilters.TopUsageLimit), metric);
        break;
      default:
        var totals = store.UsageTotalsInRangeUtc(fromUtc, toUtc, target, includePrivate);
        PrintTotals(totals, metric);
        break;
    }
  }

  private static void RunRealtime(string dbPath)
  {
    var nowLocal = DateTime.Now;
    var dailyStartLocal = nowLocal.Date;
    var weeklyStartLocal = StartOfCurrentWeekSaturday(nowLocal);
    var monthlyStartLocal = new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Local);

    var nowUtc = nowLocal.ToUniversalTime().ToString("O");
    var dailyUtc = dailyStartLocal.ToUniversalTime().ToString("O");
    var weeklyUtc = weeklyStartLocal.ToUniversalTime().ToString("O");
    var monthlyUtc = monthlyStartLocal.ToUniversalTime().ToString("O");

    using var store = new TrafficStore(dbPath);
    var currentRows = store.UsageByAppInRangeUtc(nowUtc, nowUtc, includePrivate: true)
      .ToDictionary(x => NormalizeAppName(x.AppName), x => x, StringComparer.OrdinalIgnoreCase);
    var dailyRows = store.UsageByAppInRangeUtc(dailyUtc, nowUtc, includePrivate: true)
      .ToDictionary(x => NormalizeAppName(x.AppName), x => x, StringComparer.OrdinalIgnoreCase);
    var weeklyRows = store.UsageByAppInRangeUtc(weeklyUtc, nowUtc, includePrivate: true)
      .ToDictionary(x => NormalizeAppName(x.AppName), x => x, StringComparer.OrdinalIgnoreCase);
    var monthlyRows = store.UsageByAppInRangeUtc(monthlyUtc, nowUtc, includePrivate: true)
      .ToDictionary(x => NormalizeAppName(x.AppName), x => x, StringComparer.OrdinalIgnoreCase);

    var apps = currentRows.Keys
      .Concat(dailyRows.Keys)
      .Concat(weeklyRows.Keys)
      .Concat(monthlyRows.Keys)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var rows = new List<RtUsageRow>();
    foreach (var app in apps)
    {
      currentRows.TryGetValue(app, out var current);
      dailyRows.TryGetValue(app, out var daily);
      weeklyRows.TryGetValue(app, out var weekly);
      monthlyRows.TryGetValue(app, out var monthly);

      rows.Add(new RtUsageRow(
        app,
        current.BytesReceived,
        current.BytesSent,
        daily.BytesReceived,
        daily.BytesSent,
        weekly.BytesReceived,
        weekly.BytesSent,
        monthly.BytesReceived,
        monthly.BytesSent));
    }

    Console.WriteLine($"Daily:   {dailyStartLocal:yyyy-MM-dd HH:mm:ss} local -> {nowLocal:yyyy-MM-dd HH:mm:ss} local");
    Console.WriteLine($"Weekly:  {weeklyStartLocal:yyyy-MM-dd HH:mm:ss} local -> {nowLocal:yyyy-MM-dd HH:mm:ss} local");
    Console.WriteLine($"Monthly: {monthlyStartLocal:yyyy-MM-dd HH:mm:ss} local -> {nowLocal:yyyy-MM-dd HH:mm:ss} local");
    PrintRealtimeRows(rows);
  }

  private static bool ParseIncludePrivate(string raw)
  {
    if (raw.Equals("yes", StringComparison.OrdinalIgnoreCase) || raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1")
      return true;
    if (raw.Equals("no", StringComparison.OrdinalIgnoreCase) || raw.Equals("false", StringComparison.OrdinalIgnoreCase) || raw == "0")
      return false;

    Console.Error.WriteLine($"Invalid --include-private '{raw}'. Use yes or no.");
    Environment.Exit(1);
    return false;
  }

  private static void PrintRangeHeader(string fromUtc, string toUtc, UsageTarget target, bool includePrivate)
  {
    var targetLabel = target.Kind switch
    {
      UsageTargetKind.All => "all apps",
      UsageTargetKind.Apps => "apps",
      UsageTargetKind.IpTop100 => "ip (top 100)",
      UsageTargetKind.HostTop100 => "host (top 100)",
      _ => target.Value ?? "",
    };
    Console.WriteLine($"Range (UTC): {fromUtc} .. {toUtc} (inclusive)");
    Console.WriteLine($"Target:      {targetLabel}");
    Console.WriteLine($"Private IPs: {(includePrivate ? "included" : "excluded")}");
  }

  private static void PrintTotals(UsageTotalsRow row, UsageMetric metric)
  {
    switch (metric)
    {
      case UsageMetric.Download:
        Console.WriteLine($"Download (recv): {FormatBytes(row.BytesReceived)}");
        break;
      case UsageMetric.Upload:
        Console.WriteLine($"Upload (sent):   {FormatBytes(row.BytesSent)}");
        break;
      default:
        Console.WriteLine($"Upload (sent):   {FormatBytes(row.BytesSent)}");
        Console.WriteLine($"Download (recv): {FormatBytes(row.BytesReceived)}");
        Console.WriteLine($"Total:           {FormatBytes(row.BytesSent + row.BytesReceived)}");
        break;
    }
  }

  private static void PrintAppRows(IReadOnlyList<AppUsageRow> rows, UsageMetric metric)
  {
    Console.WriteLine(metric switch
    {
      UsageMetric.Download => $"{"Application",-32} {"Download",12}",
      UsageMetric.Upload => $"{"Application",-32} {"Upload",12}",
      _ => $"{"Application",-32} {"Upload",12} {"Download",12} {"Total",12}",
    });

    foreach (var r in rows)
    {
      Console.WriteLine(metric switch
      {
        UsageMetric.Download => $"{r.AppName,-32} {FormatBytes(r.BytesReceived),12}",
        UsageMetric.Upload => $"{r.AppName,-32} {FormatBytes(r.BytesSent),12}",
        _ => $"{r.AppName,-32} {FormatBytes(r.BytesSent),12} {FormatBytes(r.BytesReceived),12} {FormatBytes(r.BytesSent + r.BytesReceived),12}",
      });
    }
  }

  private static void PrintIpRows(IReadOnlyList<IpUsageRow> rows, UsageMetric metric)
  {
    Console.WriteLine(metric switch
    {
      UsageMetric.Download => $"{"Remote IP",-40} {"Download",12}",
      UsageMetric.Upload => $"{"Remote IP",-40} {"Upload",12}",
      _ => $"{"Remote IP",-40} {"Upload",12} {"Download",12} {"Total",12}",
    });

    foreach (var r in rows)
    {
      Console.WriteLine(metric switch
      {
        UsageMetric.Download => $"{r.RemoteIp,-40} {FormatBytes(r.BytesReceived),12}",
        UsageMetric.Upload => $"{r.RemoteIp,-40} {FormatBytes(r.BytesSent),12}",
        _ => $"{r.RemoteIp,-40} {FormatBytes(r.BytesSent),12} {FormatBytes(r.BytesReceived),12} {FormatBytes(r.BytesSent + r.BytesReceived),12}",
      });
    }
  }

  private static void PrintHostRows(IReadOnlyList<HostUsageRow> rows, UsageMetric metric)
  {
    Console.WriteLine(metric switch
    {
      UsageMetric.Download => $"{"Host",-48} {"Download",12}",
      UsageMetric.Upload => $"{"Host",-48} {"Upload",12}",
      _ => $"{"Host",-48} {"Upload",12} {"Download",12} {"Total",12}",
    });

    foreach (var r in rows)
    {
      Console.WriteLine(metric switch
      {
        UsageMetric.Download => $"{r.HostName,-48} {FormatBytes(r.BytesReceived),12}",
        UsageMetric.Upload => $"{r.HostName,-48} {FormatBytes(r.BytesSent),12}",
        _ => $"{r.HostName,-48} {FormatBytes(r.BytesSent),12} {FormatBytes(r.BytesReceived),12} {FormatBytes(r.BytesSent + r.BytesReceived),12}",
      });
    }
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

  private static DateTime StartOfCurrentWeekSaturday(DateTime localNow)
  {
    var date = localNow.Date;
    while (date.DayOfWeek != DayOfWeek.Saturday)
      date = date.AddDays(-1);
    return DateTime.SpecifyKind(date, DateTimeKind.Local);
  }

  private static string NormalizeAppName(string appName)
  {
    var name = Path.GetFileName(appName);
    var dot = name.LastIndexOf('.');
    return dot > 0 ? name[..dot] : name;
  }

  private static void PrintRealtimeRows(IReadOnlyList<RtUsageRow> rows)
  {
    const string red = "\u001b[31m";
    const string green = "\u001b[32m";
    const string reset = "\u001b[0m";

    Console.WriteLine();
    Console.WriteLine($"{"App",-24} {"Download",10} {"Upload",10} {"Daily",22} {"Weekly",22} {"Monthly",22}");
    foreach (var row in rows)
    {
      var daily = $"{green}{FormatGb(row.DailyDownBytes)}{reset}/{red}{FormatGb(row.DailyUpBytes)}{reset}";
      var weekly = $"{green}{FormatGb(row.WeeklyDownBytes)}{reset}/{red}{FormatGb(row.WeeklyUpBytes)}{reset}";
      var monthly = $"{green}{FormatGb(row.MonthlyDownBytes)}{reset}/{red}{FormatGb(row.MonthlyUpBytes)}{reset}";
      Console.WriteLine($"{row.AppName,-24} {FormatMb(row.CurrentDownBytes),10} {FormatMb(row.CurrentUpBytes),10} {daily,22} {weekly,22} {monthly,22}");
    }
  }

  private static string FormatMb(long bytes) => $"{bytes / (1024d * 1024d):0.##}";

  private static string FormatGb(long bytes) => $"{bytes / (1024d * 1024d * 1024d):0.##}";
}
