'use strict';

const S = {
  screen: 'dash', charId: null, status: null, session: null,
  dashRange: '24h', repRange: '30d', tab: 'trading', report: null,
  overlayBg: 'scene',
};

const $ = id => document.getElementById(id);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

async function api(path, opts) {
  const r = await fetch(path, opts);
  if (!r.ok) {
    let msg = 'HTTP ' + r.status;
    try { const j = await r.json(); if (j.error) msg = j.error; } catch (e) { /* kein JSON */ }
    throw new Error(msg);
  }
  return r.status === 204 ? null : r.json();
}

// ---------- Formatierung (PULSAR-Stil: 14.72B / 182.4M) ----------

function fmtIsk(v) {
  const a = Math.abs(v);
  if (a >= 1e12) return (a / 1e12).toFixed(2) + 'T';
  if (a >= 1e9) return (a / 1e9).toFixed(2) + 'B';
  if (a >= 1e6) return (a / 1e6).toFixed(1) + 'M';
  if (a >= 1e3) return (a / 1e3).toFixed(1) + 'K';
  return Math.round(a).toString();
}
const signed = v => (v < 0 ? '−' : '+') + fmtIsk(v);
const cls = v => v > 0 ? 'pos' : (v < 0 ? 'neg' : '');
const full = v => Math.round(v).toLocaleString('de-DE') + ' ISK';

/** Großer Kennzahlwert mit abgesetzter Einheit, wie im Design ("14.72 B ISK"). */
function bigIsk(v, unitSuffix) {
  const a = Math.abs(v);
  let num, unit;
  if (a >= 1e12) { num = (a / 1e12).toFixed(2); unit = 'T'; }
  else if (a >= 1e9) { num = (a / 1e9).toFixed(2); unit = 'B'; }
  else if (a >= 1e6) { num = (a / 1e6).toFixed(1); unit = 'M'; }
  else if (a >= 1e3) { num = (a / 1e3).toFixed(1); unit = 'K'; }
  else { num = Math.round(a).toString(); unit = ''; }
  const sign = v < 0 ? '−' : '';
  return `${sign}${num}<span class="unit"> ${unit}${unitSuffix ? ' ' + unitSuffix : ''}</span>`;
}

function fmtTime(iso) {
  if (!iso) return '–';
  const d = new Date(iso);
  const today = new Date().toDateString() === d.toDateString();
  const hm = d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
  return today ? hm : d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' }) + ' ' + hm;
}
function hoursText(h) {
  const total = Math.round(h * 60);
  return Math.floor(total / 60) + 'h ' + String(total % 60).padStart(2, '0') + 'm';
}
function sessionClock(h) {
  const s = Math.round(h * 3600);
  const p = n => String(n).padStart(2, '0');
  return `${p(Math.floor(s / 3600))}:${p(Math.floor(s / 60) % 60)}:${p(s % 60)}`;
}
const initials = name => (name || '?').split(/\s+/).map(w => w[0]).slice(0, 2).join('').toUpperCase();
const pct = v => (v * 100).toFixed(1) + ' %';

// ---------- SVG-Charts (Flächenlinie wie im Design) ----------

let gradSeq = 0;
function areaChart(points, { w = 640, h = 200 } = {}) {
  if (!points || points.length < 2)
    return '<div class="empty">Noch zu wenige Datenpunkte — nach dem ersten Abgleich füllt sich das.</div>';

  const vals = points.map(p => p.balance);
  let min = Math.min(...vals), max = Math.max(...vals);
  if (max - min < 1) { max += 1; min -= 1; }
  const pad = (max - min) * 0.08;
  min -= pad; max += pad;

  const top = 12, bottom = h - 10;
  const X = i => (i / (points.length - 1)) * w;
  const Y = v => top + (1 - (v - min) / (max - min)) * (bottom - top);

  let line = '';
  points.forEach((p, i) => { line += (i ? 'L' : 'M') + X(i).toFixed(1) + ',' + Y(p.balance).toFixed(1) + ' '; });
  const area = line + `L${w},${h} L0,${h} Z`;

  // 4 Gitterlinien mit Werten, wie im Mockup
  let grid = '', labels = '';
  for (let g = 0; g < 4; g++) {
    const gy = top + (g / 3) * (bottom - top);
    const gv = max - (g / 3) * (max - min);
    grid += `<line x1="0" y1="${gy.toFixed(1)}" x2="${w}" y2="${gy.toFixed(1)}"></line>`;
    labels += `<text x="4" y="${(gy - 5).toFixed(1)}">${fmtIsk(gv)}</text>`;
  }

  const gid = 'wfill' + (++gradSeq);
  const lx = X(points.length - 1).toFixed(1), ly = Y(points[points.length - 1].balance).toFixed(1);
  return `
<svg viewBox="0 0 ${w} ${h}">
  <defs><linearGradient id="${gid}" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="var(--color-accent)" stop-opacity="0.28"></stop>
    <stop offset="1" stop-color="var(--color-accent)" stop-opacity="0"></stop>
  </linearGradient></defs>
  <g stroke="var(--color-neutral-800)" stroke-width="1">${grid}</g>
  <g font-size="10" fill="var(--color-neutral-400)">${labels}</g>
  <path d="${area}" fill="url(#${gid})"></path>
  <path d="${line}" fill="none" stroke="var(--color-accent)" stroke-width="2"></path>
  <circle cx="${lx}" cy="${ly}" r="4" fill="var(--color-accent)"></circle>
  <circle cx="${lx}" cy="${ly}" r="9" fill="var(--color-accent)" opacity="0.2"></circle>
</svg>`;
}

