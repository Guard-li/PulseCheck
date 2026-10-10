const translations = {
  zh: {
    tagline: '本地优先的健康监测', overview: '总览', realtime: '实时性能', hardware: '硬件与存储', network: '网络', processes: '进程', scans: '体检', reports: '报告', devices: '设备与设置',
    quickScan: '快速检测', connect: '连接设备', deviceHealth: '设备健康', lastScan: '最近体检', operatingMode: '运行模式', uptime: '运行时长', memory: '内存', disk: '系统盘', temperature: '温度',
    recentAlerts: '最近告警', recentScans: '最近体检', noAlerts: '暂无活动告警', noScans: '尚无体检记录', diskIO: '磁盘吞吐', processList: '进程列表', name: '名称', company: '公司',
    startScan: '开始体检', fullScan: '完整检测', scanHint: '所有结论来自真实采集指标和配置规则。', redact: '默认脱敏', scanProgress: '检测进度', scanHistory: '体检历史', mode: '模式', status: '状态', started: '开始时间',
    reportCenter: '报告中心', refresh: '刷新', noReports: '尚无报告', connection: '连接', save: '保存', disconnect: '断开', privacySettings: '隐私与设置', privacyHint: 'Agent 默认只监听局域网和本机，报告默认隐藏 MAC、IP、用户名和序列号。', theme: '主题', language: '语言', certificate: 'TLS 指纹',
    pairTitle: '连接 PulseCheck Agent', pairHint: '在 Agent 所在电脑运行配对命令，或通过受信任局域网输入一次性配对码。', cancel: '取消', pair: '配对', unavailable: '不可用', active: '活动', resolved: '已恢复', completed: '已完成', failed: '失败', running: '运行中', queued: '排队中', cancelled: '已取消'
  },
  en: {
    tagline: 'Local-first health monitoring', overview: 'Overview', realtime: 'Realtime', hardware: 'Hardware & Storage', network: 'Network', processes: 'Processes', scans: 'Scans', reports: 'Reports', devices: 'Devices & Settings',
    quickScan: 'Quick scan', connect: 'Connect', deviceHealth: 'Device health', lastScan: 'Last scan', operatingMode: 'Mode', uptime: 'Uptime', memory: 'Memory', disk: 'System disk', temperature: 'Temperature',
    recentAlerts: 'Recent alerts', recentScans: 'Recent scans', noAlerts: 'No active alerts', noScans: 'No scans yet', diskIO: 'Disk throughput', processList: 'Processes', name: 'Name', company: 'Company',
    startScan: 'Start scan', fullScan: 'Full scan', scanHint: 'Every conclusion is derived from real metrics and configured rules.', redact: 'Redact by default', scanProgress: 'Progress', scanHistory: 'Scan history', mode: 'Mode', status: 'Status', started: 'Started',
    reportCenter: 'Report center', refresh: 'Refresh', noReports: 'No reports yet', connection: 'Connection', save: 'Save', disconnect: 'Disconnect', privacySettings: 'Privacy & settings', privacyHint: 'The Agent listens on local networks only and reports hide MAC, IP, username and serials by default.', theme: 'Theme', language: 'Language', certificate: 'TLS fingerprint',
    pairTitle: 'Connect PulseCheck Agent', pairHint: 'Enter the one-time code from the Agent computer or verify the TLS fingerprint.', cancel: 'Cancel', pair: 'Pair', unavailable: 'Unavailable', active: 'Active', resolved: 'Resolved', completed: 'Completed', failed: 'Failed', running: 'Running', queued: 'Queued', cancelled: 'Cancelled'
  }
};

const state = {
  base: localStorage.getItem('pulsecheck.base') || location.origin,
  token: localStorage.getItem('pulsecheck.token') || '',
  lang: localStorage.getItem('pulsecheck.lang') || 'zh',
  theme: localStorage.getItem('pulsecheck.theme') || 'dark',
  ws: null,
  reconnectTimer: null,
  paused: false,
  samples: [],
  latest: null,
  currentView: 'dashboard',
  rangeMinutes: 1,
  scans: [],
  reports: [],
  pairingInfo: null
};

