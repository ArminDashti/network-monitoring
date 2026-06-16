#if WINDOWS
using System.Diagnostics;

namespace Netvan.Services;

internal static class GuiLauncher
{
  private sealed record GuiLaunchTarget(string ExePath, string WorkingDir, string Arguments = "");

  public static int Launch()
  {
    var installDir = ResolveInstallDirectory();
    Environment.SetEnvironmentVariable("NETVAN_HOME", installDir);

    foreach (var target in FindGuiCandidates(installDir))
    {
      if (!File.Exists(target.ExePath))
        continue;

      try
      {
        var startInfo = new ProcessStartInfo
        {
          FileName = target.ExePath,
          Arguments = target.Arguments,
          WorkingDirectory = target.WorkingDir,
          UseShellExecute = false,
        };
        startInfo.Environment["NETVAN_HOME"] = installDir;

        Process.Start(startInfo);
        return 0;
      }
      catch (Exception ex)
      {
        ConsoleOutput.WriteError($"Failed to start GUI ({target.ExePath}): {ex.Message}");
        return 1;
      }
    }

    ConsoleOutput.WriteError(
      "Netvan GUI not found. Re-run export.ps1 to build and install the GUI.");
    return 1;
  }

  private static string ResolveInstallDirectory()
  {
    var exePath = Environment.ProcessPath;
    if (!string.IsNullOrWhiteSpace(exePath))
      return Path.GetDirectoryName(exePath)!;

    return Storage.NetvanPaths.Home;
  }

  private static IEnumerable<GuiLaunchTarget> FindGuiCandidates(string installDir)
  {
    var repoRoot = Path.GetFullPath(Path.Combine(installDir, ".."));
    var guiDir = Path.Combine(installDir, "gui");

    yield return new GuiLaunchTarget(Path.Combine(guiDir, "Netvan.exe"), guiDir);

    var devGuiDir = Path.Combine(repoRoot, "netvan-gui");
    var devElectron = Path.Combine(devGuiDir, "node_modules", "electron", "dist", "electron.exe");
    if (File.Exists(Path.Combine(devGuiDir, "main.js")))
      yield return new GuiLaunchTarget(devElectron, devGuiDir, ".");
  }
}
#endif