function donut(parts) {
  const total = parts.reduce((a, p) => a + p.value, 0);
  if (total <= 0) return '<div class="empty">Noch keine Einnahmen in den letzten 7 Tagen erfasst.</div>';

  const colors = ['var(--color-accent)', 'var(--color-accent-700)', 'var(--color-neutral-500)', 'var(--color-neutral-700)'];
  const C = 2 * Math.PI * 48;
  let off = 0, segs = '', legend = '';
  parts.forEach((p, i) => {
    if (p.value <= 0) return;
    const len = (p.value / total) * C;
    segs += `<circle cx="60" cy="60" r="48" fill="none" stroke="${colors[i % colors.length]}" stroke-width="12"
      stroke-dasharray="${Math.max(len - 2, 1).toFixed(1)} ${C.toFixed(1)}" stroke-dashoffset="${(-off).toFixed(1)}"
      stroke-linecap="round" transform="rotate(-90 60 60)"></circle>`;
    legend += `<div><span class="sw" style="background:${colors[i % colors.length]}"></span>${esc(p.label)}
      <span class="pct">${fmtIsk(p.value)} · ${Math.round(p.value / total * 100)}%</span></div>`;
    off += len;
  });

  return `
<svg viewBox="0 0 120 120">
  <circle cx="60" cy="60" r="48" fill="none" stroke="var(--color-neutral-800)" stroke-width="12"></circle>
  ${segs}
  <text x="60" y="57" text-anchor="middle" font-size="16" fill="var(--color-text)" font-family="Chakra Petch">${fmtIsk(total)}</text>
  <text x="60" y="73" text-anchor="middle" font-size="9" fill="var(--color-neutral-400)">7-Tage-Einnahmen</text>
</svg>
<div class="split-legend">${legend}</div>`;
}

// ---------- Status / Sidebar ----------

async function loadStatus() {
  S.status = await api('/api/status');
  const st = S.status;

  $('redirectUri').textContent = st.redirectUri;
  $('scopeList').innerHTML = st.scopes.map(s => `<span class="tag tag-neutral">${esc(s)}</span>`).join('');
  // Eingabefelder nur füllen, solange der Nutzer sie nicht angefasst hat — sonst
  // überschreibt der 30s-Hintergrund-Refresh halb getippte Eingaben
  for (const id of ['clientId', 'contact', 'overlayTextPath']) {
    const el = $(id);
    if (!el.dataset.dirty && document.activeElement !== el)
      el.value = st[id] || '';
  }
  $('dbPath').textContent = st.dbPath || '–';
  $('syncState').textContent = st.syncBusy ? 'gleicht ab …' : (st.syncMessage || '–');

  const chars = st.characters || [];
  if (chars.length && (!S.charId || !chars.some(c => c.characterId === S.charId)))
    S.charId = chars[0].characterId;

  const cur = chars.find(c => c.characterId === S.charId);
  $('charAvatar').textContent = cur ? initials(cur.name) : '–';
  $('charName').textContent = cur ? cur.name : 'Kein Charakter';
  $('charSub').textContent = cur ? fmtIsk(cur.balance) + ' ISK' : 'bitte anmelden';

  $('charPop').innerHTML = chars.map(c => `
    <div class="char-opt" data-pick="${c.characterId}">
      <div class="mini">${esc(initials(c.name))}</div>
      <div><div>${esc(c.name)}</div><div class="sub">${fmtIsk(c.balance)} ISK</div></div>
    </div>`).join('') || '<div class="char-opt"><div class="sub">Noch kein Charakter angemeldet</div></div>';

  // LIVE-Tag: verbunden, wenn eingerichtet und der letzte Abgleich fehlerfrei war
  const errs = (st.errors || []).filter(e => e.error);
  const live = st.configured && chars.length > 0 && errs.length === 0;
  $('liveTag').className = 'tag ' + (live ? 'tag-accent' : 'tag-off');
  $('liveTag').style.marginLeft = 'auto';
  $('liveTag').textContent = live ? 'LIVE · ESI connected' : (st.configured ? 'ESI · Fehler beim Abgleich' : 'NICHT VERBUNDEN');

  $('dashBanners').innerHTML =
    (!st.configured || chars.length === 0
      ? `<div class="banner info">Noch nicht eingerichtet — unter <b>Settings</b> die Client-ID eintragen und einen Charakter anmelden.</div>` : '') +
    errs.map(e => `<div class="banner err"><b>${esc(e.resource)}</b>: ${esc(e.error)}</div>`).join('');

  // Timing-Karte: aktive Glättungsstufe markieren
  document.querySelectorAll('#rateHoldSeg .seg-opt').forEach(x =>
    x.classList.toggle('on', Number(x.dataset.hold) === (st.rateHold || 300)));

  $('charAdmin').innerHTML = chars.map(c => `
    <div class="row" style="border:1px solid var(--color-neutral-800);border-radius:8px;padding:10px 12px">
      <div class="char-avatar" style="width:34px;height:34px">${esc(initials(c.name))}</div>
      <div style="flex:1;min-width:0">
        <div style="font-size:13px">${esc(c.name)}</div>
        <div class="head-note">wallet · orders · mining · jobs · assets${c.hasKillScope ? ' · killmails' : ''}</div>
      </div>
      ${c.hasKillScope
        ? '<span class="tag tag-accent">Connected</span>'
        : '<span class="tag tag-off" title="Für Kills: Scope bei CCP ergänzen, dann neu anmelden">Kills-Scope fehlt</span>'}
      <button class="btn btn-danger" style="font-size:12px" data-forget="${c.characterId}">Revoke</button>
    </div>`).join('') || '<div class="empty">Noch kein Charakter verbunden.</div>';

  const url = `${location.origin}/overlay?charId=${S.charId ?? '<ID>'}`;
  $('srcUrl').value = url;
  $('btnCsv').href = S.charId ? `/api/wallet/export.csv?charId=${S.charId}` : '#';
}

