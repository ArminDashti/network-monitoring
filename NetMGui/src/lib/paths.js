const fs = require('fs');
const os = require('os');
const path = require('path');

function resolveHome() {
  return process.env.NETM_HOME
    || path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'), 'NetM');
}

function expandPath(value, home) {
  return value
    .replace(/%NETM_HOME%/gi, home)
    .replace(/\$NETM_HOME/gi, home);
}

function parseTomlValue(raw) {
  const trimmed = raw.trim();
  if ((trimmed.startsWith('"') && trimmed.endsWith('"'))
    || (trimmed.startsWith("'") && trimmed.endsWith("'"))) {
    return trimmed.slice(1, -1);
  }
  return trimmed;
}

function loadConfig(home = resolveHome()) {
  const configPath = path.join(home, 'configs.toml');
  const defaults = {
    databasePath: path.join(home, 'traffic.db'),
    samplingInterval: 1,
    retentionDays: 30,
    maxSizeMb: 500,
    logLevel: 'Info',
    logFile: path.join(home, 'netm.log'),
  };

  if (!fs.existsSync(configPath)) {
    return { home, configPath, ...defaults };
  }

  const text = fs.readFileSync(configPath, 'utf8');
  const values = {};
  let section = '';

  for (const line of text.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) continue;

    const sectionMatch = trimmed.match(/^\[(.+)\]$/);
    if (sectionMatch) {
      section = sectionMatch[1].toLowerCase();
      continue;
    }

    const eq = trimmed.indexOf('=');
    if (eq <= 0) continue;

    const key = trimmed.slice(0, eq).trim().toLowerCase();
    const value = parseTomlValue(trimmed.slice(eq + 1));
    values[`${section}.${key}`] = value;
    if (!section) values[key] = value;
  }

  const dbRaw = values.database_path || values['.database_path'];
  const databasePath = dbRaw
    ? path.resolve(expandPath(dbRaw, home))
    : defaults.databasePath;

  return {
    home,
    configPath,
    databasePath,
    samplingInterval: Number(values['monitoring.sampling_interval'] || defaults.samplingInterval),
    retentionDays: Number(values['storage.retention_days'] || defaults.retentionDays),
    maxSizeMb: Number(values['storage.max_size_mb'] || defaults.maxSizeMb),
    logLevel: values['logging.level'] || defaults.logLevel,
    logFile: expandPath(values['logging.log_file'] || defaults.logFile, home),
  };
}

module.exports = { resolveHome, expandPath, loadConfig };
