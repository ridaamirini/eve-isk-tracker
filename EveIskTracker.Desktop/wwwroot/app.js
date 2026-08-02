'use strict';

const S = {
  screen: 'dash', charId: null, status: null, session: null,
  dashRange: '24h', repRange: '30d', tab: 'trading', report: null,
  overlayBg: 'scene',
};

const $ = id => document.getElementById(id);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

// ---------- Sprache (de/en) ----------
// Schlüssel -> [deutsch, englisch]. Statische Texte tragen data-i18n(-html/-ph)
// im HTML; dynamische Strings laufen durch t().

let LANG = 'en';
const t = k => (L[k] || [k, k])[LANG === 'en' ? 1 : 0];
const NUMLOC = () => LANG === 'en' ? 'en-US' : 'de-DE';

const L = {
  // Sidebar / Allgemein
  noChar: ['Kein Charakter', 'No character'],
  pleaseLogin: ['bitte anmelden', 'please sign in'],
  noCharYet: ['Noch kein Charakter angemeldet', 'No character signed in yet'],
  copy: ['kopieren', 'copy'],
  copied: ['kopiert', 'copied'],
  saveFail: ['Speichern fehlgeschlagen: ', 'Saving failed: '],
  syncFail: ['Abgleich fehlgeschlagen: ', 'Sync failed: '],
  syncing: ['gleicht ab …', 'syncing …'],
  save: ['Speichern', 'Save'],

  // Dashboard
  sessionStart: ['Session starten', 'Start session'],
  sessionStop: ['Session beenden', 'End session'],
  noSessionRunning: ['keine laufende Session', 'no active session'],
  since: ['seit', 'since'],
  noSession: ['keine Session', 'no session'],
  rateSub: ['Ø diese Session', 'avg. this session'],
  miningSubDefault: ['heute · Marktwert', 'today · market value'],
  units: ['Einheiten', 'units'],
  today: ['heute', 'today'],
  unchangedToday: ['heute unverändert', 'unchanged today'],
  in24h: ['in 24h', 'in 24h'],
  esiError: ['ESI · Fehler beim Abgleich', 'ESI · sync error'],
  notConnected: ['NICHT VERBUNDEN', 'NOT CONNECTED'],
  setupHint: ['Noch kein Charakter verbunden — unter <b>Settings</b> mit deinem EVE-Account anmelden.',
              'No character connected yet — sign in with your EVE account under <b>Settings</b>.'],
  kMiningToday: ['MINING · HEUTE', 'MINING · TODAY'],
  noMiningToday: ['Heute noch nichts geschürft.', 'Nothing mined today yet.'],
  noIncome7d: ['Noch keine Einnahmen in den letzten 7 Tagen erfasst.', 'No income recorded in the last 7 days.'],
  income7d: ['7-Tage-Einnahmen', '7-day income'],
  noEntries: ['Noch keine Buchungen.', 'No journal entries yet.'],
  recentNote: ['Journal · bis zu 1h verzögert', 'journal · up to 1h behind'],
  pcs: ['Stk', 'pcs'],
  chartEmpty: ['Noch zu wenige Datenpunkte — nach dem ersten Abgleich füllt sich das.',
               'Not enough data points yet — this fills up after the first sync.'],
  change: ['Veränderung', 'change'],
  peak: ['Hoch', 'peak'],
  entries: ['Buchungen', 'entries'],
  asOf: ['Stand', 'as of'],
  updIn: ['Update in', 'update in'],
  updSoon: ['Update läuft gleich …', 'update running shortly …'],
  jIn: ['Journal in', 'journal in'],
  jSoon: ['Journal gleich', 'journal shortly'],

  // Wallet-Screen
  walletSub: ['Journal · Hauptwallet', 'journal · master wallet'],
  thTime: ['Zeit', 'Time'], thType: ['Typ', 'Type'], thDesc: ['Beschreibung', 'Description'],
  thAmount: ['Betrag', 'Amount'], thBalance: ['Kontostand', 'Balance'],
  refresh: ['Aktualisieren', 'Refresh'],

  // Reports
  reportsSub: ['Handel · Ratting · Mining · Industrie', 'Trading · Ratting · Mining · Industry'],
  rToday: ['Heute', 'Today'], r7: ['7T', '7D'], r30: ['30T', '30D'], r90: ['90T', '90D'], rAll: ['Alles', 'All'],
  tabTrading: ['Handel', 'Trading'], tabIndustry: ['Industrie', 'Industry'],
  kRevenue: ['UMSATZ', 'REVENUE'], kCogs: ['WARENEINSATZ', 'COST OF GOODS'], kGross: ['ROHERTRAG', 'GROSS PROFIT'],
  kFees: ['BROKER + STEUER', 'BROKER + TAX'], kNet: ['NETTOGEWINN', 'NET PROFIT'],
  fifoBase: ['nach FIFO', 'FIFO based'], beforeFees: ['vor Gebühren', 'before fees'],
  broker: ['Broker', 'Broker'], tax: ['Steuer', 'Tax'],
  thSold: ['Verkauft', 'Sold'], thRevenue: ['Umsatz', 'Revenue'], thBuy: ['Einkauf', 'Cost'],
  thProfit: ['Gewinn', 'Profit'], thMargin: ['Marge', 'Margin'],
  noSales: ['Keine Verkäufe im Zeitraum.', 'No sales in this period.'],
  kSum: ['SUMME', 'TOTAL'], thCategory: ['Kategorie', 'Category'], thCount: ['Buchungen', 'Entries'],
  noEntriesRange: ['Keine Einträge im Zeitraum.', 'No entries in this period.'],
  miningBanner: ['Bewertet mit CCPs globalem Durchschnittspreis, nicht mit dem Jita-Kurs.',
                 'Valued at CCP\'s global average price, not Jita rates.'],
  kMarketValue: ['MARKTWERT', 'MARKET VALUE'], kQty: ['MENGE', 'QUANTITY'],
  thOre: ['Erz', 'Ore'], thQty: ['Menge', 'Quantity'], thUnitPrice: ['Stückpreis', 'Unit price'], thValue: ['Wert', 'Value'],
  noMiningRange: ['Nichts geschürft im Zeitraum.', 'Nothing mined in this period.'],
  indBanner: ['Produktwert geschätzt (Stückzahl × Durchschnittspreis). Materialkosten liefert ESI nicht mit.',
              'Output value is estimated (runs × average price). ESI does not provide material costs.'],
  kJobCost: ['JOBKOSTEN', 'JOB COST'], kOutput: ['PRODUKTWERT', 'OUTPUT VALUE'], kBalanceT: ['SALDO', 'BALANCE'],
  estimated: ['geschätzt', 'estimated'], jobs: ['Jobs', 'jobs'],
  thProduct: ['Produkt', 'Product'], thCost: ['Kosten', 'Cost'], thEnd: ['Ende', 'End'],
  noJobs: ['Keine Jobs im Zeitraum.', 'No jobs in this period.'],
  killScopeBanner: ['Für Kill-Daten fehlt deinem Login noch die Berechtigung <code>esi-killmails.read_killmails.v1</code>. Auf developers.eveonline.com die Permission zur Anwendung hinzufügen, dann unter Settings den Charakter neu anmelden.',
                    'Your login is missing the <code>esi-killmails.read_killmails.v1</code> scope. Add the permission to your application on developers.eveonline.com, then re-sign-in the character under Settings.'],
  kKills: ['KILLS', 'KILLS'], kDestroyed: ['ZERSTÖRT', 'DESTROYED'], kLosses: ['VERLUSTE', 'LOSSES'], kLost: ['VERLOREN', 'LOST'],
  inRange: ['im Zeitraum', 'in this period'], zkbValue: ['Wert laut zKillboard', 'value per zKillboard'],
  ownShips: ['eigene Schiffe', 'own ships'],
  kill: ['Kill', 'Kill'], loss: ['Verlust', 'Loss'],
  thShip: ['Schiff', 'Ship'],
  noKills: ['Keine Kills oder Verluste im Zeitraum.', 'No kills or losses in this period.'],
  thLabel: ['Bezeichnung', 'Label'], thStart: ['Start', 'Start'], thDuration: ['Dauer', 'Duration'],
  noSessions: ['Noch keine abgeschlossenen Sessions.', 'No completed sessions yet.'],

  // Overlay-Screen
  overlaySub: ['Browser-Quelle für Streamlabs/OBS · 420 × 240 px', 'Browser source for Streamlabs/OBS · 420 × 240 px'],
  transparentNote: ['Hintergrund ist in OBS voll transparent', 'Background is fully transparent in OBS'],
  metricsLabel: ['Angezeigte Werte', 'Displayed values'],
  metricsHint: ['(anklicken zum An-/Abwählen)', '(click to toggle)'],
  updLabel: ['Aktualisierung', 'Update behaviour'],
  updNote2: ['Werte ändern sich nur bei echten Kontostand-Updates (~alle 2 min); ISK/h wird höchstens alle 5 min neu berechnet, damit im Stream nichts kriecht',
             'Values only change on real wallet updates (~every 2 min); ISK/h is recalculated at most every 5 min so nothing creeps on stream'],
  txtLabel: ['Alternative: Textdatei für Textquellen', 'Alternative: text file for text sources'],
  savePath: ['Pfad speichern', 'Save path'],
  overlayNote: ['In Streamlabs als Browser-Quelle mit 420×240 anlegen. Das Widget zeigt ohne laufende Session nur den Kontostand.',
                'Add as a browser source at 420×240 in Streamlabs. Without an active session the widget only shows the wallet balance.'],
  minOneMetric: ['Mindestens ein Wert muss angezeigt bleiben.', 'At least one value must stay visible.'],

  // Settings
  addChar: ['Charakter anmelden', 'Sign in character'],
  syncNow: ['Jetzt abgleichen', 'Sync now'],
  regIntro1: ['Optional: Standardmäßig nutzt die App eine eingebaute CCP-Registrierung — anmelden funktioniert sofort. Wer lieber eine eigene verwenden will: Anwendung auf',
              'Optional: the app ships with a built-in CCP registration — signing in just works. If you prefer your own: create an application at'],
  regIntro2: ['anlegen (Connection Type „Authentication & API Access"). Callback URL exakt:',
              '(connection type "Authentication & API Access"). Callback URL exactly:'],
  clientIdNote: ['Leer lassen = eingebaute Standard-App. Nur ausfüllen, wenn du deine eigene CCP-Anwendung nutzen willst.',
                 'Leave empty to use the built-in default app. Only fill in to use your own CCP application.'],
  phClientId: ['leer = Standard-App (eingebaut)', 'empty = default app (built-in)'],
  charLabel: ['Charakter im Widget', 'Character in widget'],
  charToggleName: ['Portrait & Name', 'Portrait & name'],
  autoStopLabel: ['Session automatisch beenden', 'Auto-end session'],
  autoStopName: ['Wenn der EVE-Client schließt', 'When the EVE client closes'],
  autoStopNote: ['Beendet offene Sessions, wenn der EVE-Client (exefile.exe) beim App-Start nicht läuft oder im Betrieb länger als 3 Minuten geschlossen ist.',
                 'Ends open sessions when the EVE client (exefile.exe) is not running at app start, or has been closed for more than 3 minutes.'],
  contactLabel: ['Kontakt (E-Mail oder Charaktername)', 'Contact (e-mail or character name)'],
  regNote: ['Kein Passwort, kein Client-Secret: der Login läuft über CCPs eigene Seite, hier liegt nur ein Windows-verschlüsseltes Zugriffstoken.',
            'No password, no client secret: sign-in happens on CCP\'s own page; only a Windows-encrypted access token is stored here.'],
  kTiming: ['TIMING & AKTUALISIERUNG', 'TIMING & UPDATES'],
  timingIntro: ['CCP hält jede API-Antwort eine feste Zeit im Cache — häufigeres Abfragen liefert bis dahin exakt dieselben Daten. Die App prüft alle 20 Sekunden, ob etwas Neues bereitliegt, und holt es dann sofort:',
                'CCP caches every API response for a fixed time — polling more often returns exactly the same data. The app checks every 20 seconds whether something new is available and fetches it immediately:'],
  ttData: ['Daten', 'Data'], ttFresh: ['Neu von CCP', 'Fresh from CCP'], ttUsed: ['Steckt in', 'Used in'],
  tt1a: ['Kontostand', 'Wallet balance'], tt1b: ['alle 2 min', 'every 2 min'], tt1c: ['Session-Zähler, Widget, Kennzahlen', 'session counter, widget, stats'],
  tt2a: ['Journal (Buchungen)', 'Journal (entries)'], tt2b: ['stündlich', 'hourly'], tt2c: ['Aufschlüsselung, Charts, Ratting, Recent Activity', 'breakdown, charts, ratting, recent activity'],
  tt3a: ['Mining-Ledger', 'Mining ledger'], tt3b: ['alle 10 min', 'every 10 min'], tt3c: ['Mining-Karten, Widget-Kachel', 'mining cards, widget tile'],
  tt4a: ['Produktionsjobs', 'Industry jobs'], tt4b: ['alle 5 min', 'every 5 min'], tt4c: ['Industrie-Report', 'industry report'],
  tt5a: ['Marktorders', 'Market orders'], tt5b: ['alle 20 min', 'every 20 min'], tt5c: ['Handels-Report', 'trading report'],
  tt6a: ['Marktpreise', 'Market prices'], tt6b: ['stündlich', 'hourly'], tt6c: ['Mining-/Industrie-Bewertung', 'mining/industry valuation'],
  rateHoldLabel: ['ISK/h-Glättung im Stream-Widget', 'ISK/h smoothing in the stream widget'],
  rateHoldNote: ['Das Widget übernimmt Zahlen grundsätzlich nur, wenn CCP wirklich neue Daten geliefert hat — dazwischen steht die Anzeige still. Einzige Ausnahme ist <b>ISK/h</b>: dieser Wert ist „verdientes ISK geteilt durch verstrichene Zeit" und würde ständig weiterkriechen, weil die Zeit ja immer weiterläuft. Deshalb wird er nur im hier gewählten Abstand neu berechnet (bei Session-Start/-Stopp sofort). Kürzer = aktueller, länger = ruhigeres Bild im Stream.',
                 'The widget only applies numbers when CCP actually delivered new data — in between, the display stands still. The one exception is <b>ISK/h</b>: it is "ISK earned divided by elapsed time" and would creep constantly because time keeps running. It is therefore recalculated only at the interval chosen here (immediately on session start/stop). Shorter = more current, longer = calmer picture on stream.'],
  killScopeMissing: ['Kills-Scope fehlt', 'Kill scope missing'],
  confirmRevoke: ['Charakter wirklich trennen? Gesammelte Daten bleiben erhalten.',
                  'Really disconnect this character? Collected data is kept.'],
  aboutData: ['Alle Daten lokal in', 'All data stored locally in'],
  langLabel: ['Sprache / Language', 'Language / Sprache'],
  loginFirst: ['Bitte zuerst die Client-ID eintragen und auf „Speichern" klicken — dann anmelden.',
               'Please enter the client ID and click "Save" first — then sign in.'],
};

