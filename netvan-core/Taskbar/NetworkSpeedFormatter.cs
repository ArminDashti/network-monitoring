namespace Netvan.Taskbar;

internal static class NetworkSpeedFormatter
{
  /// <summary>Megabits per second from bytes observed in a 1-second bucket.</summary>
  public static string FormatMegabitsPerSecond(long bytesPerSecond)
  {
    if (bytesPerSecond < 0)
      bytesPerSecond = 0;

    var megabits = bytesPerSecond * 8d / (1024d * 1024d);
    return $"{megabits:0.0} Mb";
  }
}
