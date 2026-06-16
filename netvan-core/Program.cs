using System.CommandLine;
using Netvan.Services;
using Netvan.Storage;
#if WINDOWS
using Netvan.Taskbar;
#endif

namespace Netvan;

internal static class Program
{
  private static string DefaultDbPath => NetvanConfig.Load().ResolvedDatabasePath;

  [STAThread]
  public static async Task<int> Main(string[] args)
  {
#if WINDOWS
    if (args.Length > 0 && args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
      return await ServiceHostRunner.RunAsync(args[1..]);

    if (IsTaskbarRunInvocation(args))
      return TaskbarWidgetManager.RunWidget();

    if (args.Length == 0)
      return GuiLauncher.Launch();
#endif

    Directory.CreateDirectory(NetvanPaths.Home);
    SettingsManager.Initialize();

    var dbOption = new Option<string>(
      aliases: new[] { "--db", "-d" },
      getDefaultValue: () => DefaultDbPath)
    { Description = "SQLite database path" };

    var reset = new Command("reset", "Remove the traffic database and restart the service (clears in-memory counters)");
    reset.SetHandler(() => RunReset());

    var root = new RootCommand("Netvan Windows TCP usage monitor")
    {
      reset,
    };

#if WINDOWS
    var serviceInstall = new Command("install", "Install the Netvan Windows service (Administrator required)")
    {
      dbOption,
    };
    serviceInstall.SetHandler(RunServiceInstall, dbOption);

    var serviceUninstall = new Command("uninstall", "Remove the Netvan Windows service (Administrator required)");
    serviceUninstall.SetHandler(RunServiceUninstall);

    var serviceStart = new Command("start", "Start the Netvan Windows service");
    serviceStart.SetHandler(RunServiceStart);

    var serviceStop = new Command("stop", "Stop the Netvan Windows service");
    serviceStop.SetHandler(RunServiceStop);

    var serviceStatus = new Command("status", "Show Netvan Windows service status");
    serviceStatus.SetHandler(RunServiceStatus);

    var service = new Command("service", "Manage the Netvan Windows service")
    {
      serviceInstall,
      serviceUninstall,
      serviceStart,
      serviceStop,
      serviceStatus,
    };
    root.AddCommand(service);

    var taskbarEnable = new Command("enable", "Show upload/download speeds in the Windows taskbar");
    taskbarEnable.SetHandler(() =>
    {
      var code = TaskbarWidgetManager.Enable();
      Environment.Exit(code);
    });

    var taskbarDisable = new Command("disable", "Remove the taskbar speed widget");
    taskbarDisable.SetHandler(() =>
    {
      var code = TaskbarWidgetManager.Disable();
      Environment.Exit(code);
    });

    var taskbar = new Command("taskbar", "Taskbar network speed widget")
    {
      taskbarEnable,
      taskbarDisable,
    };
    root.AddCommand(taskbar);
#endif

    return await root.InvokeAsync(args);
  }

#if WINDOWS
  private static bool IsTaskbarRunInvocation(string[] args) =>
    args.Length >= 2
    && args[0].Equals("taskbar", StringComparison.OrdinalIgnoreCase)
    && args[1].Equals("run", StringComparison.OrdinalIgnoreCase);

  private static void RunServiceInstall(string dbPath)
  {
    var exePath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(exePath))
    {
      ConsoleOutput.WriteError("Could not resolve the current executable path.");
      Environment.Exit(1);
      return;
    }

    dbPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dbPath));
    var code = WindowsServiceManager.Install(exePath, dbPath, out var message);
    ConsoleOutput.WriteServiceResult(code, message);
    Environment.Exit(code);
  }

  private static void RunServiceUninstall()
  {
    var code = WindowsServiceManager.Uninstall(out var message);
    ConsoleOutput.WriteServiceResult(code, message);
    Environment.Exit(code);
  }

  private static void RunServiceStart()
  {
    var code = WindowsServiceManager.Start(out var message);
    ConsoleOutput.WriteServiceResult(code, message);
    Environment.Exit(code);
  }

  private static void RunServiceStop()
  {
    var code = WindowsServiceManager.Stop(out var message);
    ConsoleOutput.WriteServiceResult(code, message);
    Environment.Exit(code);
  }

  private static void RunServiceStatus()
  {
    if (!WindowsServiceManager.IsInstalled())
    {
      ConsoleOutput.RenderServiceStatus(
        installed: false,
        WindowsServiceManager.ServiceName,
        null,
        null);
      return;
    }

    var status = WindowsServiceManager.GetStatus();
    ConsoleOutput.RenderServiceStatus(
      installed: true,
      WindowsServiceManager.ServiceName,
      WindowsServiceManager.DisplayName,
      status.ToString());
  }
#endif

  private static int RunReset()
  {
    var dbPath = DefaultDbPath;

#if WINDOWS
    var restartService = WindowsServiceManager.IsInstalled() && WindowsServiceManager.IsRunning();

    if (restartService)
    {
      var stopCode = WindowsServiceManager.Stop(out var stopMessage);
      if (stopCode != 0)
      {
        ConsoleOutput.WriteError(stopMessage);
        return stopCode;
      }

      ConsoleOutput.WriteNote("Netvan service stopped for reset.");
    }
#else
    const bool restartService = false;
#endif

    if (!TrafficDatabase.TryDeleteFiles(dbPath, out var deleteError))
    {
      ConsoleOutput.WriteError(deleteError ?? "Could not delete the database.");
      ConsoleOutput.WriteNote("Close the Netvan GUI, taskbar widget, and any other program using traffic.db, then retry.");
#if WINDOWS
      if (restartService)
        WindowsServiceManager.Start(out _);
#endif
      return 1;
    }

    if (File.Exists(dbPath))
      ConsoleOutput.WriteNote($"No database file at {dbPath} (already empty).");
    else
      ConsoleOutput.WriteSuccess($"Removed database at {dbPath}.");

#if WINDOWS
    if (restartService)
    {
      var startCode = WindowsServiceManager.Start(out var startMessage);
      if (startCode != 0)
      {
        ConsoleOutput.WriteError(startMessage);
        return startCode;
      }

      ConsoleOutput.WriteSuccess("Netvan service restarted with a fresh database and in-memory counters.");
      return 0;
    }
#endif

    ConsoleOutput.WriteNote("Service was not running; start it with netvan service start when ready.");
    return 0;
  }
}
