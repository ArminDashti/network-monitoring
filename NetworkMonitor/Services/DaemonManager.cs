using System.Diagnostics;
using System.Text.Json;
using NetworkMonitor.Storage;

namespace NetworkMonitor.Services;

internal readonly record struct DaemonState(
    int ProcessId,
    DateTime StartedUtc,
    string DatabasePath);

internal static class DaemonManager
{
    public static int Start()
    {
#if !WINDOWS
        Console.Error.WriteLine("Background collection requires a Windows build (net10.0-windows).");
        return 1;
#else
        Directory.CreateDirectory(NetmPaths.Home);

        if (TryReadState(out var existing) && IsProcessRunning(existing.ProcessId))
        {
            Console.Error.WriteLine($"Collector already running (PID {existing.ProcessId}).");
            return 1;
        }

        CleanupStalePidFile();

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            Console.Error.WriteLine("Could not resolve netm executable path.");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "collect",
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
            Console.Error.WriteLine($"Failed to start collector: {ex.Message}");
            return 1;
        }

        if (child is null)
        {
            Console.Error.WriteLine("Failed to start collector process.");
            return 1;
        }

        if (!WaitForState(TimeSpan.FromSeconds(10)))
        {
            Console.Error.WriteLine("Collector process started but did not report ready in time.");
            return 1;
        }

        if (!TryReadState(out var state))
        {
            Console.Error.WriteLine("Collector started but state file is missing.");
            return 1;
        }

        Console.WriteLine($"Collector started (PID {state.ProcessId}).");
        Console.WriteLine($"Database: {state.DatabasePath}");
        return 0;
#endif
    }

    public static int Stop()
    {
        if (!TryReadState(out var state))
        {
            Console.WriteLine("Collector is not running.");
            CleanupStalePidFile();
            return 0;
        }

        if (!IsProcessRunning(state.ProcessId))
        {
            Console.WriteLine("Collector is not running (stale PID file removed).");
            CleanupStalePidFile();
            return 0;
        }

        try
        {
            var proc = Process.GetProcessById(state.ProcessId);
            proc.CloseMainWindow();
            if (!proc.WaitForExit(3000))
                proc.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to stop collector (PID {state.ProcessId}): {ex.Message}");
            return 1;
        }

        CleanupStalePidFile();
        Console.WriteLine($"Collector stopped (PID {state.ProcessId}).");
        return 0;
    }

    public static int Status()
    {
        var config = NetmConfig.Load();
        Console.WriteLine($"Home:     {NetmPaths.Home}");
        Console.WriteLine($"Config:   {NetmPaths.ConfigFile}");
        Console.WriteLine($"Database: {config.ResolvedDatabasePath}");

        if (!TryReadState(out var state))
        {
            Console.WriteLine("Status:   stopped");
            return 0;
        }

        if (!IsProcessRunning(state.ProcessId))
        {
            Console.WriteLine($"Status:   stopped (stale PID {state.ProcessId})");
            CleanupStalePidFile();
            return 0;
        }

        var uptime = DateTime.UtcNow - state.StartedUtc;
        Console.WriteLine($"Status:   running");
        Console.WriteLine($"PID:      {state.ProcessId}");
        Console.WriteLine($"Started:  {state.StartedUtc:O} UTC");
        Console.WriteLine($"Uptime:   {FormatUptime(uptime)}");
        Console.WriteLine($"Interval: {config.SamplingIntervalSeconds}s");

        if (File.Exists(config.ResolvedDatabasePath))
        {
            try
            {
                using var store = new TrafficStore(config.ResolvedDatabasePath);
                var info = store.GetDatabaseInfo(config.ResolvedDatabasePath);
                Console.WriteLine($"Rows:     {info.RowCount:N0}");
                Console.WriteLine($"Last UTC: {info.LastMinuteUtc ?? "(none)"}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database: unavailable ({ex.Message})");
            }
        }

        return 0;
    }

    public static void WriteState(DaemonState state)
    {
        Directory.CreateDirectory(NetmPaths.Home);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(NetmPaths.PidFile, json);
    }

    public static void ClearState()
    {
        try
        {
            if (File.Exists(NetmPaths.PidFile))
                File.Delete(NetmPaths.PidFile);
        }
        catch
        {
            // Best effort.
        }
    }

    public static bool TryReadState(out DaemonState state)
    {
        state = default;
        if (!File.Exists(NetmPaths.PidFile))
            return false;

        try
        {
            var json = File.ReadAllText(NetmPaths.PidFile);
            var parsed = JsonSerializer.Deserialize<DaemonState>(json);
            if (parsed.ProcessId <= 0)
                return false;

            state = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForState(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (TryReadState(out var state) && IsProcessRunning(state.ProcessId))
                return true;
            Thread.Sleep(200);
        }

        return TryReadState(out _);
    }

    private static void CleanupStalePidFile()
    {
        if (!TryReadState(out var state))
            return;

        if (!IsProcessRunning(state.ProcessId))
            ClearState();
    }

    public static bool IsCollectorRunning()
    {
        return TryReadState(out var state) && IsProcessRunning(state.ProcessId);
    }

    private static bool IsProcessRunning(int pid)
    {
        if (pid <= 0)
            return false;

        try
        {
            var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }
}
