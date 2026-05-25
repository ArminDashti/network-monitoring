using System.Net;

namespace NetworkMonitor.Cli;

internal enum UsageTargetKind
{
  All,
  Apps,
  IpTop100,
  HostTop100,
  SpecificApp,
  SpecificIp,
  SpecificHost,
}

internal readonly record struct UsageTarget(UsageTargetKind Kind, string? Value)
{
  public static UsageTarget Parse(string? raw)
  {
    if (string.IsNullOrWhiteSpace(raw))
      return new UsageTarget(UsageTargetKind.All, null);

    var text = raw.Trim();
    if (text.Equals("apps", StringComparison.OrdinalIgnoreCase))
      return new UsageTarget(UsageTargetKind.Apps, null);
    if (text.Equals("ip", StringComparison.OrdinalIgnoreCase))
      return new UsageTarget(UsageTargetKind.IpTop100, null);
    if (text.Equals("host", StringComparison.OrdinalIgnoreCase))
      return new UsageTarget(UsageTargetKind.HostTop100, null);

    if (IPAddress.TryParse(text, out _))
      return new UsageTarget(UsageTargetKind.SpecificIp, text);

    if (text.Contains('.', StringComparison.Ordinal))
      return new UsageTarget(UsageTargetKind.SpecificHost, text);

    return new UsageTarget(UsageTargetKind.SpecificApp, text);
  }
}
