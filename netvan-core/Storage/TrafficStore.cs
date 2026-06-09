using Microsoft.Data.Sqlite;
using Netvan.Cli;

namespace Netvan.Storage;

internal sealed class TrafficStore : IDisposable
{
    internal const int BucketIntervalSeconds = 1;

    private readonly SqliteConnection _connection;

    public TrafficStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".");
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _connection = new SqliteConnection(cs);
        _connection.Open();
        EnableConcurrentReads();
        InitSchema();
    }

    private void EnableConcurrentReads()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

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

    public void ApplyDeltas(IReadOnlyList<TrafficDelta> deltas)
    {
        if (deltas.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var minuteUtc = FormatBucketUtc(now);
        
        using var tx = _connection.BeginTransaction();
        foreach (var d in deltas)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO usage (minute_utc, app_name, nic_name, remote_ip, remote_port, host_name, bytes_sent, bytes_received)
                VALUES ($min, $app, $nic, $rip, $rport, $host, $up, $down)
                ON CONFLICT(minute_utc, app_name, nic_name, remote_ip, remote_port) DO UPDATE SET
                  bytes_sent = usage.bytes_sent + excluded.bytes_sent,
                  bytes_received = usage.bytes_received + excluded.bytes_received,
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
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public UsageTotalsRow UsageTotalsInRangeUtc(
        string fromUtcInclusive,
        string toUtcInclusive,
        UsageTarget target,
        bool includePrivate)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = BuildRangeSql(
            """
            SELECT COALESCE(SUM(bytes_sent), 0), COALESCE(SUM(bytes_received), 0)
            FROM usage
            WHERE minute_utc >= $from AND minute_utc <= $to
            """,
            includePrivate,
            target,
            trailingSql: "");
        AddRangeAndTargetParams(cmd, fromUtcInclusive, toUtcInclusive, target);
        using var r = cmd.ExecuteReader();
        r.Read();
        return new UsageTotalsRow(r.GetInt64(0), r.GetInt64(1));
    }

    public IReadOnlyList<AppUsageRow> UsageByAppInRangeUtc(
        string fromUtcInclusive,
        string toUtcInclusive,
        bool includePrivate)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = BuildRangeSql(
            """
            SELECT app_name,
                   COALESCE(SUM(bytes_sent), 0) AS bytes_sent_total,
                   COALESCE(SUM(bytes_received), 0) AS bytes_recv_total
            FROM usage
            WHERE minute_utc >= $from AND minute_utc <= $to
            """,
            includePrivate,
            target: null,
            trailingSql: "GROUP BY app_name;");
        cmd.Parameters.AddWithValue("$from", fromUtcInclusive);
        cmd.Parameters.AddWithValue("$to", toUtcInclusive);
        return ReadAppUsageRows(cmd)
            .OrderByDescending(r => r.BytesSent + r.BytesReceived)
            .ToList();
    }

    public IReadOnlyList<IpUsageRow> UsageByIpInRangeUtc(
        string fromUtcInclusive,
        string toUtcInclusive,
        bool includePrivate,
        int? limit = null)
    {
        using var cmd = _connection.CreateCommand();
        var limitClause = limit is null ? "" : $" LIMIT {limit.Value}";
        cmd.CommandText = BuildRangeSql(
            """
            SELECT remote_ip,
                   COALESCE(SUM(bytes_sent), 0) AS bytes_sent_total,
                   COALESCE(SUM(bytes_received), 0) AS bytes_recv_total
            FROM usage
            WHERE minute_utc >= $from AND minute_utc <= $to
            """,
            includePrivate,
            target: null,
            trailingSql: $"GROUP BY remote_ip{limitClause};");
        cmd.Parameters.AddWithValue("$from", fromUtcInclusive);
        cmd.Parameters.AddWithValue("$to", toUtcInclusive);
        var ipRows = ReadIpUsageRows(cmd)
            .OrderByDescending(r => r.BytesSent + r.BytesReceived);
        return limit is null ? ipRows.ToList() : ipRows.Take(limit.Value).ToList();
    }

    public IReadOnlyList<HostUsageRow> UsageByHostInRangeUtc(
        string fromUtcInclusive,
        string toUtcInclusive,
        bool includePrivate,
        int? limit = null)
    {
        using var cmd = _connection.CreateCommand();
        var limitClause = limit is null ? "" : $" LIMIT {limit.Value}";
        cmd.CommandText = BuildRangeSql(
            """
            SELECT host_name,
                   COALESCE(SUM(bytes_sent), 0) AS bytes_sent_total,
                   COALESCE(SUM(bytes_received), 0) AS bytes_recv_total
            FROM usage
            WHERE minute_utc >= $from AND minute_utc <= $to
            """,
            includePrivate,
            target: null,
            trailingSql: $"GROUP BY host_name{limitClause};");
        cmd.Parameters.AddWithValue("$from", fromUtcInclusive);
        cmd.Parameters.AddWithValue("$to", toUtcInclusive);
        var hostRows = ReadHostUsageRows(cmd)
            .OrderByDescending(r => r.BytesSent + r.BytesReceived);
        return limit is null ? hostRows.ToList() : hostRows.Take(limit.Value).ToList();
    }

    public IReadOnlyList<string> ListAppNames(string? filter)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = filter is null
            ? """
              SELECT DISTINCT app_name
              FROM usage
              ORDER BY app_name COLLATE NOCASE;
              """
            : """
              SELECT DISTINCT app_name
              FROM usage
              WHERE app_name LIKE $filter
              ORDER BY app_name COLLATE NOCASE;
              """;
        if (filter is not null)
            cmd.Parameters.AddWithValue("$filter", $"%{filter}%");

        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(r.GetString(0));
        return list;
    }

    public long ClearAllUsage()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM usage;";
        return cmd.ExecuteNonQuery();
    }

    public long PruneOlderThanUtc(string cutoffUtcInclusive)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM usage WHERE minute_utc < $cutoff;";
        cmd.Parameters.AddWithValue("$cutoff", cutoffUtcInclusive);
        return cmd.ExecuteNonQuery();
    }

    public long PruneOldestFraction(double fraction)
    {
        if (fraction <= 0 || fraction >= 1)
            return 0;

        using var countCmd = _connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM usage;";
        var total = Convert.ToInt64(countCmd.ExecuteScalar() ?? 0L);
        if (total == 0)
            return 0;

        var toDelete = (long)Math.Ceiling(total * fraction);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM usage
            WHERE rowid IN (
              SELECT rowid FROM usage
              ORDER BY minute_utc ASC
              LIMIT $limit
            );
            """;
        cmd.Parameters.AddWithValue("$limit", toDelete);
        return cmd.ExecuteNonQuery();
    }

    public void Vacuum()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "VACUUM;";
        cmd.ExecuteNonQuery();
    }

    public DatabaseInfoRow GetDatabaseInfo(string databasePath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*),
                   MIN(minute_utc),
                   MAX(minute_utc),
                   COUNT(DISTINCT app_name)
            FROM usage;
            """;
        using var r = cmd.ExecuteReader();
        r.Read();
        var rowCount = r.IsDBNull(0) ? 0L : r.GetInt64(0);
        var firstMinute = r.IsDBNull(1) ? null : r.GetString(1);
        var lastMinute = r.IsDBNull(2) ? null : r.GetString(2);
        var appCount = r.IsDBNull(3) ? 0L : r.GetInt64(3);

        long fileBytes = 0;
        try
        {
            if (File.Exists(databasePath))
                fileBytes = new FileInfo(databasePath).Length;
        }
        catch
        {
            // Ignore file stat errors.
        }

        return new DatabaseInfoRow(databasePath, rowCount, firstMinute, lastMinute, appCount, fileBytes);
    }

    /// <summary>
    /// End-aligned bucket label for the 1-second interval containing <paramref name="utcNow"/>.
    /// </summary>
    internal static string FormatBucketUtc(DateTime utcNow) =>
        AlignBucketEndUtc(utcNow, BucketIntervalSeconds).ToString("yyyy-MM-ddTHH:mm:ssZ");

    internal static DateTime AlignBucketEndUtc(DateTime utcNow, int bucketIntervalSeconds)
    {
        var utc = utcNow.Kind == DateTimeKind.Utc
            ? utcNow
            : utcNow.ToUniversalTime();

        var epochSeconds = (long)(utc - DateTime.UnixEpoch).TotalSeconds;
        var bucketEndSeconds = ((epochSeconds + bucketIntervalSeconds - 1) / bucketIntervalSeconds) * bucketIntervalSeconds;
        return DateTime.UnixEpoch.AddSeconds(bucketEndSeconds);
    }

    private static string BuildRangeSql(
        string selectAndWhere,
        bool includePrivate,
        UsageTarget? target,
        string trailingSql)
    {
        var sql = new System.Text.StringBuilder(selectAndWhere);
        if (!includePrivate)
        {
            sql.AppendLine();
            sql.Append(QueryFilters.PrivateIpExcludeClause(includePrivate: false, prefix: "AND "));
        }

        if (target is UsageTarget t)
            sql.Append(QueryFilters.TargetClause(t.Kind, t.Value));

        if (!string.IsNullOrWhiteSpace(trailingSql))
        {
            sql.AppendLine();
            sql.Append(trailingSql);
        }

        return sql.ToString();
    }

    private static void AddRangeAndTargetParams(SqliteCommand cmd, string fromUtc, string toUtc, UsageTarget target)
    {
        cmd.Parameters.AddWithValue("$from", fromUtc);
        cmd.Parameters.AddWithValue("$to", toUtc);
        QueryFilters.AddTargetParameters(cmd, target.Kind, target.Value);
    }

    private static List<HostUsageRow> ReadHostUsageRows(SqliteCommand cmd)
    {
        var list = new List<HostUsageRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new HostUsageRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2)));
        return list;
    }

    private static List<AppUsageRow> ReadAppUsageRows(SqliteCommand cmd)
    {
        var list = new List<AppUsageRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AppUsageRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2)));
        return list;
    }

    private static List<IpUsageRow> ReadIpUsageRows(SqliteCommand cmd)
    {
        var list = new List<IpUsageRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new IpUsageRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2)));
        return list;
    }

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
              ORDER BY bytes_sent + bytes_received DESC;
              """
            : """
              SELECT remote_ip,
                     SUM(bytes_sent) AS up,
                     SUM(bytes_received) AS down
              FROM usage
              WHERE remote_ip = $f
              GROUP BY remote_ip
              ORDER BY bytes_sent + bytes_received DESC;
              """;
        if (filterIp is not null)
            cmd.Parameters.AddWithValue("$f", filterIp);
        return ReadIpRows(cmd);
    }

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
              ORDER BY bytes_sent + bytes_received DESC;
              """
            : """
              SELECT nic_name,
                     SUM(bytes_sent) AS up,
                     SUM(bytes_received) AS down
              FROM usage
              WHERE nic_name = $f
              GROUP BY nic_name
              ORDER BY bytes_sent + bytes_received DESC;
              """;
        if (filterNic is not null)
            cmd.Parameters.AddWithValue("$f", filterNic);
        return ReadNicRows(cmd);
    }

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
              ORDER BY bytes_sent + bytes_received DESC;
              """
            : """
              SELECT host_name,
                     SUM(bytes_sent) AS up,
                     SUM(bytes_received) AS down
              FROM usage
              WHERE host_name LIKE $f
              GROUP BY host_name
              ORDER BY bytes_sent + bytes_received DESC;
              """;
        if (filterHost is not null)
            cmd.Parameters.AddWithValue("$f", $"%{filterHost}%");
        return ReadHostRows(cmd);
    }

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

internal readonly record struct HostUsageRow(string HostName, long BytesSent, long BytesReceived);

internal readonly record struct DatabaseInfoRow(
    string DatabasePath,
    long RowCount,
    string? FirstMinuteUtc,
    string? LastMinuteUtc,
    long DistinctAppCount,
    long FileBytes);
