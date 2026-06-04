using System.CommandLine;
using System.Reflection;
using NetworkMonitor.Cli;
using NetworkMonitor.Services;
using NetworkMonitor.Storage;

namespace NetworkMonitor;

internal static class Program
{
  private static string DefaultDbPath => NetmConfig.Load().ResolvedDatabasePath;

  public static async Task<int> Main(string[] args)
  {
#if WINDOWS
    if (args.Length > 0 && args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
      return await ServiceHostRunner.RunAsync(args[1..]);
#endif

    if (IsCollectInvocation(args))
      return await RunCollectAsync();

    Directory.CreateDirectory(NetmPaths.Home);
    SettingsManager.Initialize();

    var dbOption = new Option<string>(
      aliases: new[] { "--db", "-d" },
      getDefaultValue: () => DefaultDbPath)
    { Description = "SQLite database path" };

    var filterOption = new Option<string?>(
      aliases: new[] { "--filter" },
      description: "Optional substring filter for app names.")
    { Arity = ArgumentArity.ZeroOrOne };

    var usage = new Command("usage", "Upload, download, and total bytes in a time range");
    var usageOpts = CreateUsageOptions();
    RegisterUsageHandler(usage, usageOpts);

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

    var start = new Command("start", "Start background traffic collector");
    start.SetHandler(() => DaemonManager.Start());

    var stop = new Command("stop", "Stop background traffic collector");
    stop.SetHandler(() => DaemonManager.Stop());

    var status = new Command("status", "Show collector status");
    status.SetHandler(() => DaemonManager.Status());

    var reset = new Command("reset", "Remove the traffic database and restart the collector (clears in-memory counters)")
    {
      dbOption,
    };
    reset.SetHandler(dbPath => RunReset(dbPath), dbOption);

    var root = new RootCommand("Windows TCP usage monitor (netm)")
    {
      start,
      stop,
      status,
      reset,
      info,
      usage,
      apps,
      rt,
    };

#if WINDOWS
    var intervalOption = new Option<int>(
      aliases: new[] { "--interval", "-i" },
      getDefaultValue: () => 5,
      description: "Sampling interval in seconds.")
    {
      Arity = ArgumentArity.ZeroOrOne,
    };

    var serviceInstall = new Command("install", "Install the NetM Windows service (Administrator required)")
    {
      dbOption,
      intervalOption,
    };
    serviceInstall.SetHandler(RunServiceInstall, dbOption, intervalOption);

    var serviceUninstall = new Command("uninstall", "Remove the NetM Windows service (Administrator required)");
    serviceUninstall.SetHandler(RunServiceUninstall);

    var serviceStart = new Command("start", "Start the NetM Windows service");
    serviceStart.SetHandler(RunServiceStart);

    var serviceStop = new Command("stop", "Stop the NetM Windows service");
    serviceStop.SetHandler(RunServiceStop);

    var serviceStatus = new Command("status", "Show NetM Windows service status");
    serviceStatus.SetHandler(RunServiceStatus);

    var service = new Command("service", "Manage the NetM Windows service (daemon mode)")
    {
      serviceInstall,
      serviceUninstall,
      serviceStart,
      serviceStop,
      serviceStatus,
    };
    root.AddCommand(service);
#endif

    return await root.InvokeAsync(args);
  }

  private static bool IsCollectInvocation(string[] args) =>
    args.Length > 0 && args[0].Equals("collect", StringComparison.OrdinalIgnoreCase);

  private static async Task<int> RunCollectAsync()
  {
#if WINDOWS
    SettingsManager.Initialize();
    var config = NetmConfig.Load();
    NetmLog.Configure(config);

    var pid = Environment.ProcessId;
    if (DaemonManager.TryReadState(out var existing) && existing.ProcessId != pid && existing.ProcessId > 0)
    {
      try
      {
        var other = System.Diagnostics.Process.GetProcessById(existing.ProcessId);
        if (!other.HasExited && existing.ProcessId != pid)
        {
          NetmLog.Error($"Another collector is already running (PID {existing.ProcessId}).");
          return 1;
        }
      }
      catch (ArgumentException)
      {
        // Stale PID file; overwrite below.
      }
    }

    DaemonManager.WriteState(new DaemonState(pid, DateTime.UtcNow, config.ResolvedDatabasePath));

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
      e.Cancel = true;
      cts.Cancel();
    };

    AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

    try
    {
      using var loop = new CollectionLoop(config);
      await loop.RunAsync(cts.Token);
      return 0;
    }
    catch (OperationCanceledException)
    {
      return 0;
    }
    catch (Exception ex)
    {
      NetmLog.Error($"Collector exited: {ex.Message}");
      return 1;
    }
    finally
    {
      DaemonManager.ClearState();
    }
#else
    await Task.CompletedTask;
    ConsoleUi.WriteError("Traffic collection requires Windows (net9.0-windows build).");
    return 1;
#endif
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

  private static void RegisterUsageHandler(Command command, UsageOptions options)
  {
    command.AddOption(options.Target);
    command.AddOption(options.From);
    command.AddOption(options.To);
    command.AddOption(options.IncludePrivate);
    command.AddOption(options.Db);
    command.SetHandler(
      (target, from, to, includePrivate, db) => RunUsage(target, from, to, includePrivate, db),
      options.Target, options.From, options.To, options.IncludePrivate, options.Db);
  }

#if WINDOWS
  private static void RunServiceInstall(string dbPath, int intervalSeconds)
  {
    var exePath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(exePath))
    {
      ConsoleUi.WriteError("Could not resolve the current executable path.");
      Environment.Exit(1);
      return;
    }

    dbPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dbPath));
    var code = WindowsServiceManager.Install(exePath, intervalSeconds, dbPath, out var message);
    ConsoleUi.WriteServiceResult(code, message);
    Environment.Exit(code);
  }

  private static void RunServiceUninstall()
  {
    var code = WindowsServiceManager.Uninstall(out var message);
    ConsoleUi.WriteServiceResult(code, message);
    Environment.Exit(code);
  }

  private static void RunServiceStart()
  {
    var code = WindowsServiceManager.Start(out var message);
    ConsoleUi.WriteServiceResult(code, message);
    Environment.Exit(code);
  }

  private static void RunServiceStop()
  {
    var code = WindowsServiceManager.Stop(out var message);
    ConsoleUi.WriteServiceResult(code, message);
    Environment.Exit(code);
  }

  private static void RunServiceStatus()
  {
    if (!WindowsServiceManager.IsInstalled())
    {
      ConsoleUi.RenderServiceStatus(
        installed: false,
        WindowsServiceManager.ServiceName,
        null,
        null);
      return;
    }

    var status = WindowsServiceManager.GetStatus();
    ConsoleUi.RenderServiceStatus(
      installed: true,
      WindowsServiceManager.ServiceName,
      WindowsServiceManager.DisplayName,
      status.ToString());
  }
