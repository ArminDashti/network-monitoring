const { app, BrowserWindow, ipcMain, nativeTheme, shell } = require('electron');
const path = require('path');
const { loadConfig } = require('./src/lib/paths');
const { openStore } = require('./src/lib/traffic-store');
const { resolveRangeUtc } = require('./src/lib/date-utils');
const {
  getServiceStatus,
  getCollectorStatus,
  startService,
  stopService,
} = require('./src/lib/netm-cli');

let mainWindow = null;
let config = null;

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
    title: 'NetM',
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

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  if (process.argv.includes('--dev')) {
    mainWindow.webContents.openDevTools({ mode: 'detach' });
  }
}

function withStore(fn) {
  config = loadConfig();
  let store;
  try {
    store = openStore(config.databasePath);
    return fn(store, config);
  } catch (err) {
    return { error: err.message, config };
  } finally {
    if (store) store.close();
  }
}

function registerIpc() {
  ipcMain.handle('app:get-config', () => loadConfig());

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

    return {
      kind: 'totals',
      fromUtc,
      toUtc,
      totals: store.usageTotalsInRange(fromUtc, toUtc, null, null, includePrivate),
    };
  }));

  ipcMain.handle('db:apps', (_, filter) => withStore((store) => ({
    apps: store.listAppNames(filter || null),
  })));

  ipcMain.handle('db:time-series', (_, options) => withStore((store) => {
    const { fromRaw, toRaw, includePrivate } = options;
    const { fromUtc, toUtc } = resolveRangeUtc(fromRaw, toRaw);
    return store.usageTimeSeries(fromUtc, toUtc, 1, includePrivate);
  }));

  ipcMain.handle('netm:service-status', () => getServiceStatus());
  ipcMain.handle('netm:collector-status', () => getCollectorStatus());
  ipcMain.handle('netm:service-start', () => startService());
  ipcMain.handle('netm:service-stop', () => stopService());

  ipcMain.handle('window:minimize', () => mainWindow?.minimize());
  ipcMain.handle('window:maximize', () => {
    if (mainWindow?.isMaximized()) mainWindow.unmaximize();
    else mainWindow?.maximize();
  });
  ipcMain.handle('window:close', () => mainWindow?.close());
}

app.whenReady().then(() => {
  registerIpc();
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
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
