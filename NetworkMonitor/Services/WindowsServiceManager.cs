#if WINDOWS
using System.Diagnostics;
using System.ServiceProcess;

namespace NetworkMonitor.Services;

internal static class WindowsServiceManager
{
    public const string ServiceName = "NetM";
    public const string DisplayName = "Network Monitor";

    public static bool IsInstalled()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static ServiceControllerStatus? GetStatus()
    {
        if (!IsInstalled())
            return null;

        using var controller = new ServiceController(ServiceName);
        return controller.Status;
    }

    public static int Install(string executablePath, int intervalSeconds, string dbPath, out string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            message = "Windows service installation requires Windows.";
            return 1;
        }

        if (!File.Exists(executablePath))
        {
            message = $"Executable not found: {executablePath}";
            return 1;
        }

        if (intervalSeconds < 1)
        {
            message = "Interval must be at least 1 second.";
            return 1;
        }

        if (IsInstalled())
        {
            message = $"Service '{ServiceName}' is already installed. Run 'netm service uninstall' first to reinstall.";
            return 1;
        }

        var binPath = BuildBinaryPath(executablePath, intervalSeconds, dbPath);
        var createArgs = $"create {ServiceName} binPath= {binPath} start= auto DisplayName= \"{DisplayName}\"";
        if (!RunSc(createArgs, out var createError))
        {
            message = createError;
            return 1;
        }

        RunSc($"description {ServiceName} \"Samples TCP traffic into a local SQLite database.\"", out _);
        message = $"Installed service '{ServiceName}'. Start with: netm service start";
        return 0;
    }

    public static int Uninstall(out string message)
    {
        if (!IsInstalled())
        {
            message = $"Service '{ServiceName}' is not installed.";
            return 0;
        }

        try
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }
        catch (Exception ex)
        {
            message = $"Failed to stop service before uninstall: {ex.Message}";
            return 1;
        }

        if (!RunSc($"delete {ServiceName}", out var deleteError))
        {
            message = deleteError;
            return 1;
        }

        message = $"Removed service '{ServiceName}'.";
        return 0;
    }

    public static int Start(out string message)
    {
        if (!IsInstalled())
        {
            message = $"Service '{ServiceName}' is not installed. Run: netm service install";
            return 1;
        }

        try
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.Status == ServiceControllerStatus.Running)
            {
                message = $"Service '{ServiceName}' is already running.";
                return 0;
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            message = $"Service '{ServiceName}' started.";
            return 0;
        }
        catch (Exception ex)
        {
            message = $"Failed to start service: {ex.Message}";
            return 1;
        }
    }

    public static int Stop(out string message)
    {
        if (!IsInstalled())
        {
            message = $"Service '{ServiceName}' is not installed.";
            return 1;
        }

        try
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                message = $"Service '{ServiceName}' is already stopped.";
                return 0;
            }

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            message = $"Service '{ServiceName}' stopped.";
            return 0;
        }
        catch (Exception ex)
        {
            message = $"Failed to stop service: {ex.Message}";
            return 1;
        }
    }

    public static string BuildBinaryPath(string executablePath, int intervalSeconds, string dbPath) =>
        $"\"\\\"{executablePath}\\\" run --interval {intervalSeconds} --db \\\"{dbPath}\\\"\"";

    private static bool RunSc(string arguments, out string error)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            error = "Failed to start sc.exe.";
            return false;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            error = string.Empty;
            return true;
        }

        error = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
        if (string.IsNullOrWhiteSpace(error))
            error = $"sc.exe exited with code {process.ExitCode}.";
        return false;
    }
}
#endif
