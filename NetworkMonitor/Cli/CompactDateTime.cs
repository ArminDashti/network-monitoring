using System.Globalization;

namespace NetworkMonitor.Cli;

internal static class CompactDateTime
{
  public const string Format = "yyMMdd'T'HHmm";

  public static string NormalizeInput(string raw)
  {
    var text = raw.Trim();
    if (text.Length == 6 && text.All(char.IsDigit))
      return text + "T0000";

    var tIndex = text.IndexOf('T');
    if (tIndex >= 0)
    {
      var timePart = text[(tIndex + 1)..];
      if (timePart.Length == 0)
        return text + "0000";
    }

    return text;
  }

  public static DateTime ParseBoundaryOrDefault(string? raw, DateTime defaultValue)
  {
    if (string.IsNullOrWhiteSpace(raw))
      return defaultValue;

    var normalized = NormalizeInput(raw);
    if (!DateTime.TryParseExact(normalized, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
    {
      Console.Error.WriteLine($"Invalid datetime '{raw}'. Expected {Format} (local time). Date-only (yyMMdd) uses T0000.");
      Environment.Exit(1);
      return default;
    }

    return DateTime.SpecifyKind(parsed, DateTimeKind.Local);
  }

  public static (string FromUtc, string ToUtc) ResolveRangeUtc(string? fromRaw, string? toRaw)
  {
    var fromLocal = ParseBoundaryOrDefault(fromRaw, DateTime.Today);
    var toLocal = ParseBoundaryOrDefault(toRaw, DateTime.Now);
    if (toLocal < fromLocal)
      (fromLocal, toLocal) = (toLocal, fromLocal);

    return (fromLocal.ToUniversalTime().ToString("O"), toLocal.ToUniversalTime().ToString("O"));
  }
}
