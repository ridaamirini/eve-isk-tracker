# Setup Guide

Most users don't need this document: download the release EXE, start it, sign in — done.
This guide covers the details: using your own CCP application, the stream widget,
update timing, and troubleshooting.

## Basics

- Double-clicking `EveIskTracker.exe` opens the app window and starts the internal
  service (port 8765).
- **Closing the window (X) does not quit the app** — it minimizes to the tray so the
  stream widget keeps working. Quit via right-click on the tray icon → Exit.
- Autostart with Windows: put a shortcut to the EXE with the `--tray` argument into
  `shell:startup`. The app then starts invisibly in the tray.
- Starting the EXE a second time brings the existing window to the front.
- Language (English/German) is switchable at the top of **Settings** and is remembered.

## Signing in

Official releases include a built-in ESI application registration — click
**Settings → Sign in character**, log in on CCP's page, approve the scopes, done.
Your password never touches the app; the access token is stored DPAPI-encrypted and can
only be read by your Windows account.

### Using your own CCP application (optional)

If you'd rather not use the built-in registration (or you built from source):

1. Go to [developers.eveonline.com/applications](https://developers.eveonline.com/applications)
   and sign in with your EVE account
2. **Create New Application**, any name and description
3. **Connection Type**: `Authentication & API Access`
4. **Permissions** — exactly these six:
   - `esi-wallet.read_character_wallet.v1`
   - `esi-markets.read_character_orders.v1`
   - `esi-industry.read_character_mining.v1`
   - `esi-industry.read_character_jobs.v1`
   - `esi-assets.read_assets.v1`
   - `esi-killmails.read_killmails.v1`
5. **Callback URL** — exactly:
   ```
   http://localhost:8765/callback
   ```
   This address does **not** need to be reachable from the internet — CCP only redirects
   your own browser back to your own machine. No port forwarding required.
6. Save, copy the **Client ID** from the overview page, paste it under
   **Settings → App Registration** and save. Then sign in your character(s).

## The stream widget

1. **Stream Overlay** → **Copy source URL** (or tray menu → copy widget URL)
2. In OBS/Streamlabs: add a **browser source**, paste the URL, size **420 × 240**,
   position it freely — the background is fully transparent
3. Pick the tiles you want inside the app: timer, session ISK, ISK/h, bounties est.,
   missions est., kills, destroyed, mining value, wallet. Up to 4 tiles render in one
   row; 5+ split into two balanced rows.
4. **Character in widget** toggles your portrait and name in the widget header —
   turn it off if you don't want your character shown on stream.

Changes apply to the running browser source within seconds; you never need to touch OBS
again. Without an active session the widget only shows the wallet balance. Bounties and
missions carry an "est." because they come from the journal, which CCP caches for up to
an hour.

Alternative for classic text sources: set a text file path on the Stream Overlay page;
the app writes the session value into that file.

## Update timing (why numbers aren't real-time)

CCP caches every API response for a fixed time — polling faster returns identical data.
The app checks every 20 seconds whether something new is available:

| Data | Fresh from CCP | Used in |
|---|---|---|
| Wallet balance | every 2 min | session counter, widget, stats |
| Journal (entries) | hourly | breakdown, charts, ratting, recent activity |
| Mining ledger | every 10 min | mining cards, widget tile |
| Industry jobs | every 5 min | industry report |
| Market orders | every 20 min | trading report |
| Market prices | hourly | mining/industry valuation |

The widget additionally only repaints on real data changes, and ISK/h is recalculated at
a configurable interval (Settings → Timing, default 5 min) — otherwise it would visibly
creep on stream because time keeps running.

## Data & backups

Everything lives in `%LOCALAPPDATA%\EveIskTracker\eveisk.db` (SQLite). The app writes a
consistent snapshot to `eveisk.backup.db` next to it once a day. Backing up = copying the
file. The stored sign-in is bound to your Windows account; on another machine you sign in
once again.

## Troubleshooting

**"Login failed"** — if you use your own CCP app, check the callback URL: exactly
`http://localhost:8765/callback` (with `http`, ending in `/callback`), and make sure all
six scopes are added.

**403 errors during sync** — a scope is missing from the CCP application. Add it, then
revoke and re-sign-in the character in the app.

**"Port 8765 in use" on start** — an instance is already running (check the tray).

**Kill data missing, "Kill scope missing" tag** — your stored sign-in predates the
killmail scope. Revoke and sign in again.

**Charts empty** — there is simply no journal data in the selected period yet; give the
first sync a few minutes.
