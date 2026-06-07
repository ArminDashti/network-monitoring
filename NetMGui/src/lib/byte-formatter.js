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

function formatUptime(ms) {
  const totalSeconds = Math.floor(ms / 1000);
  const days = Math.floor(totalSeconds / 86400);
  const hours = Math.floor((totalSeconds % 86400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (days >= 1) return `${days}d ${hours}h ${minutes}m`;
  if (hours >= 1) return `${hours}h ${minutes}m ${seconds}s`;
  return `${minutes}m ${seconds}s`;
}

module.exports = {
  formatBytes,
  formatMegabytes,
  formatGigabytes,
  formatSpeed,
  formatUptime,
};
