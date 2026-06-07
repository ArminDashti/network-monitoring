const fs = require('fs');
const Database = require('better-sqlite3');
const { privateIpClause, targetClause } = require('./query-filters');
const {
  formatBucketUtc,
  toUtcIso,
  startOfDayLocal,
  startOfWeekSaturdayLocal,
  startOfMonthLocal,
  normalizeAppName,
} = require('./date-utils');

class TrafficStore {
  constructor(databasePath) {
    if (!fs.existsSync(databasePath)) {
      throw new Error(`Database not found: ${databasePath}`);
    }
    this.databasePath = databasePath;
    this.db = new Database(databasePath, { readonly: true, fileMustExist: true });
  }

  close() {
    this.db.close();
  }

  usageTotalsInRange(fromUtc, toUtc, targetKind, targetValue, includePrivate) {
    const target = targetKind ? targetClause(targetKind, targetValue) : { sql: '', params: {} };
    const sql = `
      SELECT COALESCE(SUM(bytes_sent), 0) AS bytes_sent,
             COALESCE(SUM(bytes_received), 0) AS bytes_received
      FROM usage
      WHERE minute_utc >= @from AND minute_utc <= @to
      ${privateIpClause(includePrivate)}
      ${target.sql}`;
    const row = this.db.prepare(sql).get({
      from: fromUtc,
      to: toUtc,
      ...target.params,
    });
    return { bytesSent: row.bytes_sent, bytesReceived: row.bytes_received };
  }

  usageByAppInRange(fromUtc, toUtc, includePrivate) {
    const sql = `
      SELECT app_name,
             COALESCE(SUM(bytes_sent), 0) AS bytes_sent,
             COALESCE(SUM(bytes_received), 0) AS bytes_received
      FROM usage
      WHERE minute_utc >= @from AND minute_utc <= @to
      ${privateIpClause(includePrivate)}
      GROUP BY app_name
      ORDER BY bytes_sent + bytes_received DESC`;
    return this.db.prepare(sql).all({ from: fromUtc, to: toUtc });
  }

  usageByIpInRange(fromUtc, toUtc, includePrivate, limit = 100) {
    const sql = `
      SELECT remote_ip,
             COALESCE(SUM(bytes_sent), 0) AS bytes_sent,
             COALESCE(SUM(bytes_received), 0) AS bytes_received
      FROM usage
      WHERE minute_utc >= @from AND minute_utc <= @to
      ${privateIpClause(includePrivate)}
      GROUP BY remote_ip
      ORDER BY bytes_sent + bytes_received DESC
      LIMIT @limit`;
    return this.db.prepare(sql).all({ from: fromUtc, to: toUtc, limit });
  }

  usageByHostInRange(fromUtc, toUtc, includePrivate, limit = 100) {
    const sql = `
      SELECT host_name,
             COALESCE(SUM(bytes_sent), 0) AS bytes_sent,
             COALESCE(SUM(bytes_received), 0) AS bytes_received
      FROM usage
      WHERE minute_utc >= @from AND minute_utc <= @to
      ${privateIpClause(includePrivate)}
      GROUP BY host_name
      ORDER BY bytes_sent + bytes_received DESC
      LIMIT @limit`;
    return this.db.prepare(sql).all({ from: fromUtc, to: toUtc, limit });
  }

  listAppNames(filter) {
    if (filter) {
      return this.db.prepare(`
        SELECT DISTINCT app_name FROM usage
        WHERE app_name LIKE @filter
        ORDER BY app_name COLLATE NOCASE
      `).all({ filter: `%${filter}%` }).map((r) => r.app_name);
    }
    return this.db.prepare(`
      SELECT DISTINCT app_name FROM usage ORDER BY app_name COLLATE NOCASE
    `).all().map((r) => r.app_name);
  }

