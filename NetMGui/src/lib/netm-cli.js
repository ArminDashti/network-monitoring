const { execFile } = require('child_process');
const { promisify } = require('util');
const path = require('path');
const fs = require('fs');
const { resolveHome } = require('./paths');

const execFileAsync = promisify(execFile);

function findNetmExecutable() {
  const home = resolveHome();
  const candidates = [
    path.join(home, 'netm.exe'),
    path.join(process.cwd(), '..', 'NetworkMonitoringExport', 'netm.exe'),
    path.join(process.cwd(), '..', 'NetworkMonitor', 'bin', 'Debug', 'net9.0-windows', 'netm.exe'),
    'netm',
  ];

  for (const candidate of candidates) {
    if (candidate === 'netm') continue;
    if (fs.existsSync(candidate)) return candidate;
  }
  return 'netm';
}

async function runNetm(args, options = {}) {
  const exe = findNetmExecutable();
  const home = resolveHome();
  const env = { ...process.env, NETM_HOME: home, ...options.env };

  try {
    const { stdout, stderr } = await execFileAsync(exe, args, {
      env,
      windowsHide: true,
      timeout: options.timeout || 30000,
      maxBuffer: 1024 * 1024,
    });
    return {
      ok: true,
      exitCode: 0,
      stdout: stdout.trim(),
      stderr: stderr.trim(),
    };
  } catch (err) {
    return {
      ok: false,
      exitCode: err.code ?? 1,
      stdout: (err.stdout || '').trim(),
      stderr: (err.stderr || err.message || '').trim(),
    };
  }
}

async function getServiceStatus() {
  return runNetm(['service', 'status']);
}

async function getCollectorStatus() {
  return runNetm(['status']);
}

async function startService() {
  return runNetm(['service', 'start']);
}

async function stopService() {
  return runNetm(['service', 'stop']);
}

async function getInfo(dbPath) {
  return runNetm(['info', '--db', dbPath]);
}

module.exports = {
  findNetmExecutable,
  runNetm,
  getServiceStatus,
  getCollectorStatus,
  startService,
  stopService,
  getInfo,
};
