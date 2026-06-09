const K = 1024;

function formatBytes(value) {
  let n = Math.max(0, Number(value) || 0);
  if (n < K) return `${n} B`;
  if (n < K * K) return `${(n / K).toFixed(2)} KB`;
  if (n < K * K * K) return `${(n / (K * K)).toFixed(2)} MB`;
  if (n < K * K * K * K) return `${(n / (K * K * K)).toFixed(2)} GB`;
  return `${(n / (K * K * K * K)).toFixed(2)} TB`;
}

function formatMegabytes(bytes) {
  return `${(Math.max(0, bytes) / (K * K)).toFixed(2)} MB`;
}

function formatGigabytes(bytes) {
  return `${(Math.max(0, bytes) / (K * K * K)).toFixed(2)} GB`;
}

function formatSpeed(bytesPerSecond) {
  const mbps = (Math.max(0, bytesPerSecond) * 8) / (K * K);
  if (mbps >= 1000) return `${(mbps / 1000).toFixed(1)} Gb/s`;
  if (mbps >= 1) return `${mbps.toFixed(1)} Mb/s`;
  const kbps = (Math.max(0, bytesPerSecond) * 8) / K;
  return `${kbps.toFixed(0)} Kb/s`;
}

function formatDownUpPair(downBytes, upBytes) {
  return `${formatGigabytes(downBytes)} / ${formatGigabytes(upBytes)}`;
}

function formatLocal(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleString();
}

function formatLocalShort(iso) {
  if (!iso) return '—';
  const d = new Date(iso);
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
    + ' ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
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
let currentView = 'live';

async function init() {
  bindWindowControls();
  bindNavigation();
  bindUsageQuery();
  bindServiceControls();
  bindAppsFilter();
  bindSettingsControls();

  try {
    config = await window.netvanApi.getConfig();
  } catch {
    config = { samplingInterval: 1 };
  }

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

      if (view === 'service') await refreshServiceStatus();
      if (view === 'settings') await loadSettings();
      if (view === 'about') await loadAbout();
      if (view === 'apps') await loadApps(document.getElementById('apps-filter').value);
    });
  });
}

function startLivePolling() {
  const intervalMs = Math.max(1000, (config?.samplingInterval || 1) * 1000);
  if (liveTimer) clearInterval(liveTimer);
  liveTimer = setInterval(refreshLive, intervalMs);
}

async function refreshLive() {
  const statusEl = document.getElementById('live-status');
  const tbody = document.getElementById('live-tbody');

  try {
    const data = await window.netvanApi.getRealtime(true);

    if (data.error) {
      statusEl.classList.add('error');
      statusEl.innerHTML = '<span class="status-dot"></span> Database unavailable';
      tbody.innerHTML = `<tr><td colspan="6" class="empty-state">${escapeHtml(data.error)}</td></tr>`;
      document.getElementById('live-download').textContent = '—';
      document.getElementById('live-upload').textContent = '—';
      document.getElementById('live-total').textContent = '—';
      return;
    }

    statusEl.classList.remove('error');
    statusEl.innerHTML = '<span class="status-dot"></span> Live';

    document.getElementById('live-download').textContent = formatSpeed(data.totalDownBytes);
    document.getElementById('live-upload').textContent = formatSpeed(data.totalUpBytes);
    document.getElementById('live-total').textContent = formatSpeed(data.totalDownBytes + data.totalUpBytes);
    document.getElementById('live-updated').textContent = `Updated ${formatLocalShort(data.nowLocal)}`;

    document.getElementById('live-ranges').innerHTML = `
      <span class="badge">Daily: ${formatLocalShort(data.dailyStartLocal)}</span>
      <span class="badge">Weekly: ${formatLocalShort(data.weeklyStartLocal)}</span>
      <span class="badge">Monthly: ${formatLocalShort(data.monthlyStartLocal)}</span>
    `;

    if (!data.rows.length) {
      tbody.innerHTML = '<tr><td colspan="6" class="empty-state">No traffic recorded yet. Ensure the Netvan service is running.</td></tr>';
      return;
    }

    tbody.innerHTML = data.rows.map((row) => `
      <tr>
        <td><span class="app-name">${escapeHtml(row.appName)}</span></td>
        <td class="num val-down">${formatMegabytes(row.currentDownBytes)}</td>
        <td class="num val-up">${formatMegabytes(row.currentUpBytes)}</td>
        <td class="num">${formatDownUpPair(row.dailyDownBytes, row.dailyUpBytes)}</td>
        <td class="num">${formatDownUpPair(row.weeklyDownBytes, row.weeklyUpBytes)}</td>
        <td class="num">${formatDownUpPair(row.monthlyDownBytes, row.monthlyUpBytes)}</td>
      </tr>
    `).join('');
  } catch (err) {
    statusEl.classList.add('error');
    statusEl.innerHTML = '<span class="status-dot"></span> Error';
    tbody.innerHTML = `<tr><td colspan="6" class="empty-state">${escapeHtml(err.message)}</td></tr>`;
  }
}

function bindUsageQuery() {
  document.getElementById('usage-query').addEventListener('click', runUsageQuery);
}

async function runUsageQuery() {
  const fromRaw = document.getElementById('usage-from').value.trim() || null;
  const toRaw = document.getElementById('usage-to').value.trim() || null;
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

    const rangeNote = `<p class="empty-state" style="padding-top:0">Range (UTC): ${escapeHtml(data.fromUtc)} → ${escapeHtml(data.toUtc)}</p>`;

    if (data.kind === 'totals') {
      const t = data.totals;
      resultsEl.innerHTML = rangeNote + `
        <div class="totals-grid">
          <div class="total-item">
            <div class="label">Upload</div>
            <div class="value val-up">${formatBytes(t.bytesSent)}</div>
          </div>
          <div class="total-item">
            <div class="label">Download</div>
            <div class="value val-down">${formatBytes(t.bytesReceived)}</div>
          </div>
          <div class="total-item">
            <div class="label">Total</div>
            <div class="value">${formatBytes(t.bytesSent + t.bytesReceived)}</div>
          </div>
        </div>`;
      return;
    }

    const columns = {
      apps: ['Application', 'app_name'],
      ip: ['Remote IP', 'remote_ip'],
      host: ['Host', 'host_name'],
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
  const controls = [
    { id: 'setting-launch-startup', key: 'launchAtStartup' },
    { id: 'setting-close-tray', key: 'closeToTray' },
    { id: 'setting-notifications', key: 'notificationsEnabled' },
  ];

  controls.forEach(({ id, key }) => {
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
}

async function loadSettings() {
  try {
    const settings = await window.netvanApi.getSettings();
    document.getElementById('setting-launch-startup').checked = settings.launchAtStartup;
    document.getElementById('setting-close-tray').checked = settings.closeToTray;
    document.getElementById('setting-notifications').checked = settings.notificationsEnabled;
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
      ['First UTC', info.firstMinuteUtc || '(none)'],
      ['Last UTC', info.lastMinuteUtc || '(none)'],
      ['Sampling interval', `${cfg.samplingInterval}s`],
      ['Retention', `${cfg.retentionDays} days`],
      ['Max DB size', `${cfg.maxSizeMb} MB`],
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
