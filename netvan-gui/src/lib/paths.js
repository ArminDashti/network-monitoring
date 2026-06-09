const fs = require('fs');
const os = require('os');
const path = require('path');

function legacyHome() {
  return path.join(
    process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'),
    'Netvan',
  );
}

/**
 * Mirrors netvan-core Storage.NetvanPaths.Home so GUI and CLI use the same data directory.
 */
function resolveHome() {
  const fromEnv = process.env.NETVAN_HOME;
  if (fromEnv && fromEnv.trim()) {
    return path.resolve(fromEnv.trim());
  }

  const execDir = path.dirname(process.execPath);
  const execBase = path.basename(execDir).toLowerCase();

  // Packaged GUI lives in <install>/gui/Netvan.exe; data files are in <install>/.
  if (execBase === 'gui') {
    return path.resolve(path.dirname(execDir));
  }

  // netvan.exe and configs.toml live in the install directory.
  if (fs.existsSync(path.join(execDir, 'netvan.exe'))) {
    return execDir;
  }

  const guiRoot = path.resolve(__dirname, '..', '..');
  const devInstallCandidates = [
    path.join(guiRoot, '..', 'netvan'),
    path.join(guiRoot, '..', 'Netvan'),
    path.join(process.cwd(), '..', 'netvan'),
    path.join(process.cwd(), '..', 'Netvan'),
  ];
  for (const candidate of devInstallCandidates) {
    const resolved = path.resolve(candidate);
    if (fs.existsSync(path.join(resolved, 'netvan.exe'))) {
      return resolved;
    }
  }

  return legacyHome();
}

function expandPath(value, home) {
  return value
    .replace(/%NETVAN_HOME%/gi, home)
    .replace(/\$NETVAN_HOME/gi, home);
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
    logFile: path.join(home, 'netvan.log'),
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