const $ = selector => document.querySelector(selector);
const $$ = selector => [...document.querySelectorAll(selector)];
const t = key => translations[state.lang][key] || key;
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const formatBytes = value => {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return '--';
  let number = Number(value), unit = 0; const units = ['B','KB','MB','GB','TB'];
  while (number >= 1024 && unit < units.length - 1) { number /= 1024; unit++; }
  return `${number.toFixed(unit > 1 ? 1 : 0)} ${units[unit]}`;
};
const formatDate = value => value ? new Date(value).toLocaleString() : '--';
const clamp = (value, min, max) => Math.max(min, Math.min(max, Number(value) || 0));
const statusText = value => t(String(value || '').toLowerCase()) || value;

function normalizeBase(value) {
  const raw = (value || location.origin).trim().replace(/\/+$/, '');
  return raw.endsWith('/api/v1') ? raw.slice(0, -7) : raw;
}

async function api(path, options = {}) {
  const headers = new Headers(options.headers || {});
  if (state.token) headers.set('Authorization', `Bearer ${state.token}`);
  if (options.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  const response = await fetch(`${state.base}/api/v1${path}`, { ...options, headers });
  const type = response.headers.get('content-type') || '';
  const payload = type.includes('json') ? await response.json() : await response.text();
  if (!response.ok) throw new Error(payload?.error?.message || payload?.message || `HTTP ${response.status}`);
  return payload;
}

function applyLanguage() {
  document.documentElement.lang = state.lang === 'zh' ? 'zh-CN' : 'en';
  $$('[data-i18n]').forEach(element => element.textContent = t(element.dataset.i18n));
  $('#languageButton').textContent = state.lang === 'zh' ? 'EN' : '中';
  $('#pageTitle').textContent = t(state.currentView);
}

function applyTheme() {
  document.documentElement.dataset.theme = state.theme;
  document.querySelector('meta[name=theme-color]').content = state.theme === 'dark' ? '#0b1220' : '#eef3f9';
}

function cycleTheme() {
  state.theme = state.theme === 'dark' ? 'light' : 'dark';
  localStorage.setItem('pulsecheck.theme', state.theme);
  applyTheme();
}

function cycleLanguage() {
  state.lang = state.lang === 'zh' ? 'en' : 'zh';
  localStorage.setItem('pulsecheck.lang', state.lang);
  applyLanguage();
  renderAll();
}

function setView(view) {
  state.currentView = view;
  $$('.view').forEach(x => x.classList.toggle('active', x.id === `view-${view}`));
  $$('.nav-item').forEach(x => x.classList.toggle('active', x.dataset.view === view));
  $('#pageTitle').textContent = t(view);
}

function setConnection(online, text) {
  $('#connectionDot').classList.toggle('online', online);
  $('#connectionDot').classList.toggle('offline', !online);
  $('#connectionText').textContent = text || (online ? '已连接' : '未连接');
}

async function fetchInitial() {
  if (!state.token || !state.base) return;
  try {
    const [current, history, alerts, scans, reports] = await Promise.all([
      api('/metrics/current'),
      api(`/metrics/history?from=${encodeURIComponent(new Date(Date.now() - 3600000).toISOString())}&limit=2000`).catch(() => ({ data: [] })),
      api('/alerts?limit=20').catch(() => ({ data: [] })),
      api('/scans?limit=20').catch(() => ({ data: [] })),
      api('/reports?limit=20').catch(() => ({ data: [] }))
    ]);
    state.samples = history.data || [];
    state.scans = scans.data || [];
    state.reports = reports.data || [];
    updateMetrics(current.data);
    renderAlerts(alerts.data || []);
    renderScans();
    renderReports();
    setConnection(true, '已连接');
  } catch (error) {
    setConnection(false, error.message);
  }
}

function connectWebSocket() {
  if (!state.base || !state.token) return;
  if (state.ws) state.ws.close();
  const url = `${state.base.replace(/^http/, 'ws')}/api/v1/realtime`;
  const socket = new WebSocket(url, ['pulsecheck.v1', `token.${state.token}`]);
  state.ws = socket;
  socket.onopen = () => setConnection(true, '实时连接');
  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.type === 'metrics') updateMetrics(message.payload);
    if (message.type === 'alert') refreshAlerts();
  };
  socket.onerror = () => setConnection(false, '连接异常');
  socket.onclose = () => {
    setConnection(false, '已断开');
    clearTimeout(state.reconnectTimer);
    state.reconnectTimer = setTimeout(connectWebSocket, 3000);
  };
}