// ---------- Aktualisierungs-Countdown ----------

// Serverzeit-Versatz merken, damit der Countdown nicht an der PC-Uhr hängt
let syncInfo = null, clockOffset = 0;

async function loadSyncInfo() {
  if (!S.charId) { syncInfo = null; return; }
  try {
    syncInfo = await api(`/api/sync-info?charId=${S.charId}`);
    clockOffset = new Date(syncInfo.serverNow).getTime() - Date.now();
  } catch (e) { syncInfo = null; }
}

function renderSyncNote() {
  const el = $('updNote');
  if (!syncInfo || !syncInfo.walletLast) { el.textContent = ''; return; }
  const now = Date.now() + clockOffset;
  const rest = Math.round((new Date(syncInfo.walletNext).getTime() - now) / 1000);
  const stand = new Date(syncInfo.walletLast).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  const cd = rest > 0 ? `Update in ${rest}s` : 'Update läuft gleich …';
  let jr = '';
  if (syncInfo.journalLast) {
    const jRest = Math.round((new Date(syncInfo.journalNext).getTime() - now) / 60000);
    jr = ` · Journal ${jRest > 0 ? 'in ' + jRest + ' min' : 'gleich'}`;
  }
  el.textContent = `Stand ${stand} · ${cd}${jr}`;
}
setInterval(renderSyncNote, 1000);

// ---------- Dashboard ----------

async function loadDash() {
  if (!S.charId) { setSessionUi(null); return; }

  const [sum, sess] = await Promise.all([
    api(`/api/wallet/summary?charId=${S.charId}`),
    api(`/api/session?charId=${S.charId}`),
  ]);
  loadSyncInfo().then(renderSyncNote);
  S.session = sess;

  $('stBalance').innerHTML = bigIsk(sum.balance, 'ISK');
  $('stBalanceSub').innerHTML = sum.net24 === 0
    ? 'heute unverändert'
    : `<i class="ph ph-trend-${sum.net24 >= 0 ? 'up' : 'down'}"></i> ${signed(sum.net24)} in 24h`;
  $('stBalanceSub').className = 'stat-sub ' + (sum.net24 > 0 ? 'pos' : '');

  setSessionUi(sess);

  const mining = await api(`/api/mining/today?charId=${S.charId}`);
  $('stMining').innerHTML = mining.total > 0 ? bigIsk(mining.total) : '–';
  $('stMiningSub').textContent = mining.total > 0
    ? mining.units.toLocaleString('de-DE') + ' Einheiten · heute' : 'heute · Marktwert';

  $('oreList').innerHTML = mining.ores.length ? mining.ores.map((o, _, arr) => {
    const p = Math.round(o.value / arr[0].value * 100);
    return `<div>
      <div class="ore-head"><span>${esc(o.name)}</span><span class="amt">${o.qty.toLocaleString('de-DE')} Stk</span><span class="val">${fmtIsk(o.value)}</span></div>
      <div class="bar"><div class="bar-fill" style="width:${p}%"></div></div>
    </div>`;
  }).join('') : '<div class="empty">Heute noch nichts geschürft.</div>';

  const split = await api(`/api/stats/split?charId=${S.charId}`);
  $('splitBox').innerHTML = donut(split);

  await loadWalletChart('walletChart', S.dashRange, true);

  const recent = await api(`/api/journal/recent?charId=${S.charId}&limit=5`);
  const icon = rt =>
    rt.startsWith('market') ? 'ph-storefront' :
    rt.startsWith('bounty') || rt === 'ess_escrow_transfer' ? 'ph-crosshair' :
    rt.includes('mission') ? 'ph-flag' :
    rt === 'insurance' ? 'ph-shield-check' :
    rt.includes('fee') || rt.includes('tax') ? 'ph-receipt' : 'ph-coins';
  $('recentList').innerHTML = recent.length ? recent.map(e => `
    <div class="list-row">
      <i class="ph-fill ${icon(e.refType)}"></i>
      <div class="list-main">
        <div class="list-title">${esc(e.label)}</div>
        <div class="list-sub">${fmtTime(e.time)}${e.desc ? ' · ' + esc(e.desc) : ''}</div>
      </div>
      <div class="list-val ${cls(e.amount)}">${signed(e.amount)}</div>
    </div>`).join('') : '<div class="empty">Noch keine Buchungen.</div>';
  $('recentNote').textContent = 'Journal · bis zu 1h verzögert';
}

