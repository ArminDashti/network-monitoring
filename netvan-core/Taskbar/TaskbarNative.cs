using System.Runtime.InteropServices;

namespace Netvan.Taskbar;

internal static class TaskbarNative
{
  private const int SwHide = 0;
  private const int GwlStyle = -16;
  private const int WsPopup = unchecked((int)0x80000000);
  private const int WsChild = 0x40000000;
  private const int WsVisible = 0x10000000;

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

  [DllImport("user32.dll", SetLastError = true)]
  private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

  [DllImport("kernel32.dll")]
  private static extern void SetLastError(uint dwErrCode);

  [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
  private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

  [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
  private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

  [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
  private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

  [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
  private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

  private static readonly IntPtr HwndTopmost = new(-1);
  private const uint SwpNoActivate = 0x0010;
  private const uint SwpShowWindow = 0x0040;
  private const uint SwpNoSize = 0x0001;
  private const uint SwpNoMove = 0x0002;
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
    SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoActivate | SwpShowWindow | SwpNoMove | SwpNoSize);
  }

  public static bool TryAttachToTaskbar(IntPtr widgetHandle)
  {
    var trayNotify = FindTrayNotifyWindow();
    if (trayNotify == IntPtr.Zero)
      return false;

    SetLastError(0);
    var previousParent = SetParent(widgetHandle, trayNotify);
    var parentError = Marshal.GetLastWin32Error();
    if (previousParent == IntPtr.Zero && parentError != 0)
      return false;

    MakeChildWindow(widgetHandle);
    if (!SetWindowPos(widgetHandle, IntPtr.Zero, 0, 0, 0, 0, SwpNoActivate | SwpShowWindow | SwpNoMove | SwpNoSize))
      return false;

    return true;
  }

  public static Point GetWidgetLocationClient(int widgetWidth, int widgetHeight)
  {
    var trayNotify = FindTrayNotifyWindow();
    if (trayNotify == IntPtr.Zero || !GetWindowRect(trayNotify, out var trayRect))
      return new Point(4, 2);

    var screen = GetWidgetLocationScreen(widgetWidth, widgetHeight);
    return new Point(screen.X - trayRect.Left, screen.Y - trayRect.Top);
  }

  public static Point GetWidgetLocationScreen(int widgetWidth, int widgetHeight)
  {
    var trayNotify = FindTrayNotifyWindow();
    if (trayNotify != IntPtr.Zero && GetWindowRect(trayNotify, out var trayRect))
    {
      if (TryGetChevronRect(trayNotify, out var chevronRect))
      {
        var x = chevronRect.Left - widgetWidth - 6;
        var y = chevronRect.Top + Math.Max(0, (chevronRect.Height - widgetHeight) / 2);
        return ClampToTray(trayRect, x, y, widgetWidth, widgetHeight);
      }

      return new Point(
        trayRect.Left + 4,
        trayRect.Top + Math.Max(0, (trayRect.Height - widgetHeight) / 2));
    }

    var shellTray = FindWindow("Shell_TrayWnd", null);
    if (shellTray != IntPtr.Zero && GetWindowRect(shellTray, out var taskbarRect))
    {
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

  private static IntPtr FindTrayNotifyWindow()
  {
    var shellTray = FindWindow("Shell_TrayWnd", null);
    if (shellTray == IntPtr.Zero)
      return IntPtr.Zero;

    return FindWindowEx(shellTray, IntPtr.Zero, "TrayNotifyWnd", null);
  }

  private static bool TryGetChevronRect(IntPtr trayNotify, out Rect chevronRect)
  {
    chevronRect = default;

    var chevron = FindWindowEx(trayNotify, IntPtr.Zero, "Button", null);
    if (chevron != IntPtr.Zero && GetWindowRect(chevron, out chevronRect))
      return true;

    var rightmost = IntPtr.Zero;
    var rightmostLeft = int.MinValue;
    var child = IntPtr.Zero;
    while (true)
    {
      child = FindWindowEx(trayNotify, child, null, null);
      if (child == IntPtr.Zero)
        break;

      if (!GetWindowRect(child, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        continue;

      if (rect.Left > rightmostLeft)
      {
        rightmostLeft = rect.Left;
        rightmost = child;
        chevronRect = rect;
      }
    }

    return rightmost != IntPtr.Zero;
  }

  private static Point ClampToTray(Rect trayRect, int x, int y, int widgetWidth, int widgetHeight)
  {
    var minX = trayRect.Left + 2;
    var maxX = Math.Max(minX, trayRect.Right - widgetWidth - 2);
    x = Math.Clamp(x, minX, maxX);

    var minY = trayRect.Top + 1;
    var maxY = Math.Max(minY, trayRect.Bottom - widgetHeight - 1);
    y = Math.Clamp(y, minY, maxY);

    return new Point(x, y);
  }

  private static void MakeChildWindow(IntPtr handle)
  {
    var style = GetWindowLong(handle, GwlStyle);
    style &= ~WsPopup;
    style |= WsChild | WsVisible;
    SetWindowLong(handle, GwlStyle, style);
  }

  private static int GetWindowLong(IntPtr hWnd, int nIndex) =>
    IntPtr.Size == 8
      ? (int)GetWindowLongPtr64(hWnd, nIndex)
      : GetWindowLong32(hWnd, nIndex);

  private static void SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
  {
    if (IntPtr.Size == 8)
      SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong));
    else
      SetWindowLong32(hWnd, nIndex, dwNewLong);
  }
}
