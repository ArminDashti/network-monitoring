using Microsoft.Data.Sqlite;

namespace NetworkMonitor.Storage;

internal sealed class TrafficStore : IDisposable
{
    private readonly SqliteConnection _connection; // Open database connection for all queries

    // Opens or creates the SQLite database and ensures tables exist
    public TrafficStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? "."); // Ensure folder exists
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString(); // Build connection string

        _connection = new SqliteConnection(cs);
        _connection.Open();
        InitSchema(); // Create tables on first use
    }

    // Creates usage and meta tables if they are missing
    private void InitSchema()
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS usage (
              minute_utc TEXT NOT NULL,
              app_name TEXT NOT NULL,
              nic_name TEXT NOT NULL,
              remote_ip TEXT NOT NULL,
              remote_port INTEGER NOT NULL,
              host_name TEXT NOT NULL,
              bytes_sent INTEGER NOT NULL DEFAULT 0,
              bytes_received INTEGER NOT NULL DEFAULT 0,
              PRIMARY KEY (minute_utc, app_name, nic_name, remote_ip, remote_port)
            );

            CREATE INDEX IF NOT EXISTS idx_usage_minute ON usage(minute_utc);
            CREATE INDEX IF NOT EXISTS idx_usage_app ON usage(app_name);
            CREATE INDEX IF NOT EXISTS idx_usage_remote_ip ON usage(remote_ip);

            CREATE TABLE IF NOT EXISTS meta (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // Adds sampled traffic deltas into the current minute bucket
    public void ApplyDeltas(IReadOnlyList<TrafficDelta> deltas)
    {
        if (deltas.Count == 0) // Nothing to write
            return;

        var now = DateTime.UtcNow;
        var minuteUtc = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:00Z"); // Bucket key
        
        using var tx = _connection.BeginTransaction(); // One transaction for the whole batch
        foreach (var d in deltas)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO usage (minute_utc, app_name, nic_name, remote_ip, remote_port, host_name, bytes_sent, bytes_received)
                VALUES ($min, $app, $nic, $rip, $rport, $host, $up, $down)
                ON CONFLICT(minute_utc, app_name, nic_name, remote_ip, remote_port) DO UPDATE SET
                  bytes_sent = bytes_sent + excluded.bytes_sent,
                  bytes_received = bytes_received + excluded.bytes_received,
                  host_name = excluded.host_name;
                """;
            cmd.Parameters.AddWithValue("$min", minuteUtc);
            cmd.Parameters.AddWithValue("$app", d.AppName);
            cmd.Parameters.AddWithValue("$nic", d.NicName);
            cmd.Parameters.AddWithValue("$rip", d.RemoteIp);
            cmd.Parameters.AddWithValue("$rport", d.RemotePort);
            cmd.Parameters.AddWithValue("$host", d.HostName);
            cmd.Parameters.AddWithValue("$up", d.DeltaSent);
            cmd.Parameters.AddWithValue("$down", d.DeltaReceived);
            cmd.ExecuteNonQuery(); // Upsert one delta row
        }

        tx.Commit(); // Persist all deltas together
    }

    // Sums upload and download bytes in a UTC time range, optionally filtered by app
    public UsageTotalsRow UsageTotalsInRangeUtc(string fromUtcInclusive, string toUtcInclusive, string? appNameOrAll)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(bytes_sent), 0), COALESCE(SUM(bytes_received), 0)
            FROM usage
            WHERE minute_utc >= $from AND minute_utc <= $to
            """ + AppFilterClause(appNameOrAll, "AND ");
        AddRangeParams(cmd, fromUtcInclusive, toUtcInclusive, appNameOrAll);
        using var r = cmd.ExecuteReader();
        r.Read();
        return new UsageTotalsRow(r.GetInt64(0), r.GetInt64(1));
    }

    // Groups traffic by application name within a UTC time range
    public IReadOnlyList<AppUsageRow> UsageByAppInRangeUtc(string fromUtcInclusive, string toUtcInclusive, string? appNameOrAll)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT app_name,
                   COALESCE(SUM(bytes_sent), 0) AS up,
                   COALESCE(SUM(bytes_received), 0) AS down
            FROM usage
            WHERE minute_utc >= $from AND minute_utc <= $to
            """ + AppFilterClause(appNameOrAll, "AND ") + """
            GROUP BY app_name
            ORDER BY (up + down) DESC;
            """;
        AddRangeParams(cmd, fromUtcInclusive, toUtcInclusive, appNameOrAll);
        return ReadAppUsageRows(cmd);
    }

    // Groups traffic by remote IP within a UTC time range
    public IReadOnlyList<IpUsageRow> UsageByIpInRangeUtc(string fromUtcInclusive, string toUtcInclusive, string? appNameOrAll)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT remote_ip,
                   COALESCE(SUM(bytes_sent), 0) AS up,
                   COALESCE(SUM(bytes_received), 0) AS down
            FROM usage
            WHERE minute_utc >= $from AND minute_utc <= $to
            """ + AppFilterClause(appNameOrAll, "AND ") + """
            GROUP BY remote_ip
            ORDER BY (up + down) DESC;
            """;
        AddRangeParams(cmd, fromUtcInclusive, toUtcInclusive, appNameOrAll);
        return ReadIpUsageRows(cmd);
    }

    // Builds optional SQL filter for a single app name
    private static string AppFilterClause(string? appNameOrAll, string prefix)
    {
        if (appNameOrAll is null || IsAppAll(appNameOrAll)) // No app filter
            return "";
        return $"{prefix}app_name = $app\n";
    }

    // Binds from, to, and optional app parameters on a command
    private static void AddRangeParams(SqliteCommand cmd, string fromUtc, string toUtc, string? appNameOrAll)
    {
        cmd.Parameters.AddWithValue("$from", fromUtc);
        cmd.Parameters.AddWithValue("$to", toUtc);
        if (appNameOrAll is not null && !IsAppAll(appNameOrAll))
            cmd.Parameters.AddWithValue("$app", appNameOrAll);
    }

    // True when the app filter means every application
    private static bool IsAppAll(string app) =>
        app.Equals("all", StringComparison.OrdinalIgnoreCase);

    // Reads grouped app usage rows from a executed command
    private static List<AppUsageRow> ReadAppUsageRows(SqliteCommand cmd)
    {
        var list = new List<AppUsageRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AppUsageRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2)));
        return list;
    }

    // Reads grouped IP usage rows from a executed command
    private static List<IpUsageRow> ReadIpUsageRows(SqliteCommand cmd)
    {
        var list = new List<IpUsageRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new IpUsageRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2)));
        return list;
    }

    // Lifetime totals grouped by remote IP, with optional exact IP filter
    public IReadOnlyList<IpReportRow> ReportByIp(string? filterIp = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = filterIp is null
            ? """
              SELECT remote_ip,
                     SUM(bytes_sent) AS up,
                     SUM(bytes_received) AS down
              FROM usage
              GROUP BY remote_ip
              ORDER BY (up + down) DESC;
              """
            : """
              SELECT remote_ip,
                     SUM(bytes_sent) AS up,
                     SUM(bytes_received) AS down
              FROM usage
              WHERE remote_ip = $f
              GROUP BY remote_ip
              ORDER BY (up + down) DESC;
              """;
        if (filterIp is not null)
            cmd.Parameters.AddWithValue("$f", filterIp);
        return ReadIpRows(cmd);
    }

    // Lifetime totals grouped by network interface name
    public IReadOnlyList<NicReportRow> ReportByNic(string? filterNic = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = filterNic is null
            ? """
              SELECT nic_name,
                     SUM(bytes_sent) AS up,
                     SUM(bytes_received) AS down
              FROM usage
              GROUP BY nic_name
              ORDER BY (up + down) DESC;
              """
            : """
              SELECT nic_name,
                     SUM(bytes_sent) AS up,
                     SUM(bytes_received) AS down
              FROM usage
              WHERE nic_name = $f
              GROUP BY nic_name
              ORDER BY (up + down) DESC;
              """;
        if (filterNic is not null)
            cmd.Parameters.AddWithValue("$f", filterNic);
        return ReadNicRows(cmd);
    }

    // Lifetime totals grouped by host name, with optional substring filter
    public IReadOnlyList<HostReportRow> ReportByHost(string? filterHost = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = filterHost is null
            ? """
              SELECT host_name,
                     SUM(bytes_sent) AS up,
                     SUM(bytes_received) AS down
              FROM usage
              GROUP BY host_name
              ORDER BY (up + down) DESC;
              """
            : """
              SELECT host_name,
                     SUM(bytes_sent) AS up,
                     SUM(bytes_received) AS down
              FROM usage
              WHERE host_name LIKE $f
              GROUP BY host_name
              ORDER BY (up + down) DESC;
              """;
        if (filterHost is not null)
            cmd.Parameters.AddWithValue("$f", $"%{filterHost}%");
        return ReadHostRows(cmd);
    }

    // Maps SQL reader rows to IP report records
    private static List<IpReportRow> ReadIpRows(SqliteCommand cmd)
    {
        var list = new List<IpReportRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new IpReportRow(
                r.GetString(0),
                r.GetInt64(1),
                r.GetInt64(2)));
        }

        return list;
    }

    // Maps SQL reader rows to NIC report records
    private static List<NicReportRow> ReadNicRows(SqliteCommand cmd)
    {
        var list = new List<NicReportRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new NicReportRow(
                r.GetString(0),
                r.GetInt64(1),
                r.GetInt64(2)));
        }

        return list;
    }

    // Maps SQL reader rows to host report records
    private static List<HostReportRow> ReadHostRows(SqliteCommand cmd)
    {
        var list = new List<HostReportRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new HostReportRow(
                r.GetString(0),
                r.GetInt64(1),
                r.GetInt64(2)));
        }

        return list;
    }

    // Closes the database connection
    public void Dispose() => _connection.Dispose();
}

internal readonly record struct TrafficDelta(
    string AppName,
    string NicName,
    string RemoteIp,
    int RemotePort,
    string HostName,
    long DeltaSent,
    long DeltaReceived);

internal readonly record struct IpReportRow(string RemoteIp, long BytesSent, long BytesReceived);

internal readonly record struct NicReportRow(string NicName, long BytesSent, long BytesReceived);

internal readonly record struct HostReportRow(string HostName, long BytesSent, long BytesReceived);

internal readonly record struct UsageTotalsRow(long BytesSent, long BytesReceived);

internal readonly record struct AppUsageRow(string AppName, long BytesSent, long BytesReceived);

internal readonly record struct IpUsageRow(string RemoteIp, long BytesSent, long BytesReceived);