async function pair(event) {
  event.preventDefault();
  const base = normalizeBase($('#pairUrl').value);
  const code = $('#pairCode').value.trim();
  if (!/^\d{6}$/.test(code)) { $('#pairError').textContent = '请输入 6 位配对码'; return; }
  try {
    $('#pairError').textContent = '正在验证证书并配对…';
    const response = await fetch(`${base}/api/v1/auth/pair`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pairingCode: code, controllerName: $('#controllerName').value || 'Web Controller', role: 'owner' })
    });
    const payload = await response.json();
    if (!response.ok || !payload.success) throw new Error(payload?.error?.message || '配对失败');
    state.base = base; state.token = payload.data.token;
    localStorage.setItem('pulsecheck.base', base); localStorage.setItem('pulsecheck.token', state.token);
    $('#pairError').textContent = ''; $('#pairDialog').close();
    await fetchInitial(); connectWebSocket();
  } catch (error) {
    $('#pairError').textContent = `${error.message}。首次使用请先在浏览器确认 Agent 的自签名 TLS 证书。`;
  }
}

function updateMetrics(snapshot) {
  if (!snapshot || state.paused) return;
  state.latest = snapshot;
  if (!state.samples.length || state.samples[state.samples.length - 1].snapshotId !== snapshot.snapshotId) {
    state.samples.push(snapshot);
    const cutoff = Date.now() - 24 * 3600000;
    state.samples = state.samples.filter(x => new Date(x.timestampUtc).getTime() >= cutoff).slice(-4000);
  }
  const cpu = snapshot.cpu?.totalUsagePercent ?? 0;
  const memory = snapshot.memory?.usagePercent ?? 0;
  const systemVolume = snapshot.disk?.volumes?.find(x => x.isSystemVolume) || snapshot.disk?.volumes?.[0];
  const diskFree = systemVolume ? 100 - (systemVolume.usedPercent || 0) : null;
  const gpu = snapshot.gpus?.[0];
  const temperature = snapshot.sensors?.cpuTemperatureCelsius ?? gpu?.temperatureCelsius;

  $('#cpuValue').textContent = `${cpu.toFixed(1)}%`;
  $('#memoryValue').textContent = `${memory.toFixed(1)}%`;
  $('#diskValue').textContent = diskFree === null ? '--' : `${diskFree.toFixed(1)}%`;
  $('#gpuValue').textContent = gpu?.usagePercent == null ? '--' : `${gpu.usagePercent.toFixed(1)}%`;
  $('#tempValue').textContent = temperature == null ? '--' : `${temperature.toFixed(1)} °C`;
  $('#cpuBar').style.width = `${clamp(cpu, 0, 100)}%`;
  $('#memoryBar').style.width = `${clamp(memory, 0, 100)}%`;
  $('#diskBar').style.width = `${clamp(diskFree ?? 0, 0, 100)}%`;
  $('#gpuBar').style.width = `${clamp(gpu?.usagePercent ?? 0, 0, 100)}%`;
  $('#cpuDetail').textContent = `${snapshot.cpu?.logicalProcessorCount || '--'} 线程 · ${snapshot.cpu?.currentFrequencyMhz ? Math.round(snapshot.cpu.currentFrequencyMhz) + ' MHz' : '频率不可用'}`;
  $('#memoryDetail').textContent = `${formatBytes(snapshot.memory?.usedBytes)} / ${formatBytes(snapshot.memory?.totalBytes)}`;
  $('#diskDetail').textContent = systemVolume ? `${systemVolume.name} · ${formatBytes(systemVolume.freeBytes)} 可用` : '不可用';
  $('#gpuDetail').textContent = gpu?.name || '未检测到受支持 GPU';
  $('#tempDetail').textContent = gpu?.name || '标准传感器';
  $('#powerValue').textContent = gpu?.powerWatts == null ? '功耗不可用' : `GPU ${gpu.powerWatts.toFixed(1)} W`;
  $('#networkValue').textContent = snapshot.network?.adapters?.find(x => x.status === 'Up')?.name || '--';
  $('#networkRate').textContent = `↓ ${formatBytes(snapshot.network?.downloadBytesPerSecond)}/s  ↑ ${formatBytes(snapshot.network?.uploadBytesPerSecond)}/s`;
  $('#uptime').textContent = formatDuration(snapshot.system?.uptime);
  $('#operatingMode').textContent = snapshot.system?.isAdministrator ? '完整模式' : '受限模式';
  $('#deviceLine').textContent = `${snapshot.system?.computerName || '本机'} · ${snapshot.system?.osName || ''} ${snapshot.system?.osVersion || ''}`;
  renderProcesses();
  renderHardware();
  renderNetwork();
  drawAllCharts();
}

