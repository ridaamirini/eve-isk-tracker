## v0.4.0 — DPS everywhere, and overlays that follow your character

### ✨ Added

- **DPS graph in the stream widget** — the combat curves (damage dealt vs. taken) are now
  a toggleable tile in the OBS/Streamlabs widget, right below the stat tiles.
- **Standalone DPS browser source** — prefer the graph somewhere else in your scene?
  It is also available on its own at `/dpswidget` (420 × 150, transparent background).
- **Recommended source size** — the Stream Overlay screen measures the live preview and
  tells you exactly what size to give the browser source; it updates as you toggle tiles.
- **All overlays follow the character you pick in the app** — switch characters at the
  bottom left and the stream widget, the DPS source and the in-game overlay all switch
  with you. No need to touch your OBS sources; existing URLs keep working. Append
  `&pin=1` to lock a single source to one specific character (multiboxing).
- **Preview with demo curves** — the widget preview inside the app shows simulated combat
  data, so you can judge the graph without being in a fight. The real OBS source always
  shows real data.

### 💅 Improved

- **Smoother graphs** — the curves now scroll continuously (instead of jumping once per
  second), are smoothed with a 7-second rolling average and drawn as rounded Bézier
  curves instead of jagged spikes.
- **Calm right edge** — EVE writes its combat log in bursts, which made the newest
  seconds twitch. The last few seconds are now held back until they are final.
- **Combat log selection** — when multiboxing, the character you selected takes priority
  over whichever client happens to be writing the most.
- **In-game overlay footer** — the totals since client start are colour-coded to match
  their curves instead of sitting there as unlabelled grey numbers.

### 🐛 Fixed

- **The in-game overlay can no longer take the graphics driver down with it.** Overlays
  over games are a classic cause of GPU crashes, so the overlay now renders entirely
  without the GPU, in its own isolated browser process, and reloads itself if that
  process ever dies. Hiding the overlay suspends it completely.
- **Session row explains itself** — without a running session the ISK row used to show
  nothing but dashes, which read like "the tracker is broken". It now shows your wallet
  balance plus a note that no session is running (and says so clearly when no character
  is signed in, or when the tracker is unreachable).
- **Stale token errors** — "please sign in again" no longer sticks around forever after a
  successful re-login.
- **CCP downtime** is shown as a calm notice instead of red error banners; the LIVE
  status stays on.

### 📝 Docs

- README updated for all of the above, including the new external data source
  ([EVE Ref](https://everef.net/), blueprint data) and an explicit note that combat logs
  are only ever read, never written, and never leave your machine.
- All screenshots refreshed; new ones for the in-game overlay, the LP store and the
  standalone DPS source.

---

**Requirements:** Windows 10/11 with the WebView2 runtime (ships with Windows 11).
The in-game overlay needs EVE in **windowed** or **borderless fullscreen** mode.
SmartScreen warns about unsigned downloads — "More info" → "Run anyway".

**Full Changelog**: https://github.com/ridaamirini/eve-isk-tracker/compare/v0.3.0...v0.4.0
