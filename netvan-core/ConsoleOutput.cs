namespace Netvan;

internal static class ConsoleOutput
{
  public static void WriteError(string message) =>
    Console.Error.WriteLine($"Error: {message}");

  public static void WriteSuccess(string message) =>
    Console.WriteLine(message);

  public static void WriteNote(string message) =>
    Console.WriteLine(message);

  public static void WriteServiceResult(int exitCode, string message)
  {
    if (exitCode == 0)
      WriteSuccess(message);
    else
      WriteError(message);
  }

  public static void RenderServiceStatus(
    bool installed,
    string? serviceName,
    string? displayName,
    string? status)
  {
    if (!installed)
    {
      Console.WriteLine($"Service '{serviceName}' is not installed.");
      Console.WriteLine("Install with: netvan service install");
      return;
    }

    Console.WriteLine($"Service: {serviceName}");
    Console.WriteLine($"Display: {displayName}");
    Console.WriteLine($"Status: {status ?? "unknown"}");
    Console.WriteLine("Start: netvan service start  ·  Stop: netvan service stop");
  }
}