#endif

  private static int RunReset(string dbPath)
  {
    dbPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dbPath));

#if WINDOWS
    var restartService = WindowsServiceManager.IsInstalled() && WindowsServiceManager.IsRunning();
    var restartDaemon = !restartService && DaemonManager.IsCollectorRunning();

    if (restartService)
    {
      var stopCode = WindowsServiceManager.Stop(out var stopMessage);
      if (stopCode != 0)
      {
        ConsoleUi.WriteError(stopMessage);
        return stopCode;
      }

      ConsoleUi.WriteNote("NetM service stopped for reset.");
    }
    else if (restartDaemon)
    {
      var stopCode = DaemonManager.Stop();
      if (stopCode != 0)
        return stopCode;
    }
#else
    const bool restartService = false;
    const bool restartDaemon = false;
#endif

    var removed = TrafficDatabase.DeleteFiles(dbPath);
    if (removed)
      ConsoleUi.WriteSuccess($"Removed database at {dbPath}.");
    else
      ConsoleUi.WriteNote($"No database file at {dbPath} (already empty).");

#if WINDOWS
    if (restartService)
    {
      var startCode = WindowsServiceManager.Start(out var startMessage);
      if (startCode != 0)
      {
        ConsoleUi.WriteError(startMessage);
        return startCode;
      }

      ConsoleUi.WriteSuccess("NetM service restarted with a fresh database and in-memory counters.");
      return 0;
    }

    if (restartDaemon)
    {
      var startCode = DaemonManager.Start();
      if (startCode != 0)
        return startCode;

      ConsoleUi.WriteSuccess("Collector restarted with a fresh database and in-memory counters.");
      return 0;
    }
