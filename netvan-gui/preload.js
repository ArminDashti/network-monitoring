const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('netvanApi', {
  getConfig: () => ipcRenderer.invoke('app:get-config'),
  setConfig: (patch) => ipcRenderer.invoke('app:set-config', patch),
  getSettings: () => ipcRenderer.invoke('settings:get'),
  setSettings: (patch) => ipcRenderer.invoke('settings:set', patch),
  getDatabaseInfo: () => ipcRenderer.invoke('db:get-info'),
  getRealtime: (includePrivate) => ipcRenderer.invoke('db:realtime', includePrivate),
  getUsage: (options) => ipcRenderer.invoke('db:usage', options),
  listApps: (filter) => ipcRenderer.invoke('db:apps', filter),
  getTimeSeries: (options) => ipcRenderer.invoke('db:time-series', options),
  getServiceStatus: () => ipcRenderer.invoke('netvan:service-status'),
  installService: () => ipcRenderer.invoke('netvan:service-install'),
  uninstallService: () => ipcRenderer.invoke('netvan:service-uninstall'),
  startService: () => ipcRenderer.invoke('netvan:service-start'),
  stopService: () => ipcRenderer.invoke('netvan:service-stop'),
  restartService: () => ipcRenderer.invoke('netvan:service-restart'),
  resetData: () => ipcRenderer.invoke('netvan:reset'),
  minimize: () => ipcRenderer.invoke('window:minimize'),
  maximize: () => ipcRenderer.invoke('window:maximize'),
  close: () => ipcRenderer.invoke('window:close'),
});