// Journal-Kategorien kommen vom Server auf Deutsch; für EN hier übersetzt
const REF_EN = {
  'Bounties': 'Bounties', 'ESS-Auszahlung': 'ESS payout', 'Missionsbelohnung': 'Mission reward',
  'Missions-Zeitbonus': 'Mission time bonus', 'Markt (Handel)': 'Market (trade)', 'Broker-Gebühr': 'Broker fee',
  'Verkaufssteuer': 'Sales tax', 'Versicherung': 'Insurance', 'Vertrag (Preis)': 'Contract (price)',
  'Vertrag (Belohnung)': 'Contract (reward)', 'Vertrag (Gebühr)': 'Contract (fee)',
  'Industrie-Jobkosten': 'Industry job cost', 'Reprocessing-Steuer': 'Reprocessing tax',
  'Corp-Auszahlung': 'Corp payout', 'LP-Store': 'LP store', 'Order-Hinterlegung': 'Market escrow',
  'Spielerspende': 'Player donation', 'Corp-Abbuchung': 'Corp withdrawal',
  'PI-Importsteuer': 'PI import tax', 'PI-Exportsteuer': 'PI export tax', 'Skill-Kauf': 'Skill purchase',
  'Klon-Gebühr': 'Clone fee', 'Sprungtor-Gebühr': 'Gate jump fee',
  'Daily-Goal-Belohnung': 'Daily goal payout', 'Freelance-Job': 'Freelance job',
  'Missionen': 'Missions', 'Verträge': 'Contracts', 'Abzüge (Steuern/Gebühren)': 'Deductions (taxes/fees)',
  'Sonstiges': 'Other', 'Bounties & Loot': 'Bounties & loot',
};
const refLabel = de => LANG === 'en' ? (REF_EN[de] || de) : de;