function formatDuration(value) {
  if (!value) return '--';
  const match = String(value).match(/^(?:(\d+)\.)?(\d+):(\d+):(\d+)/);
  if (!match) return String(value);
  return `${Number(match[1] || 0)}天 ${match[2]}时 ${match[3]}分`;
}

function drawAllCharts() {
  const cutoff = Date.now() - state.rangeMinutes * 60000;
  const data = state.samples.filter(x => new Date(x.timestampUtc).getTime() >= cutoff);
  drawChart('#chart-cpu', [data.map(x => x.cpu?.totalUsagePercent || 0)], 100, '%');
  $('#chartCpu').textContent = `${(state.latest?.cpu?.totalUsagePercent || 0).toFixed(1)}%`;
  drawChart('#chart-memory', [data.map(x => x.memory?.usagePercent || 0)], 100, '%');
  $('#chartMemory').textContent = `${(state.latest?.memory?.usagePercent || 0).toFixed(1)}%`;
  drawChart('#chart-disk', [
    data.map(x => (x.disk?.totalReadBytesPerSecond || 0) / 1048576),
    data.map(x => (x.disk?.totalWriteBytesPerSecond || 0) / 1048576)
  ], null, 'MB/s');
  const diskRate = ((state.latest?.disk?.totalReadBytesPerSecond || 0) + (state.latest?.disk?.totalWriteBytesPerSecond || 0)) / 1048576;
  $('#chartDisk').textContent = `${diskRate.toFixed(1)} MB/s`;
  drawChart('#chart-network', [
    data.map(x => (x.network?.downloadBytesPerSecond || 0) / 1048576),
    data.map(x => (x.network?.uploadBytesPerSecond || 0) / 1048576)
  ], null, 'MB/s');
  const networkRate = ((state.latest?.network?.downloadBytesPerSecond || 0) + (state.latest?.network?.uploadBytesPerSecond || 0)) / 1048576;
  $('#chartNetwork').textContent = `${networkRate.toFixed(2)} MB/s`;
}

