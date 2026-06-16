const fs = require('fs');
const path = require('path');
const { resolveHome } = require('./paths');

const DEFAULTS = {
  launchAtStartup: false,
  closeToTray: true,
};

function getSettingsPath() {
  return path.join(resolveHome(), 'gui-settings.json');
}

function loadGuiSettings() {
  const filePath = getSettingsPath();
  if (!fs.existsSync(filePath)) {
    return { ...DEFAULTS };
  }

  try {
    const parsed = JSON.parse(fs.readFileSync(filePath, 'utf8'));
    return {
      launchAtStartup: Boolean(parsed.launchAtStartup),
      closeToTray: parsed.closeToTray !== false,
    };
  } catch {
    return { ...DEFAULTS };
  }
}

function saveGuiSettings(settings) {
  const filePath = getSettingsPath();
  const merged = {
    launchAtStartup: Boolean(settings.launchAtStartup),
    closeToTray: settings.closeToTray !== false,
  };

  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, `${JSON.stringify(merged, null, 2)}\n`, 'utf8');
  return merged;
}

module.exports = { loadGuiSettings, saveGuiSettings, DEFAULTS, getSettingsPath };