function setSessionUi(sess) {
  const active = sess && sess.active;
  $('btnSession').textContent = active ? 'Session beenden' : 'Session starten';
  $('btnSession').className = 'btn ' + (active ? 'btn-danger' : 'btn-primary');
  $('dashNote').textContent = active
    ? `Session ${sessionClock(sess.hours)} · seit ${fmtTime(sess.startedUtc)}`
    : 'keine laufende Session';

  $('stRate').innerHTML = active ? bigIsk(sess.iskPerHour) : '–';
  $('stSession').innerHTML = active ? bigIsk(sess.delta) : '–';
  $('stSession').className = 'stat-value ' + (active ? cls(sess.delta) : '');
  $('stSessionSub').textContent = active ? full(sess.delta) : 'keine Session';
}

async function loadWalletChart(el, range, withFoot) {
  const pts = await api(`/api/wallet/series?charId=${S.charId}&range=${range}`);
  $(el).innerHTML = areaChart(pts, { w: el === 'walletChart30' ? 900 : 640, h: el === 'walletChart30' ? 180 : 200 });
  if (el === 'walletChart') $('walletChartTitle').textContent = 'WALLET · ' + range.toUpperCase();

  if (withFoot && pts.length >= 2) {
    const net = pts[pts.length - 1].balance - pts[0].balance;
    let peak = pts[0];
    for (const p of pts) if (p.balance > peak.balance) peak = p;
    $('walletChartFoot').innerHTML = `
      <span><span class="${net >= 0 ? 'acc' : 'neg'}">${net >= 0 ? '▲' : '▼'} ${fmtIsk(net)}</span> Veränderung</span>
      <span>Hoch ${fmtIsk(peak.balance)} · ${fmtTime(peak.t)}</span>
      <span>${pts.length - 1} Buchungen</span>`;
  } else if (withFoot) {
    $('walletChartFoot').innerHTML = '';
  }
}

// ---------- Wallet-Screen ----------

async function loadWallet() {
  if (!S.charId) return;
  const sum = await api(`/api/wallet/summary?charId=${S.charId}`);
  $('wBalance').innerHTML = bigIsk(sum.balance, 'ISK');
  $('wIn').textContent = signed(sum.in24);
  $('wOut').textContent = '−' + fmtIsk(sum.out24);

  await loadWalletChart('walletChart30', '30d', false);

  const rows = await api(`/api/journal/recent?charId=${S.charId}&limit=40`);
  $('journalRows').innerHTML = rows.length ? rows.map(e => `
    <tr>
      <td class="head-note" style="white-space:nowrap">${fmtTime(e.time)}</td>
      <td><span class="tag tag-neutral">${esc(e.label)}</span></td>
      <td style="font-size:13px">${esc(e.desc || '—')}</td>
      <td class="num mono ${cls(e.amount)}">${signed(e.amount)}</td>
      <td class="num head-note">${fmtIsk(e.balance)}</td>
    </tr>`).join('') : '<tr><td colspan="5" class="empty">Noch keine Buchungen.</td></tr>';
}

// ---------- Reports (bestehende Module im neuen Kleid) ----------

const tile = (label, value, valueCls, hint) => `
  <div class="card stat">
    <span class="stat-label">${label}</span>
    <div class="stat-value sm ${valueCls}">${value}</div>
    <div class="stat-sub">${hint}</div>
  </div>`;
const tileGrid = inner => `<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:14px;margin-bottom:14px">${inner}</div>`;

async function loadReports() {
  if (!S.charId) return;
  S.report = await api(`/api/report?charId=${S.charId}&range=${S.repRange}`);
  renderTab();
}

