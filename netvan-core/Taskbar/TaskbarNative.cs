using System.Runtime.InteropServices;

namespace Netvan.Taskbar;

internal static class TaskbarNative
{
  private const int SwHide = 0;

  [DllImport("kernel32.dll")]
  private static extern IntPtr GetConsoleWindow();

  [DllImport("user32.dll")]
  private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

  [DllImport("user32.dll")]
  private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

  [DllImport("user32.dll")]
  private static extern int GetSystemMetrics(int nIndex);

  [DllImport("user32.dll")]
  private static extern bool SetWindowPos(
    IntPtr hWnd,
    IntPtr hWndInsertAfter,
    int x,
    int y,
    int cx,
    int cy,
    uint uFlags);

  private static readonly IntPtr HwndTopmost = new(-1);
  private const uint SwpNoActivate = 0x0010;
  private const uint SwpShowWindow = 0x0040;
  private const int SmCxScreen = 0;
  private const int SmCyScreen = 1;

  [StructLayout(LayoutKind.Sequential)]
  internal struct Rect
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
  }

  public static void HideConsole()
  {
    var handle = GetConsoleWindow();
    if (handle != IntPtr.Zero)
      ShowWindow(handle, SwHide);
  }

  public static void KeepTopMost(IntPtr handle)
  {
    SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoActivate | SwpShowWindow | 0x0001 | 0x0002);
  }

  public static Point GetWidgetLocation(int widgetWidth, int widgetHeight)
  {
    var shellTray = FindWindow("Shell_TrayWnd", null);
    if (shellTray != IntPtr.Zero && GetWindowRect(shellTray, out var taskbarRect))
    {
      var trayNotify = FindWindowEx(shellTray, IntPtr.Zero, "TrayNotifyWnd", null);
      if (trayNotify != IntPtr.Zero && GetWindowRect(trayNotify, out var trayRect))
      {
        var x = trayRect.Left + 4;
        var y = trayRect.Top + Math.Max(0, (trayRect.Height - widgetHeight) / 2);
        return new Point(x, y);
      }

      if (taskbarRect.Width > taskbarRect.Height)
      {
        var x = taskbarRect.Right - widgetWidth - 180;
        var y = taskbarRect.Top + Math.Max(0, (taskbarRect.Height - widgetHeight) / 2);
        return new Point(x, y);
      }

      var verticalX = taskbarRect.Left + Math.Max(0, (taskbarRect.Width - widgetWidth) / 2);
      var verticalY = taskbarRect.Bottom - widgetHeight - 8;
      return new Point(verticalX, verticalY);
    }

    var screenWidth = GetSystemMetrics(SmCxScreen);
    var screenHeight = GetSystemMetrics(SmCyScreen);
    return new Point(screenWidth - widgetWidth - 180, screenHeight - widgetHeight - 8);
  }
}
