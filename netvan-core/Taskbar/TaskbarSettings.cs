using Netvan.Storage;

namespace Netvan.Taskbar;

internal static class TaskbarSettings
{
  private const string RegistryValueName = "NetvanTaskbar";

  public static bool IsEnabled() => NetvanConfig.Load().TaskbarEnabled;

  public static void SetEnabled(bool enabled)
  {
    Directory.CreateDirectory(NetvanPaths.Home);
    NetvanConfig.Load().WithTaskbarEnabled(enabled).Save();
  }

  public static void RegisterStartup(string exePath)
  {
    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
      @"Software\Microsoft\Windows\CurrentVersion\Run",
      writable: true);
    key?.SetValue(RegistryValueName, $"\"{exePath}\" taskbar run");
  }

  public static void UnregisterStartup()
  {
    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
      @"Software\Microsoft\Windows\CurrentVersion\Run",
      writable: true);
    key?.DeleteValue(RegistryValueName, throwOnMissingValue: false);
  }
}