function renderTab() {
  const r = S.report;
  if (!r) return;
  document.querySelectorAll('.tabpanel').forEach(p => p.classList.add('hidden'));
  $('tab-' + S.tab).classList.remove('hidden');
  if (S.tab === 'trading') $('tab-trading').innerHTML = renderTrading(r.trading);
  if (S.tab === 'ratting') $('tab-ratting').innerHTML = renderRatting(r.ratting);
  if (S.tab === 'mining') $('tab-mining').innerHTML = renderMining(r.mining);
  if (S.tab === 'industry') $('tab-industry').innerHTML = renderIndustry(r.industry);
  if (S.tab === 'kills') loadKills();
  if (S.tab === 'sessions') loadSessionHistory();
}

async function loadKills() {
  const k = await api(`/api/kills?charId=${S.charId}&range=${S.repRange}`);
  const me = S.status.characters.find(c => c.characterId === S.charId);
  const scopeHint = me && !me.hasKillScope ? `
    <div class="banner info">Für Kill-Daten fehlt deinem Login noch die Berechtigung
    <code>esi-killmails.read_killmails.v1</code>. Auf developers.eveonline.com die Permission
    zur Anwendung hinzufügen, dann unter Settings den Charakter neu anmelden.</div>` : '';

  const rows = k.rows.map(x => `
    <tr>
      <td class="head-note" style="white-space:nowrap">${fmtTime(x.time)}</td>
      <td><span class="tag ${x.isLoss ? 'tag-off' : 'tag-accent'}">${x.isLoss ? 'Verlust' : 'Kill'}</span></td>
      <td>${esc(x.ship)}${x.victim && !x.isLoss ? ' <span class="head-note">— ' + esc(x.victim) + '</span>' : ''}</td>
      <td class="head-note">${esc(x.system)}</td>
      <td class="num mono ${x.isLoss ? 'neg' : 'acc'}">${x.value > 0 ? fmtIsk(x.value) : '—'}</td>
      <td><a href="https://zkillboard.com/kill/${x.killmailId}/" target="_blank" rel="noopener">zKill →</a></td>
    </tr>`).join('');

  $('tab-kills').innerHTML = scopeHint + tileGrid(
    tile('KILLS', String(k.killCount), 'acc', 'im Zeitraum') +
    tile('ZERSTÖRT', fmtIsk(k.destroyed), 'acc', 'Wert laut zKillboard') +
    tile('VERLUSTE', String(k.lossCount), k.lossCount > 0 ? 'neg' : '', 'eigene Schiffe') +
    tile('VERLOREN', '−' + fmtIsk(k.lost), k.lost > 0 ? 'neg' : '', 'Wert laut zKillboard')) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>Zeit</th><th>Typ</th><th>Schiff</th><th>System</th><th class="num">Wert</th><th></th></tr></thead>
      <tbody>${rows || '<tr><td colspan="6" class="empty">Keine Kills oder Verluste im Zeitraum.</td></tr>'}</tbody>
    </table></div></div>`;
}

function renderTrading(t) {
  const warn = t.unmatchedQty > 0 ? `
    <div class="banner info">Bei ${t.unmatchedQty.toLocaleString('de-DE')} verkauften Einheiten (${t.unmatchedTypes} Typen)
    ist kein Einkaufspreis bekannt — der Kauf liegt vor Beginn der Datensammlung. Der Gewinn ist dort zu hoch ausgewiesen.</div>` : '';
  const rows = t.items.slice(0, 200).map(i => `
    <tr><td>${esc(i.name)}</td>
      <td class="num">${i.quantitySold.toLocaleString('de-DE')}</td>
      <td class="num mono">${fmtIsk(i.revenue)}</td>
      <td class="num mono">${fmtIsk(i.cogs)}</td>
      <td class="num mono ${cls(i.gross)}">${signed(i.gross)}</td>
      <td class="num">${pct(i.margin)}</td></tr>`).join('');
  return warn + tileGrid(
    tile('UMSATZ', fmtIsk(t.revenue), 'acc', full(t.revenue)) +
    tile('WARENEINSATZ', fmtIsk(t.cogs), '', 'nach FIFO') +
    tile('ROHERTRAG', signed(t.grossProfit), cls(t.grossProfit), 'vor Gebühren') +
    tile('BROKER + STEUER', '−' + fmtIsk(t.brokerFees + t.salesTax), 'neg', `Broker ${fmtIsk(t.brokerFees)} · Steuer ${fmtIsk(t.salesTax)}`) +
    tile('NETTOGEWINN', signed(t.netProfit), cls(t.netProfit), full(t.netProfit))) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>Item</th><th class="num">Verkauft</th><th class="num">Umsatz</th><th class="num">Einkauf</th><th class="num">Gewinn</th><th class="num">Marge</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="6" class="empty">Keine Verkäufe im Zeitraum.</td></tr>'}</tbody>
    </table></div></div>`;
}

