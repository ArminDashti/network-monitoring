namespace Netvan.Cli;

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

  public static string FormatPeriodBytes(long bytes)
  {
    if (bytes < 0)
      bytes = 0;

    const double k = 1024d;
    var mb = bytes / (k * k);
    if (mb < k)
      return mb < 100 ? $"{mb:0.00} MB" : $"{mb:0.0} MB";

    var gb = mb / k;
    return gb < 100 ? $"{gb:0.00} GB" : $"{gb:0.0} GB";
  }

  public static string FormatDownUpPair(long downBytes, long upBytes) =>
    $"[green]{FormatPeriodBytes(downBytes)}[/] / [red]{FormatPeriodBytes(upBytes)}[/]";
}