  getDatabaseInfo() {
    const row = this.db.prepare(`
      SELECT COUNT(*) AS row_count,
             MIN(minute_utc) AS first_minute,
             MAX(minute_utc) AS last_minute,
             COUNT(DISTINCT app_name) AS app_count
      FROM usage
    `).get();

    let fileBytes = 0;
    try {
      fileBytes = fs.statSync(this.databasePath).size;
    } catch {
      // ignore
    }

    return {
      databasePath: this.databasePath,
      rowCount: row.row_count || 0,
      firstMinuteUtc: row.first_minute,
      lastMinuteUtc: row.last_minute,
      distinctAppCount: row.app_count || 0,
      fileBytes,
    };
  }

  loadRealtimeSnapshot(includePrivate = true) {
    const nowLocal = new Date();
    const dailyStartLocal = startOfDayLocal(nowLocal);
    const weeklyStartLocal = startOfWeekSaturdayLocal(nowLocal);
    const monthlyStartLocal = startOfMonthLocal(nowLocal);

    const nowUtc = formatBucketUtc(nowLocal);
    const dailyUtc = toUtcIso(dailyStartLocal);
    const weeklyUtc = toUtcIso(weeklyStartLocal);
    const monthlyUtc = toUtcIso(monthlyStartLocal);

    const toMap = (rows) => {
      const map = new Map();
      for (const row of rows) {
        map.set(normalizeAppName(row.app_name), row);
      }
      return map;
    };

    const currentRows = toMap(this.usageByAppInRange(nowUtc, nowUtc, includePrivate));
    const dailyRows = toMap(this.usageByAppInRange(dailyUtc, nowUtc, includePrivate));
    const weeklyRows = toMap(this.usageByAppInRange(weeklyUtc, nowUtc, includePrivate));
    const monthlyRows = toMap(this.usageByAppInRange(monthlyUtc, nowUtc, includePrivate));

    const apps = [...new Set([
      ...currentRows.keys(),
      ...dailyRows.keys(),
      ...weeklyRows.keys(),
      ...monthlyRows.keys(),
    ])].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }));

    const rows = apps.map((app) => {
      const current = currentRows.get(app) || { bytes_received: 0, bytes_sent: 0 };
      const daily = dailyRows.get(app) || { bytes_received: 0, bytes_sent: 0 };
      const weekly = weeklyRows.get(app) || { bytes_received: 0, bytes_sent: 0 };
      const monthly = monthlyRows.get(app) || { bytes_received: 0, bytes_sent: 0 };
      return {
        appName: app,
        currentDownBytes: current.bytes_received,
        currentUpBytes: current.bytes_sent,
        dailyDownBytes: daily.bytes_received,
        dailyUpBytes: daily.bytes_sent,
        weeklyDownBytes: weekly.bytes_received,
        weeklyUpBytes: weekly.bytes_sent,
        monthlyDownBytes: monthly.bytes_received,
        monthlyUpBytes: monthly.bytes_sent,
      };
    });

    rows.sort((a, b) => {
      const totalA = a.currentDownBytes + a.currentUpBytes;
      const totalB = b.currentDownBytes + b.currentUpBytes;
      return totalB - totalA;
    });

    const totals = this.usageTotalsInRange(nowUtc, nowUtc, null, null, includePrivate);

    return {
      nowLocal: nowLocal.toISOString(),
      dailyStartLocal: dailyStartLocal.toISOString(),
      weeklyStartLocal: weeklyStartLocal.toISOString(),
      monthlyStartLocal: monthlyStartLocal.toISOString(),
      totalDownBytes: totals.bytesReceived,
      totalUpBytes: totals.bytesSent,
      rows,
    };
  }

  usageTimeSeries(fromUtc, toUtc, bucketMinutes, includePrivate) {
    const sql = `
      SELECT substr(minute_utc, 1, 16) AS bucket,
             COALESCE(SUM(bytes_sent), 0) AS bytes_sent,
             COALESCE(SUM(bytes_received), 0) AS bytes_received
      FROM usage
      WHERE minute_utc >= @from AND minute_utc <= @to
      ${privateIpClause(includePrivate)}
      GROUP BY bucket
      ORDER BY bucket`;
    return this.db.prepare(sql).all({ from: fromUtc, to: toUtc });
  }
}

function openStore(databasePath) {
  return new TrafficStore(databasePath);
}

module.exports = { TrafficStore, openStore };