function renderRatting(r) {
  const rows = r.lines.map(l => `
    <tr><td>${esc(l.category)}</td><td class="num">${l.count.toLocaleString('de-DE')}</td>
    <td class="num mono ${cls(l.amount)}">${signed(l.amount)}</td></tr>`).join('');
  return tileGrid(tile('SUMME', signed(r.total), cls(r.total), full(r.total))) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>Kategorie</th><th class="num">Buchungen</th><th class="num">Betrag</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="3" class="empty">Keine Einträge im Zeitraum.</td></tr>'}</tbody>
    </table></div></div>`;
}

function renderMining(m) {
  const rows = m.lines.map(l => `
    <tr><td>${esc(l.name)}</td><td class="num">${l.quantity.toLocaleString('de-DE')}</td>
    <td class="num">${Math.round(l.unitPrice).toLocaleString('de-DE')}</td>
    <td class="num mono acc">${fmtIsk(l.value)}</td></tr>`).join('');
  return `<div class="banner info">Bewertet mit CCPs globalem Durchschnittspreis, nicht mit dem Jita-Kurs.</div>` +
    tileGrid(
      tile('MARKTWERT', fmtIsk(m.totalValue), 'acc', full(m.totalValue)) +
      tile('MENGE', m.totalUnits.toLocaleString('de-DE'), '', 'Einheiten')) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>Erz</th><th class="num">Menge</th><th class="num">Stückpreis</th><th class="num">Wert</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="4" class="empty">Nichts geschürft im Zeitraum.</td></tr>'}</tbody>
    </table></div></div>`;
}

