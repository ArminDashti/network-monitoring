const BUCKET_INTERVAL_SECONDS = 1;

function alignBucketEndUtc(date) {
  const ms = date.getTime();
  const epochSeconds = Math.floor(ms / 1000);
  const bucketEndSeconds = Math.ceil(epochSeconds / BUCKET_INTERVAL_SECONDS) * BUCKET_INTERVAL_SECONDS;
  return new Date(bucketEndSeconds * 1000);
}

function formatBucketUtc(date) {
  return toUtcIso(alignBucketEndUtc(date));
}

function toUtcIso(localDate) {
  return localDate.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

function startOfDayLocal(now) {
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

function startOfWeekSaturdayLocal(now) {
  const date = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  while (date.getDay() !== 6) {
    date.setDate(date.getDate() - 1);
  }
  return date;
}

function startOfMonthLocal(now) {
  return new Date(now.getFullYear(), now.getMonth(), 1);
}

function parseCompactDateTime(raw, fallback) {
  if (!raw || !String(raw).trim()) return fallback;
  let text = String(raw).trim();
  if (/^\d{6}$/.test(text)) text += 'T0000';
  const tIndex = text.indexOf('T');
  if (tIndex >= 0 && text.slice(tIndex + 1).length === 0) text += '0000';

  const match = text.match(/^(\d{2})(\d{2})(\d{2})T(\d{2})(\d{2})$/);
  if (!match) throw new Error(`Invalid datetime '${raw}'. Expected yyMMddTHHmm.`);

  const year = 2000 + Number(match[1]);
  const month = Number(match[2]) - 1;
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  return new Date(year, month, day, hour, minute, 0, 0);
}

function parseDateTimeLocal(raw, fallback) {
  if (!raw || !String(raw).trim()) return fallback;
  const text = String(raw).trim();

  const isoMatch = text.match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/);
  if (isoMatch) {
    return new Date(
      Number(isoMatch[1]),
      Number(isoMatch[2]) - 1,
      Number(isoMatch[3]),
      Number(isoMatch[4]),
      Number(isoMatch[5]),
      0,
      0,
    );
  }

  return parseCompactDateTime(raw, fallback);
}

function resolveRangeUtc(fromRaw, toRaw) {
  const now = new Date();
  let fromLocal = parseDateTimeLocal(fromRaw, startOfDayLocal(now));
  let toLocal = parseDateTimeLocal(toRaw, now);
  if (toLocal < fromLocal) [fromLocal, toLocal] = [toLocal, fromLocal];
  return {
    fromUtc: toUtcIso(fromLocal),
    toUtc: formatBucketUtc(toLocal),
  };
}

function normalizeAppName(appName) {
  const base = appName.split(/[/\\]/).pop() || appName;
  const dot = base.lastIndexOf('.');
  return dot > 0 ? base.slice(0, dot) : base;
}

module.exports = {
  BUCKET_INTERVAL_SECONDS,
  formatBucketUtc,
  toUtcIso,
  startOfDayLocal,
  startOfWeekSaturdayLocal,
  startOfMonthLocal,
  parseCompactDateTime,
  parseDateTimeLocal,
  resolveRangeUtc,
  normalizeAppName,
};
