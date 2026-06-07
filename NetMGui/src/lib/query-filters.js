const PRIVATE_IP_EXCLUDE = `
  AND (
    remote_ip NOT GLOB '10.*'
    AND remote_ip NOT GLOB '127.*'
    AND remote_ip NOT GLOB '192.168.*'
    AND remote_ip NOT GLOB '169.254.*'
    AND remote_ip NOT GLOB '172.1[6-9].*'
    AND remote_ip NOT GLOB '172.2[0-9].*'
    AND remote_ip NOT GLOB '172.3[0-1].*'
    AND remote_ip NOT GLOB 'fe80:*'
    AND remote_ip NOT GLOB 'fc*:*'
    AND remote_ip NOT GLOB 'fd*:*'
    AND remote_ip NOT IN ('::1', '0:0:0:0:0:0:0:1')
  )`;

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
        sql: 'AND (host_name = @target OR host_name LIKE @targetSub)',
        params: { target: value, targetSub: `%.${value}` },
      };
    default:
      return { sql: '', params: {} };
  }
}

module.exports = { privateIpClause, targetClause };
