const K = 1024;
const LIVE_HISTORY_MAX = 90;
const LIVE_APP_CHART_LIMIT = 8;
const LIVE_APP_HISTORY_MAX = 60;

const APP_CHART_PALETTE = [
  '#60cdff',
  '#6ccb5f',
  '#ff9f43',
  '#a78bfa',
  '#f472b6',
  '#34d399',
  '#fbbf24',
  '#38bdf8',
];

const CHART_COLORS = {
  download: '#6ccb5f',
  downloadDim: 'rgba(108, 203, 95, 0.18)',
  upload: '#ff6b6b',
  uploadDim: 'rgba(255, 107, 107, 0.18)',
  grid: 'rgba(255, 255, 255, 0.06)',
  axis: 'rgba(255, 255, 255, 0.38)',
  label: 'rgba(255, 255, 255, 0.55)',
};

function formatBytes(value) {
  let n = Math.max(0, Number(value) || 0);
  if (n < K) return `${n} B`;
  if (n < K * K) return `${(n / K).toFixed(2)} KB`;
  if (n < K * K * K) return `${(n / (K * K)).toFixed(2)} MB`;
  if (n < K * K * K * K) return `${(n / (K * K * K)).toFixed(2)} GB`;
  return `${(n / (K * K * K * K)).toFixed(2)} TB`;
}

function formatSpeed(bytesPerSecond) {
  const mbps = (Math.max(0, bytesPerSecond) * 8) / (K * K);
  if (mbps >= 1000) return `${(mbps / 1000).toFixed(1)} Gb/s`;
  if (mbps >= 1) return `${mbps.toFixed(1)} Mb/s`;
  const kbps = (Math.max(0, bytesPerSecond) * 8) / K;
  return `${kbps.toFixed(0)} Kb/s`;
}

function formatChartSpeed(bytesPerSecond) {
  const mbps = (Math.max(0, bytesPerSecond) * 8) / (K * K);
  if (mbps >= 1000) return `${(mbps / 1000).toFixed(1)} Gb/s`;
  if (mbps >= 1) return `${mbps.toFixed(1)} Mb/s`;
  const kbps = (Math.max(0, bytesPerSecond) * 8) / K;
  if (kbps >= 100) return `${kbps.toFixed(0)} Kb/s`;
  return `${kbps.toFixed(1)} Kb/s`;
}

const PERIOD_MB_PRECISION_THRESHOLD = 100;

function formatPeriodBytes(bytes) {
  const n = Math.max(0, Number(bytes) || 0);
  const mb = n / (K * K);
  if (mb < K) {
    return mb < PERIOD_MB_PRECISION_THRESHOLD
      ? `${mb.toFixed(2)} MB`
      : `${mb.toFixed(1)} MB`;
  }
  const gb = mb / K;
  return gb < PERIOD_MB_PRECISION_THRESHOLD
    ? `${gb.toFixed(2)} GB`
    : `${gb.toFixed(1)} GB`;
}

function formatLocal(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleString();
}

