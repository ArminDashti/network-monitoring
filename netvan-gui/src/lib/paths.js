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

function parseTomlBool(raw, fallback = false) {
  if (raw === undefined || raw === null || raw === '') return fallback;
  const value = String(raw).trim().toLowerCase();
  if (value === 'true' || value === 'yes' || value === '1') return true;
  if (value === 'false' || value === 'no' || value === '0') return false;
  return fallback;
}

function loadConfig(home = resolveHome()) {
  const configPath = path.join(home, 'configs.toml');
  const defaults = {
    databasePath: path.join(home, 'traffic.db'),
    retentionDays: 30,
    maxSizeMb: 500,
    logLevel: 'Info',
    logFile: path.join(home, 'netvan.log'),
    disableVpnTracking: false,
    taskbarEnabled: false,
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
    retentionDays: Number(values['storage.retention_days'] || defaults.retentionDays),
    maxSizeMb: Number(values['storage.max_size_mb'] || defaults.maxSizeMb),
    logLevel: values['logging.level'] || defaults.logLevel,
    logFile: expandPath(values['logging.log_file'] || defaults.logFile, home),
    disableVpnTracking: parseTomlBool(values['monitoring.disable_vpn_tracking'], defaults.disableVpnTracking),
    taskbarEnabled: parseTomlBool(values['taskbar.enabled'], defaults.taskbarEnabled),
  };
}

function saveConfigPatch(patch, home = resolveHome()) {
  const configPath = path.join(home, 'configs.toml');
  const current = loadConfig(home);
  const merged = { ...current, ...patch };

  const lines = [];
  if (fs.existsSync(configPath)) {
    lines.push(...fs.readFileSync(configPath, 'utf8').split(/\r?\n/));
  }

  const upsertInSection = (section, key, value) => {
    const sectionHeader = `[${section}]`;
    let sectionIndex = lines.findIndex((line) => line.trim().toLowerCase() === sectionHeader.toLowerCase());
    if (sectionIndex < 0) {
      if (lines.length && lines[lines.length - 1].trim()) lines.push('');
      lines.push(sectionHeader);
      sectionIndex = lines.length - 1;
    }

    const keyPrefix = `${key} =`;
    let keyIndex = -1;
    for (let i = sectionIndex + 1; i < lines.length; i += 1) {
      const trimmed = lines[i].trim();
      if (trimmed.startsWith('[')) break;
      if (trimmed.toLowerCase().startsWith(keyPrefix.toLowerCase())) {
        keyIndex = i;
        break;
      }
    }

    const rendered = `${key} = ${value}`;
    if (keyIndex >= 0) lines[keyIndex] = rendered;
    else lines.splice(sectionIndex + 1, 0, rendered);
  };

  if (Object.prototype.hasOwnProperty.call(patch, 'disableVpnTracking')) {
    upsertInSection('monitoring', 'disable_vpn_tracking', merged.disableVpnTracking ? 'true' : 'false');
  }

  fs.mkdirSync(path.dirname(configPath), { recursive: true });
  fs.writeFileSync(configPath, `${lines.join('\n').replace(/\n*$/, '\n')}`, 'utf8');
  return loadConfig(home);
}

module.exports = { resolveHome, expandPath, loadConfig, saveConfigPatch };
