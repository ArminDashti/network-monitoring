using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkMonitor.Services;

internal static class ProcessNameResolver
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(nint hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    /// <summary>
    /// Returns the full path to the process executable when the handle can be opened.
    /// </summary>
    public static bool TryGetExecutablePath(uint pid, [NotNullWhen(true)] out string? executablePath)
    {
        executablePath = null;
        if (pid == 0)
            return false;

        var h = OpenProcess(ProcessQueryLimitedInformation, false, (int)pid);
        if (h == nint.Zero)
            return false;

        try
        {
            var sb = new StringBuilder(2048);
            var size = sb.Capacity;
            if (!QueryFullProcessImageNameW(h, 0, sb, ref size))
                return false;

            executablePath = sb.ToString();
            return executablePath.Length > 0;
        }
        finally
        {
            _ = CloseHandle(h);
        }
    }

    public static string GetAppName(uint pid)
    {
        if (pid == 0)
            return "system";

        if (!TryGetExecutablePath(pid, out var path))
            return $"pid-{pid}";

        return Path.GetFileNameWithoutExtension(path);
    }
}
