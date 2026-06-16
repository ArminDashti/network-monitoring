const PRIVATE_IP_EXCLUDE = `
  AND (
    remote_ip NOT GLOB '10.*'
    AND remote_ip NOT GLOB '127.*'
    AND remote_ip NOT GLOB '192.168.*'
    AND remote_ip NOT GLOB '169.254.*'
    AND remote_ip NOT GLOB '172.1[6-9].*'
    AND remote_ip NOT GLOB '172.2[0-9].*'
    AND remote_ip NOT GLOB '172.3[0-1].*'
  )`;

function normalizeHostFilter(value) {
  if (!value) return '';
  return String(value).trim().replace(/\.+$/, '');
}

function hostMatchClause(prefix = 'AND ') {
  return `${prefix}(
    LOWER(host_name) = LOWER(@target)
    OR LOWER(host_name) = LOWER(@targetDot)
    OR host_name LIKE @targetSub ESCAPE '\\'
  )`;
}

function hostMatchParams(value) {
  const host = normalizeHostFilter(value);
  return {
    target: host,
    targetDot: `${host}.`,
    targetSub: `%.${host}`,
  };
}

function privateIpClause(includePrivate) {
  return includePrivate ? '' : PRIVATE_IP_EXCLUDE;
}

function targetClause(kind, value) {
  switch (kind) {
    case 'app':
      return { sql: 'AND app_name = @target', params: { target: value } };
    case 'ip':
      return { sql: 'AND remote_ip = @target', params: { target: value } };
    case 'host':
      return {
        sql: hostMatchClause(),
        params: hostMatchParams(value),
      };
    default:
      return { sql: '', params: {} };
  }
}

module.exports = {
  privateIpClause,
  targetClause,
  hostMatchClause,
  hostMatchParams,
  normalizeHostFilter,
};
