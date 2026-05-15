using System.Runtime.InteropServices;
using System.Text;

namespace NetworkMonitor.Services;

internal static class ProcessNameResolver
{
    private const uint ProcessQueryLimitedInformation = 0x1000; // Windows flag to read process info without full access

    // Opens a Windows process handle for the given process id
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    // Closes a Windows handle when we are done with it
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    // Reads the full executable path for an open process handle
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(nint hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    // Returns the application file name for a process id, or a fallback label
    public static string GetAppName(uint pid)
    {
        if (pid == 0) // Kernel or system traffic without an owning process
            return "system";

        var h = OpenProcess(ProcessQueryLimitedInformation, false, (int)pid); // Ask Windows for a read-only process handle
        if (h == nint.Zero) // Access denied or process already exited
            return $"pid-{pid}";

        try
        {
            var sb = new StringBuilder(1024); // Buffer for the executable path string
            var size = sb.Capacity; // Tell the API how large our buffer is
            if (!QueryFullProcessImageNameW(h, 0, sb, ref size)) // Fill buffer with image path
                return $"pid-{pid}";

            var path = sb.ToString(); // Full path like C:\Program Files\App\app.exe
            return Path.GetFileNameWithoutExtension(path); // Show only the exe name to the user
        }
        finally
        {
            _ = CloseHandle(h); // Always release the handle
        }
    }
}
