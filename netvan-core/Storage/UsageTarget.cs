namespace Netvan.Storage;

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

internal readonly record struct UsageTarget(UsageTargetKind Kind, string? Value);
