using Netvan.Services;
using Netvan.Storage;

namespace Netvan;

internal static class Program
{
  [STAThread]
  public static async Task<int> Main(string[] args)
  {
    Directory.CreateDirectory(NetvanPaths.Home);
    SettingsManager.Initialize();
#if WINDOWS
#if WINDOWS
    if (!Environment.UserInteractive)
      return await ServiceHostRunner.RunAsync();

    return GuiLauncher.Launch();
#else
    Console.WriteLine("Netvan service host runs on Windows only.");
    return 1;
#endif
  }
}