function formatDateLocal(date) {
  const pad = (n) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function formatTimeLocal(date) {
  const pad = (n) => String(n).padStart(2, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function setUsageDateTime(prefix, date) {
  document.getElementById(`${prefix}-date`).value = formatDateLocal(date);
  document.getElementById(`${prefix}-time`).value = formatTimeLocal(date);
}

function readUsageDateTime(prefix) {
  const date = document.getElementById(`${prefix}-date`).value.trim();
  const time = document.getElementById(`${prefix}-time`).value.trim();
  if (!date || !time) return null;
  return `${date}T${time}`;
}

function startOfDayLocal(now) {
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

function showToast(message, isError = false) {
  const container = document.getElementById('toast-container');
  const toast = document.createElement('div');
  toast.className = `toast${isError ? ' error' : ''}`;
  toast.textContent = message;
  container.appendChild(toast);
  setTimeout(() => toast.remove(), 4000);
}

function stripAnsi(text) {
  return text.replace(/\x1b\[[0-9;]*m/g, '');
}

let config = null;
let liveTimer = null;
let liveRefreshGeneration = 0;
let currentView = 'live';
let liveRows = [];
let liveHistory = [];
let liveAppHistory = new Map();
let liveChartsBound = false;

const liveCharts = {
  main: { canvas: null, ctx: null, empty: null, updated: null },
  app: { canvas: null, ctx: null, empty: null },
};

async function init() {
  bindWindowControls();
  bindNavigation();
  bindUsageQuery();
  bindServiceControls();
  bindAppsFilter();
  bindSettingsControls();
  bindLiveCharts();
  initUsageDateDefaults();

  try {
    config = await window.netvanApi.getConfig();
  } catch {
    config = {};
  }

  await loadSettings();
  await refreshLive();
  startLivePolling();
  await loadApps('');
  await loadAbout();
}

function bindWindowControls() {
  document.getElementById('btn-minimize').addEventListener('click', () => window.netvanApi.minimize());
  document.getElementById('btn-maximize').addEventListener('click', () => window.netvanApi.maximize());
  document.getElementById('btn-close').addEventListener('click', () => window.netvanApi.close());
}

function bindNavigation() {
  document.querySelectorAll('.nav-item').forEach((btn) => {
    btn.addEventListener('click', async () => {
      const view = btn.dataset.view;
      if (view === currentView) return;

      document.querySelectorAll('.nav-item').forEach((b) => b.classList.remove('active'));
      btn.classList.add('active');

      document.querySelectorAll('.view').forEach((v) => v.classList.remove('view-active'));
      document.getElementById(`view-${view}`).classList.add('view-active');
      currentView = view;

      if (view === 'settings') {
        await loadSettings();
        await refreshServiceStatus();
      }
      if (view === 'about') await loadAbout();
      if (view === 'apps') await loadApps(document.getElementById('apps-filter').value);
      if (view === 'live') {
        resizeLiveCharts();
        renderLiveCharts();
      }
    });
  });
}

function initUsageDateDefaults() {
  const now = new Date();
  const fromDate = startOfDayLocal(now);
  if (!document.getElementById('usage-from-date').value) setUsageDateTime('usage-from', fromDate);
  if (!document.getElementById('usage-to-date').value) setUsageDateTime('usage-to', now);
}

function startLivePolling() {
  const intervalMs = 1000;
  if (liveTimer) clearInterval(liveTimer);
  liveTimer = setInterval(refreshLive, intervalMs);
}

function bindLiveCharts() {
  if (liveChartsBound) return;
  liveChartsBound = true;

  liveCharts.main.canvas = document.getElementById('live-main-chart');
  liveCharts.main.ctx = liveCharts.main.canvas.getContext('2d');
  liveCharts.main.empty = document.getElementById('live-main-empty');
  liveCharts.main.updated = document.getElementById('live-chart-updated');

  liveCharts.app.canvas = document.getElementById('live-app-chart');
  liveCharts.app.ctx = liveCharts.app.canvas.getContext('2d');
  liveCharts.app.empty = document.getElementById('live-app-empty');

  let resizeTimer;
  window.addEventListener('resize', () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      if (currentView === 'live') {
        resizeLiveCharts();
        renderLiveCharts();
      }
    }, 120);
  });
}

function resizeLiveCharts() {
  [liveCharts.main, liveCharts.app].forEach((chart) => {
    if (!chart.canvas) return;
    const panel = chart.canvas.parentElement;
    const width = Math.max(280, panel.clientWidth);
    const height = Math.max(140, panel.clientHeight);
    const dpr = window.devicePixelRatio || 1;
    chart.canvas.width = Math.floor(width * dpr);
    chart.canvas.height = Math.floor(height * dpr);
    chart.canvas.style.width = `${width}px`;
    chart.canvas.style.height = `${height}px`;
    chart.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    chart.width = width;
    chart.height = height;
  });
}

function pushLiveAppHistory(rows) {
  const activeRows = rows
    .filter((row) => row.currentDownBytes > 0 || row.currentUpBytes > 0)
    .sort((a, b) => (b.currentDownBytes + b.currentUpBytes) - (a.currentDownBytes + a.currentUpBytes))
    .slice(0, LIVE_APP_CHART_LIMIT);

  const activeNames = new Set(activeRows.map((row) => row.appName));
  for (const row of activeRows) {
    const total = row.currentDownBytes + row.currentUpBytes;
    const history = liveAppHistory.get(row.appName) || [];
    history.push({ total, down: row.currentDownBytes, up: row.currentUpBytes });
    if (history.length > LIVE_APP_HISTORY_MAX) history.shift();
    liveAppHistory.set(row.appName, history);
  }

  for (const name of [...liveAppHistory.keys()]) {
    if (!activeNames.has(name)) {
      const history = liveAppHistory.get(name);
      history.push({ total: 0, down: 0, up: 0 });
      if (history.length > LIVE_APP_HISTORY_MAX) history.shift();
      if (history.every((point) => point.total === 0)) liveAppHistory.delete(name);
    }
  }
}

function getLiveAppSeries() {
  return [...liveAppHistory.entries()]
    .map(([appName, history]) => ({
      appName,
      history,
      latest: history[history.length - 1]?.total || 0,
    }))
    .filter((series) => series.latest > 0 || series.history.some((point) => point.total > 0))
    .sort((a, b) => b.latest - a.latest)
    .slice(0, LIVE_APP_CHART_LIMIT);
}

function pushLiveHistory(downBytes, upBytes) {
  liveHistory.push({
    down: Math.max(0, downBytes),
    up: Math.max(0, upBytes),
    at: Date.now(),
  });
  if (liveHistory.length > LIVE_HISTORY_MAX) {
    liveHistory = liveHistory.slice(-LIVE_HISTORY_MAX);
  }
}

function renderLiveCharts() {
  renderMainThroughputChart();
  renderAppBreakdownChart();
}

function renderMainThroughputChart() {
  const { ctx, width, height, empty, updated, canvas } = liveCharts.main;
  if (!ctx || !width) return;

  ctx.clearRect(0, 0, width, height);

  const hasData = liveHistory.some((p) => p.down > 0 || p.up > 0);
  empty.hidden = hasData;
  canvas.hidden = !hasData;
  if (!hasData) {
    if (updated) updated.textContent = '—';
    return;
  }

  const padding = { top: 14, right: 16, bottom: 28, left: 52 };
  const plotW = width - padding.left - padding.right;
  const plotH = height - padding.top - padding.bottom;
  const maxVal = Math.max(
    1,
    ...liveHistory.map((p) => Math.max(p.down, p.up)),
  );

  for (let i = 0; i <= 4; i += 1) {
    const y = padding.top + (plotH * i) / 4;
    ctx.strokeStyle = CHART_COLORS.grid;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(padding.left, y);
    ctx.lineTo(width - padding.right, y);
    ctx.stroke();

    const value = maxVal * (1 - i / 4);
    ctx.fillStyle = CHART_COLORS.label;
    ctx.font = '11px Segoe UI, system-ui, sans-serif';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    ctx.fillText(formatChartSpeed(value), padding.left - 8, y);
  }

  const points = liveHistory.length;
  const xAt = (index) => padding.left + (plotW * index) / Math.max(1, points - 1);

  function drawSeries(key, color, fillColor) {
    ctx.beginPath();
    liveHistory.forEach((point, index) => {
      const x = xAt(index);
      const y = padding.top + plotH - (point[key] / maxVal) * plotH;
      if (index === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    });
    ctx.lineTo(xAt(points - 1), padding.top + plotH);
    ctx.lineTo(xAt(0), padding.top + plotH);
    ctx.closePath();
    ctx.fillStyle = fillColor;
    ctx.fill();

    ctx.beginPath();
    liveHistory.forEach((point, index) => {
      const x = xAt(index);
      const y = padding.top + plotH - (point[key] / maxVal) * plotH;
      if (index === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    });
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.lineJoin = 'round';
    ctx.stroke();
  }

  drawSeries('down', CHART_COLORS.download, CHART_COLORS.downloadDim);
  drawSeries('up', CHART_COLORS.upload, CHART_COLORS.uploadDim);

  ctx.fillStyle = CHART_COLORS.axis;
  ctx.font = '11px Segoe UI, system-ui, sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  ctx.fillText('now', width - padding.right, height - padding.bottom + 8);

  if (updated) {
    updated.textContent = `Updated ${new Date().toLocaleTimeString()}`;
  }
}

function renderAppBreakdownChart() {
  const { ctx, width, height, empty, canvas } = liveCharts.app;
  if (!ctx || !width) return;

  ctx.clearRect(0, 0, width, height);

  const series = getLiveAppSeries();
  const pointCount = Math.max(...series.map((s) => s.history.length), 0);
  const hasData = series.length > 0 && pointCount > 1;
  empty.hidden = hasData;
  canvas.hidden = !hasData;
  if (!hasData) return;

  const legendHeight = 34;
  const padding = { top: 12, right: 16, bottom: 28 + legendHeight, left: 52 };
  const plotW = width - padding.left - padding.right;
  const plotH = height - padding.top - padding.bottom;

  const totals = [];
  for (let i = 0; i < pointCount; i += 1) {
    let sum = 0;
    for (const item of series) {
      sum += item.history[i]?.total || 0;
    }
    totals.push(sum);
  }
  const maxVal = Math.max(1, ...totals);

  for (let i = 0; i <= 4; i += 1) {
    const y = padding.top + (plotH * i) / 4;
    ctx.strokeStyle = CHART_COLORS.grid;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(padding.left, y);
    ctx.lineTo(width - padding.right, y);
    ctx.stroke();

    const value = maxVal * (1 - i / 4);
    ctx.fillStyle = CHART_COLORS.label;
    ctx.font = '11px Segoe UI, system-ui, sans-serif';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    ctx.fillText(formatChartSpeed(value), padding.left - 8, y);
  }

  const xAt = (index) => padding.left + (plotW * index) / Math.max(1, pointCount - 1);

  for (let s = series.length - 1; s >= 0; s -= 1) {
    const item = series[s];
    const color = APP_CHART_PALETTE[s % APP_CHART_PALETTE.length];
    const fillColor = color.replace(')', ', 0.28)').replace('rgb', 'rgba').replace('#', '');
    const rgbaFill = color.startsWith('#')
      ? `${color}47`
      : fillColor;

    const upper = new Array(pointCount).fill(0);
    for (let i = 0; i < pointCount; i += 1) {
      let stack = 0;
      for (let j = 0; j <= s; j += 1) {
        stack += series[j].history[i]?.total || 0;
      }
      upper[i] = stack;
    }

    const lower = new Array(pointCount).fill(0);
    for (let i = 0; i < pointCount; i += 1) {
      let stack = 0;
      for (let j = 0; j < s; j += 1) {
        stack += series[j].history[i]?.total || 0;
      }
      lower[i] = stack;
    }

    ctx.beginPath();
    for (let i = 0; i < pointCount; i += 1) {
      const x = xAt(i);
      const y = padding.top + plotH - (upper[i] / maxVal) * plotH;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    for (let i = pointCount - 1; i >= 0; i -= 1) {
      const x = xAt(i);
      const y = padding.top + plotH - (lower[i] / maxVal) * plotH;
      ctx.lineTo(x, y);
    }
    ctx.closePath();
    ctx.fillStyle = rgbaFill;
    ctx.fill();

    ctx.beginPath();
    for (let i = 0; i < pointCount; i += 1) {
      const x = xAt(i);
      const y = padding.top + plotH - (upper[i] / maxVal) * plotH;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.strokeStyle = color;
    ctx.lineWidth = 1.5;
    ctx.stroke();
  }

  ctx.fillStyle = CHART_COLORS.axis;
  ctx.font = '11px Segoe UI, system-ui, sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  ctx.fillText('now', width - padding.right, height - padding.bottom + 8);

  const legendY = height - legendHeight + 6;
  let legendX = padding.left;
  ctx.textAlign = 'left';
  ctx.textBaseline = 'middle';
  ctx.font = '11px Segoe UI, system-ui, sans-serif';

  series.forEach((item, index) => {
    const color = APP_CHART_PALETTE[index % APP_CHART_PALETTE.length];
    const label = item.appName.length > 16 ? `${item.appName.slice(0, 15)}…` : item.appName;
    const speed = formatSpeed(item.latest);
    const text = `${label} · ${speed}`;
    const textWidth = ctx.measureText(text).width + 22;
    if (legendX + textWidth > width - padding.right) return;

    ctx.fillStyle = color;
    ctx.fillRect(legendX, legendY + 5, 10, 10);
    ctx.fillStyle = CHART_COLORS.label;
    ctx.fillText(text, legendX + 14, legendY + 10);
    legendX += textWidth + 12;
  });
}

async function refreshLive() {
  const generation = ++liveRefreshGeneration;

  try {
    const data = await window.netvanApi.getRealtime(true);
    if (generation !== liveRefreshGeneration) return;

    if (data.error) {
      document.getElementById('live-download').textContent = '—';
      document.getElementById('live-upload').textContent = '—';
      liveRows = [];
      liveHistory = [];
      liveAppHistory = new Map();
      resizeLiveCharts();
      renderLiveCharts();
      return;
    }

    document.getElementById('live-download').textContent = formatSpeed(data.totalDownBytes);
    document.getElementById('live-upload').textContent = formatSpeed(data.totalUpBytes);

    liveRows = data.rows || [];
    pushLiveHistory(data.totalDownBytes, data.totalUpBytes);
    pushLiveAppHistory(liveRows);

    if (currentView === 'live') {
      resizeLiveCharts();
      renderLiveCharts();
    }
  } catch {
    if (generation !== liveRefreshGeneration) return;
    document.getElementById('live-download').textContent = '—';
    document.getElementById('live-upload').textContent = '—';
  }
}

function bindUsageQuery() {
  document.getElementById('usage-query').addEventListener('click', runUsageQuery);
}

async function runUsageQuery() {
  const fromRaw = readUsageDateTime('usage-from');
  const toRaw = readUsageDateTime('usage-to');
  const target = document.getElementById('usage-target').value;
  const includePrivate = document.getElementById('usage-private').checked;
  const resultsEl = document.getElementById('usage-results');

  resultsEl.innerHTML = '<p class="empty-state">Querying…</p>';

  try {
    const data = await window.netvanApi.getUsage({ fromRaw, toRaw, target, includePrivate });

    if (data.error) {
      resultsEl.innerHTML = `<div class="error-banner">${escapeHtml(data.error)}</div>`;
      return;
    }

    const rangeNote = `<p class="empty-state" style="padding-top:0">Range: ${escapeHtml(formatLocal(data.fromUtc))} → ${escapeHtml(formatLocal(data.toUtc))}</p>`;

    const columns = {
      apps: ['Application', 'app_name'],
      ip: ['IP', 'remote_ip'],
      host: ['Hostname', 'host_name'],
    };
    const [label, key] = columns[data.kind];

    if (!data.rows.length) {
      resultsEl.innerHTML = rangeNote + '<p class="empty-state">No matching traffic in this range.</p>';
      return;
    }

    resultsEl.innerHTML = rangeNote + `
      <div class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>${label}</th>
              <th class="num">Upload</th>
              <th class="num">Download</th>
              <th class="num">Total</th>
            </tr>
          </thead>
          <tbody>
            ${data.rows.map((row) => `
              <tr>
                <td><span class="app-name">${escapeHtml(row[key])}</span></td>
                <td class="num val-up">${formatBytes(row.bytes_sent)}</td>
                <td class="num val-down">${formatBytes(row.bytes_received)}</td>
                <td class="num">${formatBytes(row.bytes_sent + row.bytes_received)}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
      <p class="empty-state">${data.rows.length} row(s)</p>`;
  } catch (err) {
    resultsEl.innerHTML = `<div class="error-banner">${escapeHtml(err.message)}</div>`;
  }
}

function bindAppsFilter() {
  let debounce;
  document.getElementById('apps-filter').addEventListener('input', (e) => {
    clearTimeout(debounce);
    debounce = setTimeout(() => loadApps(e.target.value.trim()), 250);
  });
}

async function loadApps(filter) {
  const grid = document.getElementById('apps-grid');
  grid.innerHTML = '<p class="empty-state">Loading…</p>';

  try {
    const data = await window.netvanApi.listApps(filter || null);
    if (data.error) {
      grid.innerHTML = `<div class="error-banner">${escapeHtml(data.error)}</div>`;
      return;
    }

    if (!data.apps.length) {
      grid.innerHTML = '<p class="empty-state">No applications found.</p>';
      return;
    }

    grid.innerHTML = data.apps.map((app) => `
      <div class="app-chip">${escapeHtml(app)}</div>
    `).join('');
  } catch (err) {
    grid.innerHTML = `<div class="error-banner">${escapeHtml(err.message)}</div>`;
  }
}

function bindServiceControls() {
  document.getElementById('service-refresh').addEventListener('click', refreshServiceStatus);

  bindControlAction('service-install', 'Service install', () => window.netvanApi.installService());
  bindControlAction('service-uninstall', 'Service uninstall', () => window.netvanApi.uninstallService());
  bindControlAction('service-start', 'Service start', () => window.netvanApi.startService());
  bindControlAction('service-stop', 'Service stop', () => window.netvanApi.stopService());
  bindControlAction('service-restart', 'Service restart', () => window.netvanApi.restartService());

  document.getElementById('data-reset').addEventListener('click', async () => {
    const confirmed = window.confirm(
      'Delete all traffic data and restart the Windows service if it is running?\n\nThis cannot be undone.'
    );
    if (!confirmed) return;

    const result = await window.netvanApi.resetData();
    const message = stripAnsi(result.stderr || result.stdout)
      || (result.ok ? 'Database reset complete' : 'Reset failed');
    showToast(message, !result.ok);
    await refreshServiceStatus();
    if (currentView === 'live') await refreshLive();
    if (currentView === 'about') await loadAbout();
  });
}

async function bindControlAction(elementId, label, action) {
  document.getElementById(elementId).addEventListener('click', async () => {
    const result = await action();
    const message = stripAnsi(result.stderr || result.stdout)
      || (result.ok ? `${label} requested` : `${label} failed`);
    showToast(message, !result.ok);
    await refreshServiceStatus();
    if (currentView === 'live') await refreshLive();
  });
}

async function refreshServiceStatus() {
  const serviceEl = document.getElementById('service-output');
  serviceEl.textContent = 'Loading…';

  const service = await window.netvanApi.getServiceStatus();
  serviceEl.textContent = stripAnsi(
    service.stdout || service.stderr || (service.ok ? 'OK' : 'Failed to get service status')
  );
}

function bindSettingsControls() {
  const guiControls = [
    { id: 'setting-launch-startup', key: 'launchAtStartup' },
    { id: 'setting-close-tray', key: 'closeToTray' },
  ];

  guiControls.forEach(({ id, key }) => {
    document.getElementById(id).addEventListener('change', async (event) => {
      try {
        await window.netvanApi.setSettings({ [key]: event.target.checked });
        showToast('Settings saved');
      } catch (err) {
        showToast(err.message, true);
        await loadSettings();
      }
    });
  });

  document.getElementById('setting-disable-vpn').addEventListener('change', async (event) => {
    try {
      await window.netvanApi.setConfig({ disableVpnTracking: event.target.checked });
      showToast('Collection settings saved');
    } catch (err) {
      showToast(err.message, true);
      await loadSettings();
    }
  });
}

async function loadSettings() {
  try {
    const [settings, cfg] = await Promise.all([
      window.netvanApi.getSettings(),
      window.netvanApi.getConfig(),
    ]);
    document.getElementById('setting-launch-startup').checked = settings.launchAtStartup;
    document.getElementById('setting-close-tray').checked = settings.closeToTray;
    document.getElementById('setting-disable-vpn').checked = Boolean(cfg.disableVpnTracking);
  } catch (err) {
    showToast(err.message, true);
  }
}

async function loadAbout() {
  const el = document.getElementById('about-info');

  try {
    const [cfg, info] = await Promise.all([
      window.netvanApi.getConfig(),
      window.netvanApi.getDatabaseInfo(),
    ]);

    if (info.error) {
      el.innerHTML = `<div class="error-banner">${escapeHtml(info.error)}</div>`;
      return;
    }

    const rows = [
      ['Home', cfg.home],
      ['Config', cfg.configPath],
      ['Database', info.databasePath],
      ['File size', formatBytes(info.fileBytes)],
      ['Rows', info.rowCount.toLocaleString()],
      ['Applications', info.distinctAppCount.toLocaleString()],
      ['First', formatLocal(info.firstMinuteUtc)],
      ['Last', formatLocal(info.lastMinuteUtc)],
      ['Retention', `${cfg.retentionDays} days`],
      ['Max DB size', `${cfg.maxSizeMb} MB`],
      ['VPN tracking', cfg.disableVpnTracking ? 'Disabled' : 'Enabled'],
    ];

    el.innerHTML = rows.map(([key, value]) => `
      <div class="info-row">
        <div class="info-key">${escapeHtml(key)}</div>
        <div class="info-value">${escapeHtml(String(value))}</div>
      </div>
    `).join('');
  } catch (err) {
    el.innerHTML = `<div class="error-banner">${escapeHtml(err.message)}</div>`;
  }
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

document.addEventListener('DOMContentLoaded', init);
