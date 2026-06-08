using System.Diagnostics;
using System.Text.Json;
using Netvan.Cli;
using Netvan.Storage;

namespace Netvan.Taskbar;

internal readonly record struct TaskbarState(int ProcessId, DateTime StartedUtc);

internal static class TaskbarWidgetManager
{
  private static string PidFile => Path.Combine(NetmPaths.Home, "taskbar.pid");

  public static int Enable()
  {
#if !WINDOWS
    ConsoleUi.WriteError("Taskbar widget requires a Windows build (net9.0-windows).");
    return 1;
#else
    Directory.CreateDirectory(NetmPaths.Home);

    var exe = Environment.ProcessPath;
    if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
    {
      ConsoleUi.WriteError("Could not resolve netm executable path.");
      return 1;
    }

    TaskbarSettings.SetEnabled(true);
    TaskbarSettings.RegisterStartup(exe);

    if (TryReadState(out var existing) && IsProcessRunning(existing.ProcessId))
    {
      ConsoleUi.WriteSuccess($"Taskbar widget already running (PID {existing.ProcessId}).");
      ConsoleUi.WriteNote("Startup entry updated.");
      return 0;
    }

    CleanupStalePidFile();
    return StartWidgetProcess(exe);
#endif
  }

  public static int Disable()
  {
#if !WINDOWS
    ConsoleUi.WriteError("Taskbar widget requires a Windows build (net9.0-windows).");
    return 1;
#else
    TaskbarSettings.SetEnabled(false);
    TaskbarSettings.UnregisterStartup();

    if (!TryReadState(out var state))
    {
      ConsoleUi.WriteSuccess("Taskbar widget disabled.");
      return 0;
    }

    if (!IsProcessRunning(state.ProcessId))
    {
      CleanupStalePidFile();
      ConsoleUi.WriteSuccess("Taskbar widget disabled.");
      return 0;
    }

    try
    {
      var process = Process.GetProcessById(state.ProcessId);
      process.Kill(entireProcessTree: true);
      process.WaitForExit(3000);
    }
    catch (ArgumentException)
    {
      // Process already exited.
    }
    catch (Exception ex)
    {
      ConsoleUi.WriteError($"Failed to stop taskbar widget: {ex.Message}");
      return 1;
    }

    CleanupStalePidFile();
    ConsoleUi.WriteSuccess("Taskbar widget disabled.");
    return 0;
#endif
  }

  public static int RunWidget()
  {
#if !WINDOWS
    return 1;
#else
    using var mutex = new Mutex(true, @"Global\NetM.TaskbarWidget", out var createdNew);
    if (!createdNew)
      return 0;

    SettingsManager.Initialize();
    if (!TaskbarSettings.IsEnabled())
      return 0;

    TaskbarNative.HideConsole();
    WriteState(new TaskbarState(Environment.ProcessId, DateTime.UtcNow));

    try
    {
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.Run(new TaskbarOverlayForm());
      return 0;
    }
    finally
    {
      ClearState();
    }
#endif
  }

  private static int StartWidgetProcess(string exe)
  {
    var psi = new ProcessStartInfo
    {
      FileName = exe,
      Arguments = "taskbar run",
      WorkingDirectory = NetmPaths.Home,
      CreateNoWindow = true,
      UseShellExecute = false,
    };

    Process? child;
    try
    {
      child = Process.Start(psi);
    }
    catch (Exception ex)
    {
      ConsoleUi.WriteError($"Failed to start taskbar widget: {ex.Message}");
      return 1;
    }

    if (child is null)
    {
      ConsoleUi.WriteError("Failed to start taskbar widget process.");
      return 1;
    }

    if (!WaitForState(TimeSpan.FromSeconds(5)))
    {
      ConsoleUi.WriteError("Taskbar widget started but did not report ready in time.");
      return 1;
    }

    if (!TryReadState(out var state))
    {
      ConsoleUi.WriteError("Taskbar widget started but state file is missing.");
      return 1;
    }

    ConsoleUi.WriteSuccess($"Taskbar widget enabled (PID {state.ProcessId}).");
    ConsoleUi.WriteNote("Upload/download speeds appear in the Windows taskbar.");
    return 0;
  }

  internal static void WriteState(TaskbarState state)
  {
    var json = JsonSerializer.Serialize(state);
    File.WriteAllText(PidFile, json);
  }

  internal static void ClearState()
  {
    if (File.Exists(PidFile))
      File.Delete(PidFile);
  }

  private static bool TryReadState(out TaskbarState state)
  {
    state = default;
    if (!File.Exists(PidFile))
      return false;

    try
    {
      var json = File.ReadAllText(PidFile);
      state = JsonSerializer.Deserialize<TaskbarState>(json);
      return state.ProcessId > 0;
    }
    catch
    {
      return false;
    }
  }

  private static bool IsProcessRunning(int processId)
  {
    if (processId <= 0)
      return false;

    try
    {
      var process = Process.GetProcessById(processId);
      return !process.HasExited;
    }
    catch (ArgumentException)
    {
      return false;
    }
  }

  private static void CleanupStalePidFile()
  {
    if (File.Exists(PidFile))
      File.Delete(PidFile);
  }

  private static bool WaitForState(TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      if (TryReadState(out var state) && IsProcessRunning(state.ProcessId))
        return true;

      Thread.Sleep(100);
    }

    return false;
  }
}
