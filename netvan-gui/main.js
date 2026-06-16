const {
  app,
  BrowserWindow,
  ipcMain,
  nativeTheme,
  shell,
  Tray,
  Menu,
  nativeImage,
} = require('electron');
const path = require('path');
const { loadConfig, resolveHome, saveConfigPatch } = require('./src/lib/paths');
const { loadGuiSettings, saveGuiSettings } = require('./src/lib/gui-settings');
const { openStore } = require('./src/lib/traffic-store');
const { resolveRangeUtc } = require('./src/lib/date-utils');
const {
  getServiceStatus,
  installService,
  uninstallService,
  startService,
  stopService,
  restartService,
  resetData,
} = require('./src/lib/netvan-cli');

process.env.NETVAN_HOME = process.env.NETVAN_HOME || resolveHome();

const gotSingleInstanceLock = app.requestSingleInstanceLock();
if (!gotSingleInstanceLock) {
  app.quit();
}

let mainWindow = null;
let tray = null;
let config = null;
let guiSettings = loadGuiSettings();
let appIsQuitting = false;
let storeOperations = 0;
let storeBlocked = false;

const TRAY_ICON = nativeImage.createFromDataURL(
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAKUlEQVQ4T2P8z8BQz0BFwQgGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGBgYGAJh8AAD//wMA'
  + 'j2qJ8QAAAABJRU5ErkJggg=='
);

function createWindow() {
  config = loadConfig();

  mainWindow = new BrowserWindow({
    width: 1280,
    height: 820,
    minWidth: 960,
    minHeight: 640,
    backgroundColor: '#1c1c1c',
    show: false,
    autoHideMenuBar: true,
    title: 'Netvan',
    titleBarStyle: 'hidden',
    titleBarOverlay: {
      color: '#1c1c1c00',
      symbolColor: nativeTheme.shouldUseDarkColors ? '#ffffff' : '#1a1a1a',
      height: 40,
    },
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
    },
  });

  mainWindow.loadFile(path.join(__dirname, 'renderer', 'index.html'));

  mainWindow.once('ready-to-show', () => {
    mainWindow.show();
  });

  mainWindow.on('close', (event) => {
    if (!appIsQuitting && guiSettings.closeToTray) {
      event.preventDefault();
      mainWindow.hide();
    }
  });

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  if (process.argv.includes('--dev')) {
    mainWindow.webContents.openDevTools({ mode: 'detach' });
  }
}

function showMainWindow() {
  if (!mainWindow) {
    createWindow();
    return;
  }

  if (!mainWindow.isVisible()) {
    mainWindow.show();
  }
  mainWindow.focus();
}

function quitApp() {
  appIsQuitting = true;
  app.quit();
}

function applyLaunchAtStartup(enabled) {
  const loginSettings = {
    openAtLogin: enabled,
    name: 'Netvan',
  };

  if (!app.isPackaged) {
    loginSettings.path = process.execPath;
    loginSettings.args = [path.resolve(__dirname)];
  }

  app.setLoginItemSettings(loginSettings);
}

function syncTray() {
  if (!guiSettings.closeToTray) {
    if (tray) {
      tray.destroy();
      tray = null;
    }
    return;
  }

  if (tray) return;

  tray = new Tray(TRAY_ICON);
  tray.setToolTip('Netvan');
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: 'Open Netvan', click: () => showMainWindow() },
    { type: 'separator' },
    { label: 'Quit', click: () => quitApp() },
  ]));
  tray.on('double-click', () => showMainWindow());
}

function applyGuiSettings() {
  applyLaunchAtStartup(guiSettings.launchAtStartup);
  syncTray();
}

function withStore(fn) {
  if (storeBlocked) {
    return { error: 'Database temporarily unavailable', config: loadConfig() };
  }

  config = loadConfig();
  storeOperations += 1;
  let store;
  try {
    store = openStore(config.databasePath);
    return fn(store, config);
  } catch (err) {
    return { error: err.message, config };
  } finally {
    if (store) store.close();
    storeOperations -= 1;
  }
}

function waitForStoreIdle(timeoutMs = 5000) {
  const started = Date.now();
  return new Promise((resolve) => {
    const check = () => {
      if (storeOperations === 0 || Date.now() - started >= timeoutMs) {
        resolve(storeOperations === 0);
        return;
      }
      setTimeout(check, 50);
    };
    check();
  });
}

