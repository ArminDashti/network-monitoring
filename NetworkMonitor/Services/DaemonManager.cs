using System.Diagnostics;
using System.Text.Json;
using NetworkMonitor.Cli;
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
        ConsoleUi.WriteError("Background collection requires a Windows build (net9.0-windows).");
        return 1;
#else
        Directory.CreateDirectory(NetmPaths.Home);

        if (TryReadState(out var existing) && IsProcessRunning(existing.ProcessId))
        {
            ConsoleUi.WriteError($"Collector already running (PID {existing.ProcessId}).");
            return 1;
        }

        CleanupStalePidFile();

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            ConsoleUi.WriteError("Could not resolve netm executable path.");
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
            ConsoleUi.WriteError($"Failed to start collector: {ex.Message}");
            return 1;
        }

        if (child is null)
        {
            ConsoleUi.WriteError("Failed to start collector process.");
            return 1;
        }

        if (!WaitForState(TimeSpan.FromSeconds(10)))
        {
            ConsoleUi.WriteError("Collector process started but did not report ready in time.");
            return 1;
        }

        if (!TryReadState(out var state))
        {
            ConsoleUi.WriteError("Collector started but state file is missing.");
            return 1;
        }

        ConsoleUi.WriteSuccess($"Collector started (PID {state.ProcessId}).");
        ConsoleUi.WriteNote($"Database: {state.DatabasePath}");
        return 0;
#endif
    }

    public static int Stop()
    {
        if (!TryReadState(out var state))
        {
            ConsoleUi.WriteNote("Collector is not running.");
            CleanupStalePidFile();
            return 0;
        }

        if (!IsProcessRunning(state.ProcessId))
        {
            ConsoleUi.WriteWarning("Collector is not running (stale PID file removed).");
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
            ConsoleUi.WriteError($"Failed to stop collector (PID {state.ProcessId}): {ex.Message}");
            return 1;
        }

        CleanupStalePidFile();
        ConsoleUi.WriteSuccess($"Collector stopped (PID {state.ProcessId}).");
        return 0;
    }

    public static int Status()
    {
        var config = NetmConfig.Load();

        if (!TryReadState(out var state))
        {
            ConsoleUi.RenderDaemonStatus(
                NetmPaths.Home,
                NetmPaths.ConfigFile,
                config.ResolvedDatabasePath,
                isRunning: false,
                isStale: false,
                null, null, null, null, null, null, null);
            return 0;
        }

        if (!IsProcessRunning(state.ProcessId))
        {
            ConsoleUi.RenderDaemonStatus(
                NetmPaths.Home,
                NetmPaths.ConfigFile,
                config.ResolvedDatabasePath,
                isRunning: false,
                isStale: true,
                state.ProcessId,
                null, null, null, null, null, null);
            CleanupStalePidFile();
            return 0;
        }

        var uptime = DateTime.UtcNow - state.StartedUtc;
        long? rowCount = null;
        string? lastMinuteUtc = null;
        string? databaseError = null;

        if (File.Exists(config.ResolvedDatabasePath))
        {
            try
            {
                using var store = new TrafficStore(config.ResolvedDatabasePath, config.SamplingIntervalSeconds);
                var info = store.GetDatabaseInfo(config.ResolvedDatabasePath);
                rowCount = info.RowCount;
                lastMinuteUtc = info.LastMinuteUtc;
            }
            catch (Exception ex)
            {
                databaseError = ex.Message;
            }
        }

        ConsoleUi.RenderDaemonStatus(
            NetmPaths.Home,
            NetmPaths.ConfigFile,
            config.ResolvedDatabasePath,
            isRunning: true,
            isStale: false,
            state.ProcessId,
            state.StartedUtc,
            uptime,
            config.SamplingIntervalSeconds,
            rowCount,
            lastMinuteUtc,
            databaseError);

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
}