function applyLang() {
  document.documentElement.lang = LANG;
  document.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = t(el.dataset.i18n); });
  document.querySelectorAll('[data-i18n-html]').forEach(el => { el.innerHTML = t(el.dataset.i18nHtml); });
  document.querySelectorAll('[data-i18n-ph]').forEach(el => { el.placeholder = t(el.dataset.i18nPh); });
}

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
const full = v => Math.round(v).toLocaleString(NUMLOC()) + ' ISK';
const nloc = v => v.toLocaleString(NUMLOC());

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
  const hm = d.toLocaleTimeString(NUMLOC(), { hour: '2-digit', minute: '2-digit' });
  return today ? hm : d.toLocaleDateString(NUMLOC(), { day: '2-digit', month: '2-digit' }) + ' ' + hm;
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

// Charakter-Portraits von CCPs öffentlichem Image-Server (kein Login nötig).
// Lädt das Bild nicht (z.B. Demo-Charakter), bleiben die Initialen darunter sichtbar.
const portraitImg = id =>
  `<img src="https://images.evetech.net/characters/${id}/portrait?size=64" alt="" loading="lazy" onerror="this.remove()">`;

// ---------- SVG-Charts (Flächenlinie wie im Design) ----------

let gradSeq = 0;
function areaChart(points, { w = 640, h = 200 } = {}) {
  if (!points || points.length < 2)
    return `<div class="empty">${t('chartEmpty')}</div>`;

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
  if (total <= 0) return `<div class="empty">${t('noIncome7d')}</div>`;

  const colors = ['var(--color-accent)', 'var(--color-accent-700)', 'var(--color-neutral-500)', 'var(--color-neutral-700)'];
  const C = 2 * Math.PI * 48;
  let off = 0, segs = '', legend = '';
  parts.forEach((p, i) => {
    if (p.value <= 0) return;
    const len = (p.value / total) * C;
    segs += `<circle cx="60" cy="60" r="48" fill="none" stroke="${colors[i % colors.length]}" stroke-width="12"
      stroke-dasharray="${Math.max(len - 2, 1).toFixed(1)} ${C.toFixed(1)}" stroke-dashoffset="${(-off).toFixed(1)}"
      stroke-linecap="round" transform="rotate(-90 60 60)"></circle>`;
    legend += `<div><span class="sw" style="background:${colors[i % colors.length]}"></span>${esc(refLabel(p.label))}
      <span class="pct">${fmtIsk(p.value)} · ${Math.round(p.value / total * 100)}%</span></div>`;
    off += len;
  });

  return `
<svg viewBox="0 0 120 120">
  <circle cx="60" cy="60" r="48" fill="none" stroke="var(--color-neutral-800)" stroke-width="12"></circle>
  ${segs}
  <text x="60" y="57" text-anchor="middle" font-size="16" fill="var(--color-text)" font-family="Chakra Petch">${fmtIsk(total)}</text>
  <text x="60" y="73" text-anchor="middle" font-size="9" fill="var(--color-neutral-400)">${t('income7d')}</text>
</svg>
<div class="split-legend">${legend}</div>`;
}