#endif

    ConsoleUi.WriteNote("Collector was not running; start it with netm start or netm service start when ready.");
    return 0;
  }

  private static void RunInfo(string dbPath)
  {
    using var store = new TrafficStore(dbPath);
    var info = store.GetDatabaseInfo(dbPath);
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

#if WINDOWS
    if (Environment.GetEnvironmentVariable("NETM_DEBUG") == "1")
      Native.IpHelperApi.PrintStatsDiagnostics();
#endif

    ConsoleUi.RenderInfo(version, info, CompactDateTime.Format);
  }

  private static void RunAppsList(string? filter, string dbPath)
  {
    using var store = new TrafficStore(dbPath);
    var apps = store.ListAppNames(filter);
    ConsoleUi.RenderAppsList(apps, filter);
  }

  private static void RunUsage(
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
    var targetLabel = DescribeTarget(target);
    ConsoleUi.RenderUsageContext(fromUtc, toUtc, targetLabel, includePrivate);

    switch (target.Kind)
    {
      case UsageTargetKind.Apps:
        ConsoleUi.RenderAppUsageTable(store.UsageByAppInRangeUtc(fromUtc, toUtc, includePrivate));
        break;
      case UsageTargetKind.IpTop100:
        ConsoleUi.RenderIpUsageTable(store.UsageByIpInRangeUtc(fromUtc, toUtc, includePrivate, QueryFilters.TopUsageLimit));
        break;
      case UsageTargetKind.HostTop100:
        ConsoleUi.RenderHostUsageTable(store.UsageByHostInRangeUtc(fromUtc, toUtc, includePrivate, QueryFilters.TopUsageLimit));
        break;
      default:
        var totals = store.UsageTotalsInRangeUtc(fromUtc, toUtc, target, includePrivate);
        ConsoleUi.RenderUsageTotals(totals.BytesSent, totals.BytesReceived);
        break;
    }
  }

  private static string DescribeTarget(UsageTarget target) =>
    target.Kind switch
    {
      UsageTargetKind.All => "all apps",
      UsageTargetKind.Apps => "apps",
      UsageTargetKind.IpTop100 => "ip (top 100)",
      UsageTargetKind.HostTop100 => "host (top 100)",
      _ => target.Value ?? "",
    };

  private static void RunRealtime(string dbPath)
  {
    var nowLocal = DateTime.Now;
    var dailyStartLocal = nowLocal.Date;
    var weeklyStartLocal = StartOfCurrentWeekSaturday(nowLocal);
    var monthlyStartLocal = new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Local);

    var nowUtc = TrafficStore.FormatBucketUtc(nowLocal.ToUniversalTime());
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

    var rows = new List<ConsoleUi.RealtimeUsageRow>();
    foreach (var app in apps)
    {
      currentRows.TryGetValue(app, out var current);
      dailyRows.TryGetValue(app, out var daily);
      weeklyRows.TryGetValue(app, out var weekly);
      monthlyRows.TryGetValue(app, out var monthly);

      rows.Add(new ConsoleUi.RealtimeUsageRow(
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

    ConsoleUi.RenderRealtime(dailyStartLocal, weeklyStartLocal, monthlyStartLocal, nowLocal, rows);
  }

  private static bool ParseIncludePrivate(string raw)
  {
    if (raw.Equals("yes", StringComparison.OrdinalIgnoreCase) || raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1")
      return true;
    if (raw.Equals("no", StringComparison.OrdinalIgnoreCase) || raw.Equals("false", StringComparison.OrdinalIgnoreCase) || raw == "0")
      return false;

    ConsoleUi.WriteError($"Invalid --include-private '{raw}'. Use yes or no.");
    Environment.Exit(1);
    return false;
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

}
