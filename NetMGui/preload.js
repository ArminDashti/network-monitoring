const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('netmApi', {
  getConfig: () => ipcRenderer.invoke('app:get-config'),
  getDatabaseInfo: () => ipcRenderer.invoke('db:get-info'),
  getRealtime: (includePrivate) => ipcRenderer.invoke('db:realtime', includePrivate),
  getUsage: (options) => ipcRenderer.invoke('db:usage', options),
  listApps: (filter) => ipcRenderer.invoke('db:apps', filter),
  getTimeSeries: (options) => ipcRenderer.invoke('db:time-series', options),
  getServiceStatus: () => ipcRenderer.invoke('netm:service-status'),
  getCollectorStatus: () => ipcRenderer.invoke('netm:collector-status'),
  startService: () => ipcRenderer.invoke('netm:service-start'),
  stopService: () => ipcRenderer.invoke('netm:service-stop'),
  minimize: () => ipcRenderer.invoke('window:minimize'),
  maximize: () => ipcRenderer.invoke('window:maximize'),
  close: () => ipcRenderer.invoke('window:close'),
});
