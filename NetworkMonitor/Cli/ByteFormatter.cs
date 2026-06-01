namespace NetworkMonitor.Cli;

internal static class ByteFormatter
{
  public static string FormatBytes(long value)
  {
    if (value < 0)
      value = 0;

    const double k = 1024d;
    if (value < k)
      return $"{value} B";
    if (value < k * k)
      return $"{value / k:0.##} KB";
    if (value < k * k * k)
      return $"{value / (k * k):0.##} MB";
    if (value < k * k * k * k)
      return $"{value / (k * k * k):0.##} GB";
    return $"{value / (k * k * k * k):0.##} TB";
  }

  public static string FormatMegabytes(long bytes) => $"{bytes / (1024d * 1024d):0.##} MB";

  public static string FormatGigabytes(long bytes) => $"{bytes / (1024d * 1024d * 1024d):0.##} GB";

  public static string FormatDownUpPair(long downBytes, long upBytes) =>
    $"[green]{FormatGigabytes(downBytes)}[/] / [red]{FormatGigabytes(upBytes)}[/]";
}