function drawChart(selector, series, fixedMax, unit) {
  const canvas = $(selector); if (!canvas) return;
  const ratio = devicePixelRatio || 1, width = canvas.clientWidth || 500, height = canvas.clientHeight || 220;
  canvas.width = width * ratio; canvas.height = height * ratio;
  const ctx = canvas.getContext('2d'); ctx.scale(ratio, ratio); ctx.clearRect(0, 0, width, height);
  const all = series.flat(); const max = fixedMax || Math.max(1, ...all) * 1.2;
  const colors = ['#2f80ed', '#f2994a'];
  ctx.strokeStyle = getComputedStyle(document.documentElement).getPropertyValue('--line'); ctx.lineWidth = 1;
  for (let i = 0; i <= 4; i++) { const y = 18 + (height - 42) * i / 4; ctx.beginPath(); ctx.moveTo(34, y); ctx.lineTo(width - 10, y); ctx.stroke(); }
  ctx.fillStyle = getComputedStyle(document.documentElement).getPropertyValue('--muted'); ctx.font = '11px sans-serif';
  for (let i = 0; i <= 4; i++) { const y = 18 + (height - 42) * i / 4; ctx.fillText(((max * (4 - i) / 4).toFixed(max < 10 ? 1 : 0)), 2, y + 4); }
  series.forEach((values, index) => {
    if (values.length < 1) return;
    ctx.strokeStyle = colors[index % colors.length]; ctx.lineWidth = 2.2; ctx.beginPath();
    values.forEach((value, i) => {
      const x = 34 + (width - 44) * (values.length === 1 ? 1 : i / (values.length - 1));
      const y = 18 + (height - 42) * (1 - clamp(value, 0, max) / max);
      i ? ctx.lineTo(x, y) : ctx.moveTo(x, y);
    }); ctx.stroke();
  });
  if (!all.length) { ctx.fillStyle = getComputedStyle(document.documentElement).getPropertyValue('--muted'); ctx.fillText('等待真实数据…', width / 2 - 34, height / 2); }
}

function renderProcesses() {
  const query = ($('#processSearch').value || '').toLowerCase();
  const rows = (state.latest?.topProcesses || []).filter(x => !query || x.name.toLowerCase().includes(query));
  $('#processTable').innerHTML = rows.map(x => `<tr><td>${x.processId}</td><td>${esc(x.name)}</td><td>${x.cpuUsagePercent == null ? '--' : x.cpuUsagePercent.toFixed(2) + '%'}</td><td>${formatBytes(x.workingSetBytes)}</td><td>${esc(x.company || '--')}</td></tr>`).join('') || '<tr><td colspan="5" class="empty">暂无进程数据</td></tr>';
}

function renderHardware() {
  const s = state.latest; if (!s) return;
  const modules = (s.memory?.modules || []).map(x => `<div class="list-item"><span>${esc(x.bankLabel || '内存插槽')}<br><small>${esc(x.manufacturer || '')} ${esc(x.partNumber || '')}</small></span><b>${formatBytes(x.capacityBytes)} · ${x.speedMhz || '--'} MHz</b></div>`).join('');
  const disks = (s.disk?.volumes || []).map(x => `<div class="list-item"><span>${esc(x.name)} ${esc(x.label || '')}<br><small>${esc(x.fileSystem || '')}</small></span><b>${formatBytes(x.freeBytes)} 可用</b></div>`).join('');
  const physical = (s.disk?.physicalDisks || []).map(x => `<div class="list-item"><span>${esc(x.friendlyName)}<br><small>${esc(x.mediaType)} · ${esc(x.busType)}</small></span><b>${esc(x.healthStatus || '状态未知')}</b></div>`).join('');
  $('#hardwareContent').innerHTML = `<article class="card"><h2>CPU</h2><div class="list-item"><span>${esc(s.cpu?.name)}<br><small>${s.cpu?.physicalCoreCount || '--'} 核 / ${s.cpu?.logicalProcessorCount || '--'} 线程</small></span><b>${s.cpu?.currentFrequencyMhz ? Math.round(s.cpu.currentFrequencyMhz) + ' MHz' : '频率不可用'}</b></div></article><article class="card"><h2>内存模块</h2>${modules || '<div class="empty">未读取到 SMBIOS 内存模块</div>'}</article><article class="card"><h2>卷</h2>${disks}</article><article class="card"><h2>物理磁盘与 SMART</h2>${physical || `<div class="empty">${state.lang === 'zh' ? '需要管理员权限和存储管理组件' : 'Administrator and Storage Management required'}</div>`}</article>`;
}

