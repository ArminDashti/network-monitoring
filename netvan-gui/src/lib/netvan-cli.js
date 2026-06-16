const { execFile } = require('child_process');
const { promisify } = require('util');
const path = require('path');
const fs = require('fs');
const { resolveHome } = require('./paths');

const execFileAsync = promisify(execFile);

function findNetvanExecutable() {
  const home = resolveHome();
  const candidates = [
    path.join(home, 'netvan.exe'),
    path.join(path.dirname(process.execPath), 'netvan.exe'),
    path.join(path.dirname(process.execPath), '..', 'netvan.exe'),
    path.join(process.cwd(), '..', 'netvan', 'netvan.exe'),
    path.join(process.cwd(), '..', 'Netvan', 'netvan.exe'),
    path.join(process.cwd(), '..', 'netvan-core', 'bin', 'Release', 'net9.0-windows', 'win-x64', 'netvan.exe'),
    path.join(process.cwd(), '..', 'netvan-core', 'bin', 'Debug', 'net9.0-windows', 'netvan.exe'),
  ];

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return path.resolve(candidate);
  }
  return 'netvan';
}

async function runNetvan(args, options = {}) {
  const exe = findNetvanExecutable();
  const home = resolveHome();
  const env = { ...process.env, NETVAN_HOME: home, ...options.env };

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
  return runNetvan(['service', 'status']);
}

async function startService() {
  return runNetvan(['service', 'start']);
}

async function stopService() {
  return runNetvan(['service', 'stop']);
}

async function restartService() {
  const stopResult = await stopService();
  if (!stopResult.ok) return stopResult;
  return startService();
}

async function installService() {
  return runNetvan(['service', 'install'], { timeout: 60000 });
}

async function uninstallService() {
  return runNetvan(['service', 'uninstall'], { timeout: 60000 });
}

async function resetData() {
  return runNetvan(['reset'], { timeout: 90000 });
}

module.exports = {
  findNetvanExecutable,
  runNetvan,
  getServiceStatus,
  installService,
  uninstallService,
  startService,
  stopService,
  restartService,
  resetData,
};
