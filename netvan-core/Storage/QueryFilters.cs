using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;

namespace Netvan.Storage;

internal static class QueryFilters
{
  public const int TopUsageLimit = 100;

  public static string PrivateIpExcludeClause(bool includePrivate, string prefix = "AND ")
  {
    if (includePrivate)
      return "";

    return $"""
      {prefix}(
        remote_ip NOT GLOB '10.*'
        AND remote_ip NOT GLOB '127.*'
        AND remote_ip NOT GLOB '192.168.*'
        AND remote_ip NOT GLOB '169.254.*'
        AND remote_ip NOT GLOB '172.1[6-9].*'
        AND remote_ip NOT GLOB '172.2[0-9].*'
        AND remote_ip NOT GLOB '172.3[0-1].*'
      )
      """;
  }

  public static string TargetClause(UsageTargetKind kind, string? value)
  {
    return kind switch
    {
      UsageTargetKind.SpecificApp => "AND app_name = $target\n",
      UsageTargetKind.SpecificIp => "AND remote_ip = $target\n",
      UsageTargetKind.SpecificHost => HostMatchClause(),
      _ => "",
    };
  }

  public static void AddTargetParameters(SqliteCommand cmd, UsageTargetKind kind, string? value)
  {
    if (kind == UsageTargetKind.SpecificApp || kind == UsageTargetKind.SpecificIp)
      cmd.Parameters.AddWithValue("$target", value ?? "");
    else if (kind == UsageTargetKind.SpecificHost)
      AddHostMatchParameters(cmd, value);
  }

  public static string HostMatchClause(string prefix = "AND ") =>
    $"""
      {prefix}(
        LOWER(host_name) = LOWER($target)
        OR LOWER(host_name) = LOWER($targetDot)
        OR host_name LIKE $targetSub ESCAPE '\'
      )
      """;

  public static void AddHostMatchParameters(SqliteCommand cmd, string? filterHost)
  {
    var host = NormalizeHostFilter(filterHost);
    cmd.Parameters.AddWithValue("$target", host);
    cmd.Parameters.AddWithValue("$targetDot", host + ".");
    cmd.Parameters.AddWithValue("$targetSub", "%." + host);
  }

  private static string NormalizeHostFilter(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return "";

    return value.Trim().TrimEnd('.');
  }

  public static bool IsPrivateRemoteIp(string remoteIp)
  {
    if (!IPAddress.TryParse(remoteIp, out var address))
      return false;

    if (IPAddress.IsLoopback(address))
      return true;

    if (address.AddressFamily != AddressFamily.InterNetwork)
      return false;

    var bytes = address.GetAddressBytes();
    if (bytes[0] == 10)
      return true;
    if (bytes[0] == 127)
      return true;
    if (bytes[0] == 192 && bytes[1] == 168)
      return true;
    if (bytes[0] == 169 && bytes[1] == 254)
      return true;
    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
      return true;
    return false;
  }
}