// ---------- Status / Sidebar ----------

async function loadStatus() {
  S.status = await api('/api/status');
  const st = S.status;

  if ((st.lang || 'en') !== LANG) { LANG = st.lang || 'en'; applyLang(); }
  document.querySelectorAll('#langSeg .seg-opt').forEach(x =>
    x.classList.toggle('on', x.dataset.lang === LANG));

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
  $('appVersion').textContent = 'v' + (st.version || '?');
  $('syncState').textContent = st.syncBusy ? t('syncing') : (st.syncMessage || '–');

  // Baut dieses Build ohne eingebettete Standard-App (Fork), ist die eigene ID Pflicht
  if (!st.hasDefaultApp) {
    $('clientId').placeholder = '1a2b3c4d5e6f...';
    $('clientIdNote').classList.add('hidden');
  } else {
    $('clientId').placeholder = t('phClientId');
    $('clientIdNote').classList.remove('hidden');
  }

  const chars = st.characters || [];
  if (chars.length && (!S.charId || !chars.some(c => c.characterId === S.charId)))
    S.charId = chars[0].characterId;

  const cur = chars.find(c => c.characterId === S.charId);
  $('charAvatar').innerHTML = cur ? esc(initials(cur.name)) + portraitImg(cur.characterId) : '–';
  $('charName').textContent = cur ? cur.name : t('noChar');
  $('charSub').textContent = cur ? fmtIsk(cur.balance) + ' ISK' : t('pleaseLogin');

  $('charPop').innerHTML = chars.map(c => `
    <div class="char-opt" data-pick="${c.characterId}">
      <div class="mini">${esc(initials(c.name))}${portraitImg(c.characterId)}</div>
      <div><div>${esc(c.name)}</div><div class="sub">${fmtIsk(c.balance)} ISK</div></div>
    </div>`).join('') || `<div class="char-opt"><div class="sub">${t('noCharYet')}</div></div>`;

  // LIVE-Tag: verbunden, wenn eingerichtet und der letzte Abgleich fehlerfrei war
  const errs = (st.errors || []).filter(e => e.error);
  const live = st.configured && chars.length > 0 && errs.length === 0;
  $('liveTag').className = 'tag ' + (live ? 'tag-accent' : 'tag-off');
  $('liveTag').textContent = live ? 'LIVE · ESI connected' : (st.configured ? t('esiError') : t('notConnected'));

  $('dashBanners').innerHTML =
    (!st.configured || chars.length === 0
      ? `<div class="banner info">${t('setupHint')}</div>` : '') +
    errs.map(e => `<div class="banner err"><b>${esc(e.resource)}</b>: ${esc(e.error)}</div>`).join('');

  // Timing-Karte: aktive Glättungsstufe markieren
  document.querySelectorAll('#rateHoldSeg .seg-opt').forEach(x =>
    x.classList.toggle('on', Number(x.dataset.hold) === (st.rateHold || 300)));

  // Auto-Stopp-Schalter (Session endet mit dem EVE-Client)
  $('autoStopToggle').innerHTML =
    `<span class="tag ${st.sessionAutoStop ? 'tag-accent' : 'tag-off'} click" data-autostop="${st.sessionAutoStop ? 0 : 1}">${t('autoStopName')}</span>`;

  $('charAdmin').innerHTML = chars.map(c => `
    <div class="row" style="border:1px solid var(--color-neutral-800);border-radius:8px;padding:10px 12px">
      <div class="char-avatar" style="width:34px;height:34px">${esc(initials(c.name))}${portraitImg(c.characterId)}</div>
      <div style="flex:1;min-width:0">
        <div style="font-size:13px">${esc(c.name)}</div>
        <div class="head-note">wallet · orders · mining · jobs · assets${c.hasKillScope ? ' · killmails' : ''}</div>
      </div>
      ${c.hasKillScope
        ? '<span class="tag tag-accent">Connected</span>'
        : `<span class="tag tag-off">${t('killScopeMissing')}</span>`}
      <button class="btn btn-danger" style="font-size:12px" data-forget="${c.characterId}">Revoke</button>
    </div>`).join('') || `<div class="empty">${t('noCharYet')}</div>`;

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
  const stand = new Date(syncInfo.walletLast).toLocaleTimeString(NUMLOC(), { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  const cd = rest > 0 ? `${t('updIn')} ${rest}s` : t('updSoon');
  let jr = '';
  if (syncInfo.journalLast) {
    const jRest = Math.round((new Date(syncInfo.journalNext).getTime() - now) / 60000);
    jr = ` · ${jRest > 0 ? t('jIn') + ' ' + jRest + ' min' : t('jSoon')}`;
  }
  el.textContent = `${t('asOf')} ${stand} · ${cd}${jr}`;
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
    ? t('unchangedToday')
    : `<i class="ph ph-trend-${sum.net24 >= 0 ? 'up' : 'down'}"></i> ${signed(sum.net24)} ${t('in24h')}`;
  $('stBalanceSub').className = 'stat-sub ' + (sum.net24 > 0 ? 'pos' : '');

  setSessionUi(sess);

  const mining = await api(`/api/mining/today?charId=${S.charId}`);
  $('stMining').innerHTML = mining.total > 0 ? bigIsk(mining.total) : '–';
  $('stMiningSub').textContent = mining.total > 0
    ? `${nloc(mining.units)} ${t('units')} · ${t('today')}` : t('miningSubDefault');

  $('oreList').innerHTML = mining.ores.length ? mining.ores.map((o, _, arr) => {
    const p = Math.round(o.value / arr[0].value * 100);
    return `<div>
      <div class="ore-head"><span>${esc(o.name)}</span><span class="amt">${nloc(o.qty)} ${t('pcs')}</span><span class="val">${fmtIsk(o.value)}</span></div>
      <div class="bar"><div class="bar-fill" style="width:${p}%"></div></div>
    </div>`;
  }).join('') : `<div class="empty">${t('noMiningToday')}</div>`;

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
        <div class="list-title">${esc(refLabel(e.label))}</div>
        <div class="list-sub">${fmtTime(e.time)}${e.desc ? ' · ' + esc(e.desc) : ''}</div>
      </div>
      <div class="list-val ${cls(e.amount)}">${signed(e.amount)}</div>
    </div>`).join('') : `<div class="empty">${t('noEntries')}</div>`;
  $('recentNote').textContent = t('recentNote');
}

function setSessionUi(sess) {
  const active = sess && sess.active;
  $('btnSession').textContent = active ? t('sessionStop') : t('sessionStart');
  $('btnSession').className = 'btn ' + (active ? 'btn-danger' : 'btn-primary');
  $('dashNote').textContent = active
    ? `Session ${sessionClock(sess.hours)} · ${t('since')} ${fmtTime(sess.startedUtc)}`
    : t('noSessionRunning');

  $('stRate').innerHTML = active ? bigIsk(sess.iskPerHour) : '–';
  $('stSession').innerHTML = active ? bigIsk(sess.delta) : '–';
  $('stSession').className = 'stat-value ' + (active ? cls(sess.delta) : '');
  $('stSessionSub').textContent = active ? full(sess.delta) : t('noSession');
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
      <span><span class="${net >= 0 ? 'acc' : 'neg'}">${net >= 0 ? '▲' : '▼'} ${fmtIsk(net)}</span> ${t('change')}</span>
      <span>${t('peak')} ${fmtIsk(peak.balance)} · ${fmtTime(peak.t)}</span>
      <span>${pts.length - 1} ${t('entries')}</span>`;
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
      <td><span class="tag tag-neutral">${esc(refLabel(e.label))}</span></td>
      <td style="font-size:13px">${esc(e.desc || '—')}</td>
      <td class="num mono ${cls(e.amount)}">${signed(e.amount)}</td>
      <td class="num head-note">${fmtIsk(e.balance)}</td>
    </tr>`).join('') : `<tr><td colspan="5" class="empty">${t('noEntries')}</td></tr>`;
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
  const scopeHint = me && !me.hasKillScope
    ? `<div class="banner info">${t('killScopeBanner')}</div>` : '';

  const rows = k.rows.map(x => `
    <tr>
      <td class="head-note" style="white-space:nowrap">${fmtTime(x.time)}</td>
      <td><span class="tag ${x.isLoss ? 'tag-off' : 'tag-accent'}">${x.isLoss ? t('loss') : t('kill')}</span></td>
      <td>${esc(x.ship)}${x.victim && !x.isLoss ? ' <span class="head-note">— ' + esc(x.victim) + '</span>' : ''}</td>
      <td class="head-note">${esc(x.system)}</td>
      <td class="num mono ${x.isLoss ? 'neg' : 'acc'}">${x.value > 0 ? fmtIsk(x.value) : '—'}</td>
      <td><a href="https://zkillboard.com/kill/${x.killmailId}/" target="_blank" rel="noopener">zKill →</a></td>
    </tr>`).join('');

  $('tab-kills').innerHTML = scopeHint + tileGrid(
    tile(t('kKills'), String(k.killCount), 'acc', t('inRange')) +
    tile(t('kDestroyed'), fmtIsk(k.destroyed), 'acc', t('zkbValue')) +
    tile(t('kLosses'), String(k.lossCount), k.lossCount > 0 ? 'neg' : '', t('ownShips')) +
    tile(t('kLost'), '−' + fmtIsk(k.lost), k.lost > 0 ? 'neg' : '', t('zkbValue'))) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>${t('thTime')}</th><th>${t('thType')}</th><th>${t('thShip')}</th><th>System</th><th class="num">${t('thValue')}</th><th></th></tr></thead>
      <tbody>${rows || `<tr><td colspan="6" class="empty">${t('noKills')}</td></tr>`}</tbody>
    </table></div></div>`;
}

function renderTrading(tr) {
  const warn = tr.unmatchedQty > 0 ? `
    <div class="banner info">${LANG === 'en'
      ? `No purchase price is known for ${nloc(tr.unmatchedQty)} sold units (${tr.unmatchedTypes} types) — the buy predates data collection. Profit is overstated there.`
      : `Bei ${nloc(tr.unmatchedQty)} verkauften Einheiten (${tr.unmatchedTypes} Typen) ist kein Einkaufspreis bekannt — der Kauf liegt vor Beginn der Datensammlung. Der Gewinn ist dort zu hoch ausgewiesen.`}</div>` : '';
  const rows = tr.items.slice(0, 200).map(i => `
    <tr><td>${esc(i.name)}</td>
      <td class="num">${nloc(i.quantitySold)}</td>
      <td class="num mono">${fmtIsk(i.revenue)}</td>
      <td class="num mono">${fmtIsk(i.cogs)}</td>
      <td class="num mono ${cls(i.gross)}">${signed(i.gross)}</td>
      <td class="num">${pct(i.margin)}</td></tr>`).join('');
  return warn + tileGrid(
    tile(t('kRevenue'), fmtIsk(tr.revenue), 'acc', full(tr.revenue)) +
    tile(t('kCogs'), fmtIsk(tr.cogs), '', t('fifoBase')) +
    tile(t('kGross'), signed(tr.grossProfit), cls(tr.grossProfit), t('beforeFees')) +
    tile(t('kFees'), '−' + fmtIsk(tr.brokerFees + tr.salesTax), 'neg', `${t('broker')} ${fmtIsk(tr.brokerFees)} · ${t('tax')} ${fmtIsk(tr.salesTax)}`) +
    tile(t('kNet'), signed(tr.netProfit), cls(tr.netProfit), full(tr.netProfit))) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>Item</th><th class="num">${t('thSold')}</th><th class="num">${t('thRevenue')}</th><th class="num">${t('thBuy')}</th><th class="num">${t('thProfit')}</th><th class="num">${t('thMargin')}</th></tr></thead>
      <tbody>${rows || `<tr><td colspan="6" class="empty">${t('noSales')}</td></tr>`}</tbody>
    </table></div></div>`;
}

function renderRatting(r) {
  const rows = r.lines.map(l => `
    <tr><td>${esc(refLabel(l.category))}</td><td class="num">${nloc(l.count)}</td>
    <td class="num mono ${cls(l.amount)}">${signed(l.amount)}</td></tr>`).join('');
  return tileGrid(tile(t('kSum'), signed(r.total), cls(r.total), full(r.total))) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>${t('thCategory')}</th><th class="num">${t('thCount')}</th><th class="num">${t('thAmount')}</th></tr></thead>
      <tbody>${rows || `<tr><td colspan="3" class="empty">${t('noEntriesRange')}</td></tr>`}</tbody>
    </table></div></div>`;
}

function renderMining(m) {
  const rows = m.lines.map(l => `
    <tr><td>${esc(l.name)}</td><td class="num">${nloc(l.quantity)}</td>
    <td class="num">${nloc(Math.round(l.unitPrice))}</td>
    <td class="num mono acc">${fmtIsk(l.value)}</td></tr>`).join('');
  return `<div class="banner info">${t('miningBanner')}</div>` +
    tileGrid(
      tile(t('kMarketValue'), fmtIsk(m.totalValue), 'acc', full(m.totalValue)) +
      tile(t('kQty'), nloc(m.totalUnits), '', t('units'))) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>${t('thOre')}</th><th class="num">${t('thQty')}</th><th class="num">${t('thUnitPrice')}</th><th class="num">${t('thValue')}</th></tr></thead>
      <tbody>${rows || `<tr><td colspan="4" class="empty">${t('noMiningRange')}</td></tr>`}</tbody>
    </table></div></div>`;
}

function renderIndustry(ind) {
  const rows = ind.jobs.slice(0, 200).map(j => `
    <tr><td>${esc(j.product)}</td><td class="num">${nloc(j.runs)}</td>
    <td class="num mono neg">${fmtIsk(j.cost)}</td>
    <td class="num mono acc">${fmtIsk(j.outputValue)}</td>
    <td class="num mono ${cls(j.outputValue - j.cost)}">${signed(j.outputValue - j.cost)}</td>
    <td><span class="tag tag-neutral">${esc(j.status)}</span></td>
    <td class="head-note">${fmtTime(j.endUtc)}</td></tr>`).join('');
  return `<div class="banner info">${t('indBanner')}</div>` +
    tileGrid(
      tile(t('kJobCost'), '−' + fmtIsk(ind.totalCost), 'neg', full(ind.totalCost)) +
      tile(t('kOutput'), fmtIsk(ind.totalOutputValue), 'acc', t('estimated')) +
      tile(t('kBalanceT'), signed(ind.balance), cls(ind.balance), `${ind.jobCount} ${t('jobs')}`)) + `
    <div class="card" style="padding:4px 0"><div class="tablewrap"><table class="table">
      <thead><tr><th>${t('thProduct')}</th><th class="num">Runs</th><th class="num">${t('thCost')}</th><th class="num">${t('kOutput')}</th><th class="num">${t('kBalanceT')}</th><th>Status</th><th>${t('thEnd')}</th></tr></thead>
      <tbody>${rows || `<tr><td colspan="7" class="empty">${t('noJobs')}</td></tr>`}</tbody>
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
      <thead><tr><th>${t('thLabel')}</th><th>${t('thStart')}</th><th>${t('thDuration')}</th><th class="num">ISK</th><th class="num">ISK/h</th></tr></thead>
      <tbody>${rows || `<tr><td colspan="5" class="empty">${t('noSessions')}</td></tr>`}</tbody>
    </table></div></div>`;
}

// ---------- Overlay-Screen ----------

const metricDefs = () => [
  { key: 'time', name: 'Timer', label: 'SESSION' },
  { key: 'session', name: 'Session-ISK', label: 'SESSION ISK' },
  { key: 'rate', name: 'ISK/h', label: LANG === 'en' ? 'ISK/HOUR' : 'ISK/STUNDE' },
  { key: 'bounties', name: 'Bounties', label: LANG === 'en' ? 'BOUNTIES EST.' : 'BOUNTIES CA.' },
  { key: 'missions', name: LANG === 'en' ? 'Missions' : 'Missionen', label: LANG === 'en' ? 'MISSIONS EST.' : 'MISSIONEN CA.' },
  { key: 'kills', name: 'Kills', label: 'KILLS' },
  { key: 'destroyed', name: LANG === 'en' ? 'Destroyed' : 'Zerstört', label: LANG === 'en' ? 'DESTROYED' : 'ZERSTÖRT' },
  { key: 'mining', name: 'Mining', label: LANG === 'en' ? 'MINING VALUE' : 'MINING-WERT' },
  { key: 'wallet', name: 'Wallet', label: 'WALLET' },
];
const activeMetrics = () =>
  ((S.status && S.status.overlayMetrics) || 'time,session,rate,mining').split(',').filter(Boolean);

async function loadOverlayScreen() {
  if (!S.charId) return;
  const d = await api(`/api/overlay-data?charId=${S.charId}`);
  $('pvChar').innerHTML = d.showChar
    ? `${portraitImg(S.charId)}${esc(d.name || '–')}` : '';

  // Opsec-Schalter: Portrait & Name im Widget-Kopf an/aus
  $('charToggle').innerHTML =
    `<span class="tag ${d.showChar ? 'tag-accent' : 'tag-off'} click" data-char-toggle="${d.showChar ? 0 : 1}">${t('charToggleName')}</span>`;

  const defs = metricDefs();
  const act = activeMetrics();
  const cell = (k, cw) => {
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
    const def = defs.find(m => m.key === k);
    return `<div class="pw-cell" style="${cw}"><div class="pw-label">${def.label}</div><div class="pw-value ${cls}">${text}</div></div>`;
  };
  // wie im Widget: bis 4 Kacheln eine Reihe, ab 5 zwei ausgewogene Reihen;
  // Zellenbreite folgt der vollsten Reihe, kürzere Reihen werden zentriert
  const rows = act.length <= 4 ? [act] : [act.slice(0, Math.ceil(act.length / 2)), act.slice(Math.ceil(act.length / 2))];
  const cellW = `width:calc(${(100 / rows[0].length).toFixed(3)}% - 1px)`;
  $('pvRow').innerHTML = rows.map(r =>
    '<div class="pw-row">' + r.map(k => cell(k, cellW)).join('<div class="pw-sep"></div>') + '</div>').join('');

  $('metricToggles').innerHTML = defs.map(m =>
    `<span class="tag ${act.includes(m.key) ? 'tag-accent' : 'tag-off'} click" data-metric="${m.key}">${m.name}</span>`).join('');
}

$('autoStopToggle').addEventListener('click', async e => {
  const b = e.target.closest('[data-autostop]');
  if (!b) return;
  try {
    await api('/api/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sessionAutoStop: b.dataset.autostop }),
    });
    await loadStatus();
  } catch (err) { alert(t('saveFail') + err.message); }
});

$('charToggle').addEventListener('click', async e => {
  const b = e.target.closest('[data-char-toggle]');
  if (!b) return;
  try {
    await api('/api/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ overlayChar: b.dataset.charToggle }),
    });
    loadOverlayScreen();
  } catch (err) { alert(t('saveFail') + err.message); }
});

$('metricToggles').addEventListener('click', async e => {
  const tgt = e.target.closest('[data-metric]');
  if (!tgt) return;
  const act = activeMetrics();
  const key = tgt.dataset.metric;
  const next = act.includes(key) ? act.filter(k => k !== key) : [...act, key];
  if (!next.length) { alert(t('minOneMetric')); return; }
  try {
    await api('/api/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ overlayMetrics: next.join(',') }),
    });
    // Reihenfolge wie der Server normieren (feste Kachel-Reihenfolge)
    S.status.overlayMetrics = metricDefs().map(m => m.key).filter(k => next.includes(k)).join(',');
    loadOverlayScreen();
  } catch (err) { alert(t('saveFail') + err.message); }
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
  catch (e) { alert(t('syncFail') + e.message); }
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
  } catch (e) { alert(t('saveFail') + e.message); }
});

$('btnSaveTxt').addEventListener('click', async () => {
  try {
    await api('/api/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ overlayTextPath: $('overlayTextPath').value }),
    });
    delete $('overlayTextPath').dataset.dirty;
  } catch (e) { alert(t('saveFail') + e.message); }
});

$('btnCopyUrl').addEventListener('click', async e => {
  try { await navigator.clipboard.writeText($('srcUrl').value); e.currentTarget.innerHTML = '<i class="ph ph-check"></i> ' + t('copied'); }
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
  } catch (err) { alert(t('saveFail') + err.message); }
});

// Sprachwahl: sofort umschalten, speichern, aktuelle Ansicht neu aufbauen
$('langSeg').addEventListener('click', async e => {
  const b = e.target.closest('[data-lang]');
  if (!b || b.dataset.lang === LANG) return;
  try {
    await api('/api/config', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ lang: b.dataset.lang }),
    });
    LANG = b.dataset.lang;
    applyLang();
    document.querySelectorAll('#langSeg .seg-opt').forEach(x => x.classList.toggle('on', x === b));
    await loadStatus();
    await loaders[S.screen]();
    renderSyncNote();
  } catch (err) { alert(t('saveFail') + err.message); }
});

// Login erst zulassen, wenn die Client-ID gespeichert ist — sonst navigiert das
// ganze Fenster zu einer Fehlerseite, und das ist im Desktop-Fenster eine Sackgasse.
$('btnAddChar').addEventListener('click', e => {
  if (!S.status || !S.status.configured) {
    e.preventDefault();
    alert(t('loginFirst'));
    $('clientId').focus();
  }
});

document.addEventListener('click', async e => {
  const copy = e.target.dataset && e.target.dataset.copy;
  if (copy) {
    try { await navigator.clipboard.writeText($(copy).textContent); e.target.textContent = t('copied'); } catch (err) { }
    setTimeout(() => { e.target.textContent = t('copy'); }, 1600);
  }
  const forget = e.target.dataset && e.target.dataset.forget;
  if (forget && confirm(t('confirmRevoke'))) {
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
  applyLang();
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