function renderIndustry(ind) {
  const rows = ind.jobs.slice(0, 200).map(j => `
    <tr><td>${esc(j.product)}</td><td class="num">${j.runs.toLocaleString('de-DE')}</td>
    <td class="num mono neg">${fmtIsk(j.cost)}</td>
    <td class="num mono acc">${fmtIsk(j.outputValue)}</td>
    <td class="num mono ${cls(j.outputValue - j.cost)}">${signed(j.outputValue - j.cost)}</td>
    <td><span class="tag tag-neutral">${esc(j.status)}</span></td>
    <td class="head-note">${fmtTime(j.endUtc)}</td></tr>`).join('');
  return `<div class="banner info">Produktwert geschätzt (Stückzahl × Durchschnittspreis). Materialkosten liefert ESI nicht mit.</div>` +
    tileGrid(
      tile('JOBKOSTEN', '−' + fmtIsk(ind.totalCost), 'neg', full(ind.totalCost)) +
      tile('PRODUKTWERT', fmtIsk(ind.totalOutputValue), 'acc', 'geschätzt') +
      tile('SALDO', signed(ind.balance), cls(ind.balance), ind.jobCount + ' Jobs')) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>Produkt</th><th class="num">Runs</th><th class="num">Kosten</th><th class="num">Produktwert</th><th class="num">Saldo</th><th>Status</th><th>Ende</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="7" class="empty">Keine Jobs im Zeitraum.</td></tr>'}</tbody>
    </table></div></div>`;
}

async function loadSessionHistory() {
  const list = await api(`/api/session/history?charId=${S.charId}`);
  const rows = list.map(s => `
    <tr><td>${esc(s.label || '—')}</td><td class="head-note">${fmtTime(s.startedUtc)}</td>
    <td>${hoursText(s.hours)}</td>
    <td class="num mono ${cls(s.delta)}">${signed(s.delta)}</td>
    <td class="num mono ${cls(s.iskPerHour)}">${signed(s.iskPerHour)}/h</td></tr>`).join('');
  $('tab-sessions').innerHTML = `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>Bezeichnung</th><th>Start</th><th>Dauer</th><th class="num">ISK</th><th class="num">ISK/h</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="5" class="empty">Noch keine abgeschlossenen Sessions.</td></tr>'}</tbody>
    </table></div></div>`;
}

// ---------- Overlay-Screen ----------

const METRIC_DEFS = [
  { key: 'time', name: 'Timer', label: 'SESSION' },
  { key: 'session', name: 'Session-ISK', label: 'SESSION-ISK' },
  { key: 'rate', name: 'ISK/h', label: 'ISK/STUNDE' },
  { key: 'bounties', name: 'Bounties', label: 'BOUNTIES CA.' },
  { key: 'missions', name: 'Missionen', label: 'MISSIONEN CA.' },
  { key: 'kills', name: 'Kills', label: 'KILLS' },
  { key: 'destroyed', name: 'Zerstört', label: 'ZERSTÖRT' },
  { key: 'mining', name: 'Mining', label: 'MINING-WERT' },
  { key: 'wallet', name: 'Wallet', label: 'WALLET' },
];
const activeMetrics = () =>
  ((S.status && S.status.overlayMetrics) || 'time,session,rate,mining').split(',').filter(Boolean);

async function loadOverlayScreen() {
  if (!S.charId) return;
  const d = await api(`/api/overlay-data?charId=${S.charId}`);
  $('pvChar').textContent = d.name || '–';

  const act = activeMetrics();
  const cell = k => {
    let text = '–', cls = '';
    if (k === 'wallet') text = fmtIsk(d.balance);
    if (k === 'time' && d.active) text = sessionClock(d.hours);
    if (k === 'rate' && d.active) { text = signed(d.iskPerHour); cls = 'accent'; }
    if (k === 'session' && d.active) { text = signed(d.delta); cls = d.delta > 0 ? 'pos' : d.delta < 0 ? 'neg' : ''; }
    if (k === 'bounties' && d.active && d.bounties > 0) { text = fmtIsk(d.bounties); cls = 'pos'; }
    if (k === 'missions' && d.active && d.missions > 0) { text = fmtIsk(d.missions); cls = 'pos'; }
    if (k === 'kills' && d.active) text = String(d.kills || 0);
    if (k === 'destroyed' && d.active && d.destroyed > 0) { text = fmtIsk(d.destroyed); cls = 'accent'; }
    if (k === 'mining' && d.active && d.mining > 0) text = fmtIsk(d.mining);
    const def = METRIC_DEFS.find(m => m.key === k);
    return `<div class="pw-cell"><div class="pw-label">${def.label}</div><div class="pw-value ${cls}">${text}</div></div>`;
  };
  // wie im Widget: bis 4 Kacheln eine Reihe, ab 5 zwei ausgewogene Reihen
  const rows = act.length <= 4 ? [act] : [act.slice(0, Math.ceil(act.length / 2)), act.slice(Math.ceil(act.length / 2))];
  $('pvRow').innerHTML = rows.map(r =>
    '<div class="pw-row">' + r.map(cell).join('<div class="pw-sep"></div>') + '</div>').join('');

  $('metricToggles').innerHTML = METRIC_DEFS.map(m =>
    `<span class="tag ${act.includes(m.key) ? 'tag-accent' : 'tag-off'} click" data-metric="${m.key}">${m.name}</span>`).join('');
}

$('metricToggles').addEventListener('click', async e => {
  const t = e.target.closest('[data-metric]');
  if (!t) return;
  const act = activeMetrics();
  const key = t.dataset.metric;
  const next = act.includes(key) ? act.filter(k => k !== key) : [...act, key];
  if (!next.length) { alert('Mindestens ein Wert muss angezeigt bleiben.'); return; }
  try {
    await api('/api/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ overlayMetrics: next.join(',') }),
    });
    // Reihenfolge wie der Server normieren (feste Kachel-Reihenfolge)
    S.status.overlayMetrics = METRIC_DEFS.map(m => m.key).filter(k => next.includes(k)).join(',');
    loadOverlayScreen();
  } catch (err) { alert('Speichern fehlgeschlagen: ' + err.message); }
});

// ---------- Navigation & Ereignisse ----------

const loaders = { dash: loadDash, wallet: loadWallet, reports: loadReports, overlay: loadOverlayScreen, settings: async () => {} };

async function showScreen(key) {
  S.screen = key;
  document.querySelectorAll('.screen').forEach(s => s.classList.add('hidden'));
  $('screen-' + key).classList.remove('hidden');
  document.querySelectorAll('#sideNav .nav-item').forEach(n => n.classList.toggle('on', n.dataset.screen === key));
  try { await loaders[key](); } catch (e) { console.error(e); }
}

$('sideNav').addEventListener('click', e => {
  const item = e.target.closest('.nav-item');
  if (item) showScreen(item.dataset.screen);
});

$('charBox').addEventListener('click', () => $('charPop').classList.toggle('hidden'));
document.addEventListener('click', e => {
  if (!e.target.closest('.char-wrap')) $('charPop').classList.add('hidden');
});

$('charPop').addEventListener('click', async e => {
  const opt = e.target.closest('[data-pick]');
  if (!opt) return;
  S.charId = Number(opt.dataset.pick);
  $('charPop').classList.add('hidden');
  await loadStatus();
  await loaders[S.screen]();
});

$('dashRange').addEventListener('click', e => {
  const b = e.target.closest('.seg-opt'); if (!b) return;
  S.dashRange = b.dataset.r;
  document.querySelectorAll('#dashRange .seg-opt').forEach(x => x.classList.toggle('on', x === b));
  loadWalletChart('walletChart', S.dashRange, true);
});

$('repRange').addEventListener('click', e => {
  const b = e.target.closest('.seg-opt'); if (!b) return;
  S.repRange = b.dataset.r;
  document.querySelectorAll('#repRange .seg-opt').forEach(x => x.classList.toggle('on', x === b));
  loadReports();
});

$('tabbar').addEventListener('click', e => {
  const b = e.target.closest('.seg-opt'); if (!b) return;
  S.tab = b.dataset.tab;
  document.querySelectorAll('#tabbar .seg-opt').forEach(x => x.classList.toggle('on', x === b));
  renderTab();
});

$('btnSession').addEventListener('click', async () => {
  if (!S.charId) { showScreen('settings'); return; }
  if (S.session && S.session.active)
    await api(`/api/session/stop?charId=${S.charId}`, { method: 'POST' });
  else
    await api(`/api/session/start?charId=${S.charId}&label=`, { method: 'POST' });
  loadDash();
});

async function runSync(btn) {
  btn.disabled = true;
  try { await api('/api/sync', { method: 'POST' }); }
  catch (e) { alert('Abgleich fehlgeschlagen: ' + e.message); }
  btn.disabled = false;
  await loadStatus();
  await loaders[S.screen]();
}
$('btnSync').addEventListener('click', e => runSync(e.currentTarget));
$('btnRefresh').addEventListener('click', e => runSync(e.currentTarget));

// Angefasste Felder markieren, damit der Hintergrund-Refresh sie in Ruhe lässt
for (const id of ['clientId', 'contact', 'overlayTextPath'])
  $(id).addEventListener('input', e => { e.target.dataset.dirty = '1'; });

$('btnSaveConfig').addEventListener('click', async () => {
  try {
    await api('/api/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ clientId: $('clientId').value, contact: $('contact').value }),
    });
    delete $('clientId').dataset.dirty;
    delete $('contact').dataset.dirty;
    await loadStatus();
  } catch (e) { alert('Speichern fehlgeschlagen: ' + e.message); }
});

$('btnSaveTxt').addEventListener('click', async () => {
  try {
    await api('/api/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ overlayTextPath: $('overlayTextPath').value }),
    });
    delete $('overlayTextPath').dataset.dirty;
  } catch (e) { alert('Speichern fehlgeschlagen: ' + e.message); }
});

$('btnCopyUrl').addEventListener('click', async e => {
  try { await navigator.clipboard.writeText($('srcUrl').value); e.currentTarget.innerHTML = '<i class="ph ph-check"></i> kopiert'; }
  catch (err) { }
  setTimeout(() => { $('btnCopyUrl').innerHTML = '<i class="ph ph-copy"></i> Copy source URL'; }, 1600);
});

$('btnScene').addEventListener('click', () => $('sceneBox').classList.remove('checker'));
$('btnChecker').addEventListener('click', () => $('sceneBox').classList.add('checker'));

$('rateHoldSeg').addEventListener('click', async e => {
  const b = e.target.closest('[data-hold]');
  if (!b) return;
  try {
    await api('/api/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ rateHold: b.dataset.hold }),
    });
    S.status.rateHold = Number(b.dataset.hold);
    document.querySelectorAll('#rateHoldSeg .seg-opt').forEach(x => x.classList.toggle('on', x === b));
  } catch (err) { alert('Speichern fehlgeschlagen: ' + err.message); }
});

// Login erst zulassen, wenn die Client-ID gespeichert ist — sonst navigiert das
// ganze Fenster zu einer Fehlerseite, und das ist im Desktop-Fenster eine Sackgasse.
$('btnAddChar').addEventListener('click', e => {
  if (!S.status || !S.status.configured) {
    e.preventDefault();
    alert('Bitte zuerst die Client-ID eintragen und auf „Speichern" klicken — dann anmelden.');
    $('clientId').focus();
  }
});

document.addEventListener('click', async e => {
  const copy = e.target.dataset && e.target.dataset.copy;
  if (copy) {
    try { await navigator.clipboard.writeText($(copy).textContent); e.target.textContent = 'kopiert'; } catch (err) { }
    setTimeout(() => { e.target.textContent = 'kopieren'; }, 1600);
  }
  const forget = e.target.dataset && e.target.dataset.forget;
  if (forget && confirm('Charakter wirklich trennen? Gesammelte Daten bleiben erhalten.')) {
    await api('/api/character/' + forget, { method: 'DELETE' });
    S.charId = null;
    await loadStatus();
    await loaders[S.screen]();
  }
});

// ---------- Start ----------

(async function init() {
  // #dash / #wallet / #reports / #overlay / #settings als Direkteinstieg,
  // optional mit Charakter-ID und Reports-Tab: #reports:90000001:kills
  const [forced, forcedChar, forcedTab] = location.hash.replace('#', '').split(':');
  if (forcedChar) S.charId = Number(forcedChar);
  if (forcedTab && $('tab-' + forcedTab)) {
    S.tab = forcedTab;
    document.querySelectorAll('#tabbar .seg-opt').forEach(x =>
      x.classList.toggle('on', x.dataset.tab === forcedTab));
  }
  await loadStatus();
  const needSetup = !S.status.configured || (S.status.characters || []).length === 0;
  await showScreen(loaders[forced] ? forced : (needSetup ? 'settings' : 'dash'));

  setInterval(async () => {
    try {
      await loadStatus();
      if (S.screen === 'dash' || S.screen === 'overlay') await loaders[S.screen]();
    } catch (e) { /* App evtl. gerade beendet */ }
  }, 30000);
})();
