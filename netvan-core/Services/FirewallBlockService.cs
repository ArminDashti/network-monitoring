using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Netvan.Services;

/// <summary>
/// Creates Windows Defender Firewall rules via netsh (requires an elevated shell).
/// </summary>
internal static class FirewallBlockService
{
    private const string RuleNamePrefix = "netvan-block";

    /// <summary>
    /// Blocks outbound traffic to the given remote IPv4 address.
    /// </summary>
    public static bool TryBlockOutboundToIp(string remoteIp, out string errorMessage)
    {
        errorMessage = "";
        if (!IPAddress.TryParse(remoteIp.Trim(), out var address))
        {
            errorMessage = $"Invalid IP address '{remoteIp}'.";
            return false;
        }

        var normalized = address.ToString();
        var ruleName = $"{RuleNamePrefix}-ip-{SanitizeRuleToken(normalized)}";
        var args = BuildAddRuleArguments(
            ruleName,
            $"dir=out action=block protocol=any remoteip={EscapeRemoteIpForNetsh(normalized)} enable=yes");

        return TryRunNetSh(args, out errorMessage);
    }

    /// <summary>
    /// Blocks all outbound traffic for the given executable path.
    /// </summary>
    public static bool TryBlockOutboundForProgram(string programPath, out string errorMessage)
    {
        errorMessage = "";
        if (!File.Exists(programPath))
        {
            errorMessage = $"Program not found: '{programPath}'.";
            return false;
        }

        var fullPath = Path.GetFullPath(programPath);
        if (!fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Firewall program rules require a path to a .exe file.";
            return false;
        }

        var token = SanitizeRuleToken(Path.GetFileNameWithoutExtension(fullPath));
        if (token.Length > 40)
            token = token[..40];
        var ruleName = $"{RuleNamePrefix}-app-{token}-{Guid.NewGuid():N}";
        if (ruleName.Length > 200)
            ruleName = ruleName[..200];

        var args = BuildAddRuleArguments(
            ruleName,
            $"dir=out action=block program=\"{EscapeProgramPathForNetsh(fullPath)}\" protocol=any enable=yes");

        return TryRunNetSh(args, out errorMessage);
    }

    /// <summary>
    /// Resolves a URL host to IP addresses and blocks outbound traffic to those remotes.
    /// </summary>
    public static bool TryBlockOutboundToUrl(string urlInput, out string errorMessage)
    {
        errorMessage = "";
        if (!TryParseUrlHost(urlInput, out var host, out var parseError))
        {
            errorMessage = parseError;
            return false;
        }

        List<IPAddress> ips;
        try
        {
            ips = Dns.GetHostAddresses(host)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            errorMessage = $"DNS lookup failed for '{host}': {ex.Message}";
            return false;
        }

        if (ips.Count == 0)
        {
            errorMessage = $"No IP addresses resolved for host '{host}'.";
            return false;
        }

        var remoteIpArg = string.Join(",", ips.Select(ip => EscapeRemoteIpForNetsh(ip.ToString())));
        var ruleName = $"{RuleNamePrefix}-url-{SanitizeRuleToken(host)}-{Guid.NewGuid():N}";
        ruleName = ruleName.Length > 200 ? ruleName[..200] : ruleName;

        var args = BuildAddRuleArguments(
            ruleName,
            $"dir=out action=block protocol=any remoteip={remoteIpArg} enable=yes");

        return TryRunNetSh(args, out errorMessage);
    }

    /// <summary>
    /// Resolves user input to one or more .exe paths (full path, or running process name).
    /// </summary>
    public static IReadOnlyList<string> ResolveProgramPaths(string appInput)
    {
        var trimmed = appInput.Trim();
        if (trimmed.Length == 0)
            return Array.Empty<string>();

        if (LooksLikeFilePath(trimmed))
        {
            var full = Path.GetFullPath(trimmed);
            return File.Exists(full) ? new[] { full } : Array.Empty<string>();
        }

        var processName = Path.GetFileNameWithoutExtension(trimmed);
        if (processName.Length == 0)
            return Array.Empty<string>();

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in Process.GetProcessesByName(processName))
        {
            try
            {
                if (ProcessNameResolver.TryGetExecutablePath((uint)proc.Id, out var path) &&
                    path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(path))
                    paths.Add(Path.GetFullPath(path));
            }
            catch
            {
                // Process may have exited or be inaccessible; skip it.
            }
        }

        return paths.ToList();
    }

    private static bool LooksLikeFilePath(string value) =>
        value.Contains(Path.DirectorySeparatorChar) ||
        value.Contains(Path.AltDirectorySeparatorChar) ||
        (value.Length >= 2 && value[1] == ':');

    private static bool TryParseUrlHost(string urlInput, out string host, out string errorMessage)
    {
        host = "";
        var raw = urlInput.Trim();
        if (raw.Length == 0)
        {
            errorMessage = "URL is empty.";
            return false;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            Uri.TryCreate("https://" + raw, UriKind.Absolute, out uri);

        if (uri is null || string.IsNullOrWhiteSpace(uri.Host))
        {
            errorMessage = $"Could not parse a host from '{urlInput}'. Use a hostname or full URL.";
            return false;
        }

        host = uri.IdnHost.Length > 0 ? uri.IdnHost : uri.Host;
        errorMessage = "";
        return true;
    }

    private static string BuildAddRuleArguments(string ruleName, string tailArguments) =>
        $"advfirewall firewall add rule name=\"{EscapeRuleName(ruleName)}\" {tailArguments}";

    private static string EscapeRuleName(string name) => name.Replace("\"", "'", StringComparison.Ordinal);

    private static string EscapeProgramPathForNetsh(string fullPath) => fullPath.Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeRemoteIpForNetsh(string ip) => ip;

    private static string SanitizeRuleToken(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '.' or ':' or '-' or '_')
                sb.Append(char.ToLowerInvariant(c));
            else
                sb.Append('-');
        }

        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "x" : (s.Length > 80 ? s[..80] : s);
    }

    private static bool TryRunNetSh(string arguments, out string errorMessage)
    {
        errorMessage = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                errorMessage = "Failed to start netsh.";
                return false;
            }

            // Read streams before WaitForExit to avoid pipe buffer deadlocks on long output.
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            stdout = stdout.Trim();
            stderr = stderr.Trim();
            if (proc.ExitCode == 0)
                return true;

            errorMessage = string.IsNullOrWhiteSpace(stderr)
                ? (string.IsNullOrWhiteSpace(stdout) ? $"netsh exited with code {proc.ExitCode}." : stdout)
                : stderr;

            if (errorMessage.Contains("requires elevation", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage += " Run the command from an elevated (Administrator) prompt.";
            }

            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