async function resetDatabaseSafely() {
  storeBlocked = true;
  await waitForStoreIdle();
  try {
    return await resetData();
  } finally {
    storeBlocked = false;
  }
}

function registerIpc() {
  ipcMain.handle('app:get-config', () => loadConfig());

  ipcMain.handle('app:set-config', (_, patch) => {
    saveConfigPatch(patch);
    return loadConfig();
  });

  ipcMain.handle('settings:get', () => loadGuiSettings());

  ipcMain.handle('settings:set', (_, patch) => {
    guiSettings = saveGuiSettings({ ...guiSettings, ...patch });
    applyGuiSettings();
    return guiSettings;
  });

  ipcMain.handle('db:get-info', () => withStore((store) => store.getDatabaseInfo()));

  ipcMain.handle('db:realtime', (_, includePrivate) => withStore((store) =>
    store.loadRealtimeSnapshot(includePrivate !== false)));

  ipcMain.handle('db:usage', (_, options) => withStore((store) => {
    const { fromRaw, toRaw, target, targetValue, includePrivate } = options;
    const { fromUtc, toUtc } = resolveRangeUtc(fromRaw, toRaw);

    if (target === 'apps') {
      return {
        kind: 'apps',
        fromUtc,
        toUtc,
        rows: store.usageByAppInRange(fromUtc, toUtc, includePrivate),
      };
    }
    if (target === 'ip') {
      return {
        kind: 'ip',
        fromUtc,
        toUtc,
        rows: store.usageByIpInRange(fromUtc, toUtc, includePrivate),
      };
    }
    if (target === 'host') {
      return {
        kind: 'host',
        fromUtc,
        toUtc,
        rows: store.usageByHostInRange(fromUtc, toUtc, includePrivate),
      };
    }
    if (target === 'app' || target === 'ip-single' || target === 'host-single') {
      const kind = target === 'app' ? 'app' : target === 'ip-single' ? 'ip' : 'host';
      return {
        kind: 'totals',
        fromUtc,
        toUtc,
        totals: store.usageTotalsInRange(fromUtc, toUtc, kind, targetValue, includePrivate),
        targetValue,
      };
    }

    return { error: `Unknown usage target '${target}'. Expected apps, host, or ip.` };
  }));

  ipcMain.handle('db:apps', (_, filter) => withStore((store) => ({
    apps: store.listAppNames(filter || null),
  })));

  ipcMain.handle('db:time-series', (_, options) => withStore((store) => {
    const { fromRaw, toRaw, includePrivate } = options;
    const { fromUtc, toUtc } = resolveRangeUtc(fromRaw, toRaw);
    return store.usageTimeSeries(fromUtc, toUtc, 1, includePrivate);
  }));

  ipcMain.handle('netvan:service-status', () => getServiceStatus());
  ipcMain.handle('netvan:service-install', () => installService());
  ipcMain.handle('netvan:service-uninstall', () => uninstallService());
  ipcMain.handle('netvan:service-start', () => startService());
  ipcMain.handle('netvan:service-stop', () => stopService());
  ipcMain.handle('netvan:service-restart', () => restartService());
  ipcMain.handle('netvan:reset', () => resetDatabaseSafely());

  ipcMain.handle('window:minimize', () => mainWindow?.minimize());
  ipcMain.handle('window:maximize', () => {
    if (mainWindow?.isMaximized()) mainWindow.unmaximize();
    else mainWindow?.maximize();
  });
  ipcMain.handle('window:close', () => mainWindow?.close());
}

if (gotSingleInstanceLock) {
  app.on('second-instance', () => {
    showMainWindow();
  });

  app.whenReady().then(() => {
    registerIpc();
    applyGuiSettings();
    createWindow();

    app.on('activate', () => {
      showMainWindow();
    });
  });
}

app.on('before-quit', () => {
  appIsQuitting = true;
});

app.on('window-all-closed', () => {
  if (guiSettings.closeToTray) return;
  if (process.platform !== 'darwin') app.quit();
});

nativeTheme.on('updated', () => {
  if (!mainWindow) return;
  mainWindow.setTitleBarOverlay({
    color: '#1c1c1c00',
    symbolColor: nativeTheme.shouldUseDarkColors ? '#ffffff' : '#1a1a1a',
    height: 40,
  });
});
