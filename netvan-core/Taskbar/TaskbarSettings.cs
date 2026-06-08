using System.Text.Json;
using System.Text.Json.Nodes;
using Netvan.Storage;

namespace Netvan.Taskbar;

internal static class TaskbarSettings
{
  private const string TaskbarEnabledKey = "taskbarEnabled";
  private const string RegistryValueName = "NetMTaskbar";

  public static bool IsEnabled()
  {
    if (!File.Exists(NetmPaths.SettingsFile))
      return false;

    try
    {
      var json = File.ReadAllText(NetmPaths.SettingsFile);
      var node = JsonNode.Parse(json)?.AsObject();
      return node?[TaskbarEnabledKey]?.GetValue<bool>() ?? false;
    }
    catch
    {
      return false;
    }
  }

  public static void SetEnabled(bool enabled)
  {
    Directory.CreateDirectory(NetmPaths.Home);

    JsonObject root;
    if (File.Exists(NetmPaths.SettingsFile))
    {
      try
      {
        root = JsonNode.Parse(File.ReadAllText(NetmPaths.SettingsFile))?.AsObject() ?? new JsonObject();
      }
      catch
      {
        root = new JsonObject();
      }
    }
    else
    {
      root = new JsonObject
      {
        ["databasePath"] = NetmConfig.Load().ResolvedDatabasePath,
        ["createdAt"] = DateTime.UtcNow.ToString("O"),
        ["version"] = "1.0",
      };
    }

    root[TaskbarEnabledKey] = enabled;

    var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(NetmPaths.SettingsFile, text);
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
