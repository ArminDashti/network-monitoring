#if WINDOWS
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

    public static string GetAppName(uint pid)
    {
        if (pid == 0)
            return "system";

        var h = OpenProcess(ProcessQueryLimitedInformation, false, (int)pid);
        if (h == nint.Zero)
            return $"pid-{pid}";

        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            if (!QueryFullProcessImageNameW(h, 0, sb, ref size))
                return $"pid-{pid}";

            var path = sb.ToString();
            return Path.GetFileNameWithoutExtension(path);
        }
        finally
        {
            _ = CloseHandle(h);
        }
    }
}
#endif