function renderNetwork() {
  const adapters = state.latest?.network?.adapters?.filter(x => x.status === 'Up' && (x.ipAddresses?.length || x.gateways?.length)) || [];
  $('#networkContent').innerHTML = adapters.map(x => `<article class="card"><div class="card-title"><h2>${esc(x.name)}</h2><span class="muted">${esc(x.status)}</span></div><p>${esc(x.description)}</p><div class="grid metric-inline"><div><small>IP</small><b>${esc((x.ipAddresses || []).join(', ') || '--')}</b></div><div><small>网关</small><b>${esc((x.gateways || []).join(', ') || '--')}</b></div><div><small>DNS</small><b>${esc((x.dnsServers || []).join(', ') || '--')}</b></div><div><small>速率</small><b>↓ ${formatBytes(x.downloadBytesPerSecond)}/s ↑ ${formatBytes(x.uploadBytesPerSecond)}/s</b></div><div><small>链路</small><b>${esc(x.linkSpeed || '--')}</b></div></div>${x.wifi ? `<p>Wi-Fi ${esc(x.wifi.ssid || '')} · ${esc(x.wifi.band || '')} · 信道 ${x.wifi.channel || '--'} · 信号 ${x.wifi.signalPercent || '--'}%</p>` : ''}</article>`).join('') || '<div class="card empty">暂无活动网络适配器数据</div>';
}

function renderAlerts(alerts) {
  $('#alertList').innerHTML = alerts.length ? alerts.map(x => `<div class="list-item"><span><b>${esc(x.title)}</b><br><small>${esc(x.description)}</small></span><small>${formatDate(x.createdAtUtc)}</small></div>`).join('') : `<div class="empty">${t('noAlerts')}</div>`;
}

async function refreshAlerts() {
  try { const response = await api('/alerts?limit=20'); renderAlerts(response.data || []); } catch {}
}

function renderScans() {
  $('#recentScans').innerHTML = state.scans.length ? state.scans.slice(0, 5).map(x => `<div class="list-item"><span>${esc(x.mode)} · ${statusText(x.status)}<br><small>${formatDate(x.createdAtUtc)}</small></span><small>${x.progress?.percent ?? 0}%</small></div>`).join('') : `<div class="empty">${t('noScans')}</div>`;
  $('#scanTable').innerHTML = state.scans.length ? state.scans.map(x => `<tr><td>${esc(x.scanId.slice(0, 8))}</td><td>${esc(x.mode)}</td><td>${esc(statusText(x.status))}</td><td>${formatDate(x.createdAtUtc)}</td><td>${x.reportId ? `<button class="secondary" onclick="openReport('${x.reportId}')">报告</button>` : ''}</td></tr>`).join('') : '<tr><td colspan="5" class="empty">暂无体检记录</td></tr>';
  const latest = state.scans[0];
  if (latest) {
    $('#scanProgressBar').style.width = `${latest.progress?.percent || 0}%`;
    $('#scanProgressText').textContent = `${statusText(latest.status)} · ${latest.progress?.message || ''}`;
    $('#scanLog').textContent = JSON.stringify(latest.progress || {}, null, 2);
  }
}

async function startScan(mode) {
  try {
    setView('scans');
    const response = await api('/scans', { method: 'POST', body: JSON.stringify({ mode, intensity: 'low', generatePdf: $('#generatePdf').checked, redactSensitiveData: $('#redactReport').checked, allowDiskWriteTest: false }) });
    pollScan(response.data.scanId);
  } catch (error) { $('#scanProgressText').textContent = error.message; }
}

async function pollScan(id) {
  try {
    const response = await api(`/scans/${id}`);
    const scan = response.data;
    $('#scanProgressBar').style.width = `${scan.progress?.percent || 0}%`;
    $('#scanProgressText').textContent = `${statusText(scan.status)} · ${scan.progress?.message || ''}`;
    $('#scanLog').textContent = JSON.stringify(scan, null, 2);
    if (['completed', 'failed', 'cancelled'].includes(scan.status)) {
      await fetchInitial(); setView('reports');
      return;
    }
    setTimeout(() => pollScan(id), 700);
  } catch (error) { $('#scanProgressText').textContent = error.message; }
}

function renderReports() {
  $('#reportList').innerHTML = state.reports.length ? state.reports.map(report => `<article class="report-item"><h3>${esc(report.deviceName)} · ${report.score}</h3><p class="muted">${formatDate(report.generatedAtUtc)} · ${esc(report.riskLevel)}风险 · ${report.findingCount} 项</p><div class="downloads">${(report.artifacts || []).map(artifact => `<button class="secondary" onclick="downloadArtifact('${report.reportId}','${artifact.format}','${esc(artifact.fileName)}')">${artifact.format.toUpperCase()}</button>`).join('')}</div></article>`).join('') : `<div class="empty">${t('noReports')}</div>`;
}

