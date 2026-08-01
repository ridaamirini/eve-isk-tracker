# EVE ISK Tracker

A Windows desktop app that reads your EVE Online wallet through CCP's official ESI API,
keeps **all data permanently on your machine**, and turns it into useful analytics — with a
live session HUD widget for OBS/Streamlabs. One EXE, no installation, no cloud, no
third-party server.

**UI available in English and German** (switchable in Settings).

![Dashboard](docs/dashboard.png)

## Why?

CCP's API only serves a rolling window (wallet journal and transactions go back ~30 days).
EVE ISK Tracker polls the data regularly and keeps it in a local SQLite database — the
longer the app runs, the further back your own history reaches, beyond what the game
itself still shows you.

## Features

- **Dashboard** — wallet balance, ISK/h, session ISK, mining yield; wallet history chart
  (24h/7d/30d), recent activity, 7-day income split donut
- **Wallet** — journal with categorized entries, 30-day balance chart, in/out last 24h,
  CSV export
- **Reports** — trading with **real FIFO profit calculation** (actual cost basis instead of
  raw revenue, broker fees and sales tax separated), ratting/missions, mining, industry,
  **kills & losses with zKillboard values and links**, session history
- **Sessions** — start/stop with one click, live ISK delta and ISK/h, mining attribution
  to the session window
- **Stream widget** — 420 × 240 px browser source styled like classic session HUDs:
  timer · session ISK · ISK/h · bounties · missions · kills · destroyed · mining · wallet,
  each tile toggleable, optional character portrait & name in the header, fully
  transparent background, live preview inside the app
- **Update countdown** — the dashboard shows exactly how old the current numbers are and
  when the next refresh arrives

| Wallet | Stream widget |
|---|---|
| ![Wallet](docs/wallet.png) | ![Widget](docs/widget.png) |

![Reports](docs/reports.png)
![Kills](docs/kills.png)
![Stream Overlay](docs/overlay.png)

## Getting started

1. Download `EveIskTracker.exe` from [Releases](../../releases) and run it
   (Windows 10/11 with the WebView2 runtime, which ships with Windows 11).
2. Go to **Settings → Sign in character** and log in on CCP's official SSO page.
3. Done. The first sync starts automatically.

Official release builds ship with a built-in ESI application registration, so there is
nothing to configure. If you prefer to use **your own CCP application** (or you build from
source, where no default is embedded), see [SETUP.md](SETUP.md).

> Windows SmartScreen warns about unsigned downloads — "More info" → "Run anyway".

### Widget in OBS/Streamlabs

**Stream Overlay** → **Copy source URL** → add as a browser source at **420 × 240** in
OBS/Streamlabs. Pick which tiles to show (and whether to show your character's portrait
and name) inside the app; changes apply to the running widget within seconds. Without an
active session the widget only shows the wallet balance. The app must be running — the
window may be closed, it keeps living in the tray.

## Security & privacy

- Sign-in via **EVE SSO (OAuth 2 with PKCE)** — your password is only ever entered on
  CCP's own page; the app never sees it
- No client secret anywhere (a distributed EXE could not keep one secret anyway; PKCE is
  designed for exactly this)
- The refresh token is stored **DPAPI-encrypted** — only your Windows account can read it
- The app talks to `esi.evetech.net`, `login.eveonline.com`, `images.evetech.net`
  (character portraits) and — only for kill valuations — `zkillboard.com`;
  fonts/icons come from Google Fonts / unpkg (CDN)
- All data lives in `%LOCALAPPDATA%\EveIskTracker\eveisk.db` (SQLite), with an automatic
  daily snapshot backup next to it

## Building from source

Requires the [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) and the
WebView2 runtime.

```bash
dotnet publish EveIskTracker.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o release
```

Source builds contain **no default client ID** — either enter your own CCP application's
client ID in Settings (see [SETUP.md](SETUP.md)), or pass one at build time with
`-p:DefaultClientId=...`.

Tests (45 calculation checks, incl. FIFO edge cases):

```bash
dotnet run --project EveIskTracker.Tests
```

Demo data to explore the UI without an EVE login (`unseed` removes it again):

```bash
dotnet run --project EveIskTracker.Tests -- seed
```

## Architecture

```
EveIskTracker.Desktop/   WinForms window (WebView2) + internal Kestrel server (port 8765)
  Program.cs             entry point, single-instance guard, --tray for autostart
  MainForm.cs            window, tray icon, dark title bar
  WebHost.cs             HTTP endpoints: UI, API, widget, OAuth callback
  Db.cs                  SQLite schema (permanent data collection, WAL + daily backup)
  EsiClient.cs           ESI HTTP with ETag caching and error-limit awareness
  Sso.cs                 EVE SSO v2 with PKCE
  SyncService.cs         background sync paced by CCP's cache timers
  Analytics.cs           FIFO engine, ratting/mining/industry analytics
  Sessions.cs            session tracking
  wwwroot/               UI (embedded into the EXE), i18n en/de
EveIskTracker.Tests/     calculation tests + demo data seeder
```

The internal web server is not incidental: OBS fetches the widget over HTTP, and CCP's
OAuth sign-in needs the `localhost` callback.

## Honest limitations

- **ESI cache timers** dictate freshness: wallet balance every 2 min, journal hourly.
  Polling faster returns identical data — no tool can go below this.
- **FIFO needs history**: sales of items bought before data collection started have no
  cost basis — the app flags this openly instead of hiding it.
- **Industry without material costs**: ESI only provides job installation costs; the
  balance shown is accordingly optimistic.
- **Kill values come from zKillboard** (one request per new kill). If zKillboard is
  unreachable, the kill is still listed, just without a value.

## Acknowledgements

- [EVE Online / CCP Games](https://www.eveonline.com/) — ESI API, SSO and image server.
  EVE Online and all related trademarks are the property of CCP hf.
- [zKillboard](https://zkillboard.com/) — kill valuations
- Design: "PULSAR" theme based on the Nocturne design system
- [Phosphor Icons](https://phosphoricons.com/), fonts: Chakra Petch & IBM Plex Sans