async function downloadArtifact(reportId, format, fileName) {
  try {
    const response = await fetch(`${state.base}/api/v1/reports/${reportId}/artifacts/${format}`, { headers: { Authorization: `Bearer ${state.token}` } });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const blob = await response.blob(), url = URL.createObjectURL(blob), anchor = document.createElement('a');
    anchor.href = url; anchor.download = fileName; document.body.appendChild(anchor); anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (error) { alert(error.message); }
}

async function refreshReports() {
  try { const response = await api('/reports?limit=100'); state.reports = response.data || []; renderReports(); } catch (error) { alert(error.message); }
}

function renderAll() {
  if (state.latest) updateMetrics(state.latest);
  renderScans(); renderReports();
}

function bindEvents() {
  $$('.nav-item').forEach(button => button.addEventListener('click', () => setView(button.dataset.view)));
  $('#themeButton').addEventListener('click', cycleTheme);
  $('#languageButton').addEventListener('click', cycleLanguage);
  $('#settingsTheme').addEventListener('click', cycleTheme);
  $('#settingsLanguage').addEventListener('click', cycleLanguage);
  $('#connectButton').addEventListener('click', openPairDialog);
  $('#quickScanButton').addEventListener('click', () => startScan('quick'));
  $('#startScanButton').addEventListener('click', () => startScan(document.querySelector('input[name=scanMode]:checked').value));
  $('#pairForm').addEventListener('submit', pair);
  $('#pauseButton').addEventListener('click', () => { state.paused = !state.paused; $('#pauseButton').textContent = state.paused ? '继续更新' : '暂停更新'; });
  $$('.range-tabs button').forEach(button => button.addEventListener('click', () => { $$('.range-tabs button').forEach(x => x.classList.remove('active')); button.classList.add('active'); state.rangeMinutes = Number(button.dataset.range); drawAllCharts(); }));
  $('#processSearch').addEventListener('input', renderProcesses);
  $('#refreshReports').addEventListener('click', refreshReports);
  $('#saveConnection').addEventListener('click', () => { state.base = normalizeBase($('#agentUrl').value); state.token = $('#tokenField').value.trim(); localStorage.setItem('pulsecheck.base', state.base); localStorage.setItem('pulsecheck.token', state.token); fetchInitial(); connectWebSocket(); });
  $('#disconnectButton').addEventListener('click', () => { state.token = ''; localStorage.removeItem('pulsecheck.token'); state.ws?.close(); setConnection(false, '未连接'); openPairDialog(); });
  window.openReport = async id => { try { const response = await api(`/reports/${id}`); state.reports = [response.data]; setView('reports'); renderReports(); } catch (error) { alert(error.message); } };
  window.downloadArtifact = downloadArtifact;
}

async function openPairDialog() {
  $('#pairUrl').value = state.base || location.origin;
  $('#pairError').textContent = '';
  $('#pairDialog').showModal();
  try {
    if (location.hostname === 'localhost' || location.hostname === '127.0.0.1' || location.hostname === '::1') {
      const response = await fetch(`${normalizeBase($('#pairUrl').value)}/api/v1/pairing/info`, { cache: 'no-store' });
      if (response.ok) {
        const payload = await response.json(); state.pairingInfo = payload.data;
        $('#pairCode').value = payload.data.pairingCode || '';
        $('#certFingerprint').textContent = payload.data.certificateSha256 || '--';
      }
    }
  } catch { $('#pairError').textContent = '请先在浏览器信任 Agent 的本地 TLS 证书。'; }
}

async function init() {
  applyTheme(); applyLanguage(); bindEvents();
  $('#agentUrl').value = state.base; $('#tokenField').value = state.token;
  if (state.token) { await fetchInitial(); connectWebSocket(); }
  else setTimeout(openPairDialog, 250);
}

init();