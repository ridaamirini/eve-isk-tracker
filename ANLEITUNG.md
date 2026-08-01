# EVE ISK Tracker

Windows-Desktop-App im dunklen Space-Design, die dein EVE-Wallet über
CCPs offizielle Schnittstelle (ESI) ausliest, die Daten dauerhaft lokal sammelt und daraus
Auswertungen rechnet. Navigation links: **Dashboard** (Kennzahlen, Wallet-Verlauf, Recent
Activity, Mining, Einnahmen-Donut), **Wallet** (Journal, 30-Tage-Kurve, CSV-Export),
**Reports** (Handel/FIFO, Ratting, Mining, Industrie, Sessions), **Stream Overlay**
(Widget-Vorschau und -Konfiguration), **Settings** (ESI-Verbindung, App-Registrierung).
Charakterwechsel unten links in der Sidebar.

**Das Stream-Widget ist 420 × 240 px** („ISK TRACKER · LIVE") und wird in Streamlabs/OBS
als Browser-Quelle mit genau dieser Größe angelegt — frei positionierbar, Hintergrund
transparent. Welche Werte es zeigt (Wallet, ISK/h, Session, Mining), wählst du auf der
Stream-Overlay-Seite per Klick an und ab; Änderungen übernimmt das Widget binnen Sekunden
ohne Anfassen der Browser-Quelle. Ohne laufende Session zeigt es nur den Kontostand.
Session-Start/-Stopp: Knopf oben rechts im Dashboard.

**Die Anwendung ist `release\EveIskTracker.exe` — eine Datei, keine Installation.**
Beim Doppelklick öffnet sich ein normales Programmfenster.

> Einzige Systemvoraussetzung neben Windows: die WebView2-Runtime, die auf Windows 11
> (und jedem Windows mit Edge) bereits vorhanden ist. Auf deinem Rechner: vorhanden.

---

## Bedienung

- **Fenster schließen (X)** beendet die App nicht, sondern legt sie in den Infobereich
  (Tray, neben der Uhr). Server, Datenabgleich und das Streamlabs-Widget laufen weiter —
  wichtig, damit die Anzeige im Stream nicht ausfällt, wenn du das Fenster mal wegklickst.
- **Beenden**: Rechtsklick auf das Tray-Symbol → *Beenden*.
- **Widget-URL kopieren**: ebenfalls im Tray-Menü — direkt einfügbar in Streamlabs.
- **Autostart mit Windows** (optional): Verknüpfung zur EXE mit dem Zusatz `--tray` in
  `shell:startup` legen. Die App startet dann unsichtbar im Tray.
- Ein zweiter Doppelklick auf die EXE öffnet keine zweite Instanz, sondern holt das
  vorhandene Fenster nach vorn.

---

## Warum das mehr kann als die Auswertung im Spiel

CCP gibt über die Schnittstelle nur ein rollierendes Fenster heraus: Wallet-Journal und
Transaktionen reichen etwa 30 Tage zurück, danach ist alles Ältere weg. Diese App ruft die
Daten regelmäßig ab und **legt sie dauerhaft lokal ab**. Je länger sie mitläuft, desto weiter
reicht deine Historie zurück.

---

## Einrichtung (einmalig, etwa fünf Minuten)

### 1. App starten

Doppelklick auf `EveIskTracker.exe`.

> Falls Windows SmartScreen warnt: Das passiert bei jeder Anwendung ohne gekaufte
> Code-Signatur. Über *Weitere Informationen* → *Trotzdem ausführen* startest du sie.

### 2. Eigene Anwendung bei CCP registrieren

1. [developers.eveonline.com/applications](https://developers.eveonline.com/applications)
   öffnen (der Link aus der App öffnet sich in deinem normalen Browser), mit dem
   EVE-Account anmelden
2. **Create New Application**
3. Name und Beschreibung frei wählen (z.B. „ISK Tracker")
4. **Connection Type**: `Authentication & API Access`
5. **Permissions** — genau diese sechs:
   - `esi-wallet.read_character_wallet.v1`
   - `esi-markets.read_character_orders.v1`
   - `esi-industry.read_character_mining.v1`
   - `esi-industry.read_character_jobs.v1`
   - `esi-assets.read_assets.v1`
   - `esi-killmails.read_killmails.v1`
6. **Callback URL** — exakt:
   ```
   http://localhost:8765/callback
   ```
   Diese Adresse muss **nicht** aus dem Internet erreichbar sein — CCP leitet nach dem
   Login nur deinen eigenen Rechner dorthin zurück. Keine Portfreigabe nötig.
7. Speichern; die **Client ID** von der Übersichtsseite kopieren.

### 3. In der App eintragen

Client ID einfügen, bei *Kontakt* E-Mail oder Charaktername, **Speichern**,
**Charakter anmelden**. Der Login läuft über CCPs eigene Seite; dein Passwort sieht nur CCP.
Das Zugriffstoken liegt danach Windows-verschlüsselt auf deinem Rechner (DPAPI —
nur dein Windows-Konto kann es lesen). Ein Client-Secret gibt es bewusst nicht.

### 4. Ersten Abgleich starten

**Jetzt abgleichen** klicken. Der erste Lauf dauert etwas; danach hält die App die Daten
im Hintergrund aktuell.

---

## Das Streamlabs-Widget

1. In der App: **Stream Overlay** → **Copy source URL** (oder Tray-Menü → Widget-URL kopieren)
2. Streamlabs → Szene *Live* → **Quellen** → **+** → **Browserquelle**
3. URL einfügen, **Breite 420, Höhe 240**, dann frei in der Szene positionieren
   (im Design vorgesehen: unten links)
4. Hintergrund ist transparent; die Vorschau auf der Stream-Overlay-Seite zeigt,
   wie es in der Szene wirkt

Wählbare Kacheln (Stream-Overlay-Seite, anklicken): **Session-Timer, Session-ISK, ISK/h,
Bounties, Missionen, Mining-Wert, Wallet** — bis zu 4 in einer Reihe, ab 5 zwei Reihen.
Bounties und Missionen tragen ein „ca.", weil sie aus dem Journal stammen (bis zu 1h
Verzögerung durch CCPs Cache). Ohne laufende Session zeigt das Widget nur den Kontostand.
Alternativ gibt es auf der Stream-Overlay-Seite eine Textdatei-Ausgabe für klassische
Textquellen.

Damit das Widget während des Streams funktioniert, muss die App laufen (Fenster darf
zu sein — Tray genügt).

---

## Die Statistiken

- **ISK pro Tag** — Balkendiagramm über den gewählten Zeitraum: Einnahmen nach oben,
  Ausgaben nach unten, dazu Durchschnitt und bester Tag. (Hinterlegtes Geld für offene
  Marktorders ist herausgerechnet, das ist kein Gewinn/Verlust.)
- **Session-Verlaufskurve** — dein Kontostand seit Session-Start als Linie, alle
  zwei Minuten ein Messpunkt.
- **Handel** — Verkäufe nach dem First-in-first-out-Prinzip gegen frühere Käufe
  verrechnet: echter Einstandspreis, echter Gewinn, Marge pro Item. Broker-Gebühr und
  Verkaufssteuer separat. Verkäufe ohne bekannten Einkauf (vor Beginn der Datensammlung)
  werden offen ausgewiesen.
- **Ratting** — Bounties, ESS, Missionen, Versicherungen, Verträge nach Kategorie.
- **Mining** — Ledger mal Marktpreis (CCPs globaler Durchschnitt, nicht Jita).
- **Industrie** — Jobkosten gegen geschätzten Produktwert. Materialkosten liefert ESI
  nicht mit; der Saldo ist entsprechend optimistisch.

### Wie aktuell die Zahlen sind

Von CCPs Cache-Zeiten vorgegeben (häufigeres Abfragen liefert dieselbe Antwort):

| Angabe | Aktualisierung |
|---|---|
| Kontostand → Session-Zähler und Widget | alle **2 Minuten** |
| Journal → Aufschlüsselung, Tages-Chart | **stündlich** |
| Marktorders | alle 20 Minuten |
| Mining-Ledger | alle 10 Minuten |
| Produktionsjobs | alle 5 Minuten |

---

## Gut zu wissen

**Datenablage:** `%LOCALAPPDATA%\EveIskTracker\eveisk.db` (SQLite). Sichern = Datei kopieren.
Der gespeicherte Login ist an dein Windows-Konto gebunden; auf einem anderen Rechner einmal
neu anmelden.

**Nichts verlässt deinen Rechner** außer den Anfragen an CCPs Server (`esi.evetech.net`,
`login.eveonline.com`). Kein fremder Dienst, kein Konto, keine Telemetrie.

**Mehrere Charaktere:** ⚙ → *Weiteren Charakter anmelden*; oben rechts wechseln.
Die Widget-URL enthält die Charakter-ID.

---

## Wenn etwas nicht klappt

**„Login fehlgeschlagen"** — Callback-URL bei CCP prüfen: exakt
`http://localhost:8765/callback` (mit `http`, mit Schrägstrich-Endung `/callback`).

**403-Fehler beim Abgleich** — eine der fünf Berechtigungen fehlt in der CCP-Registrierung.
Ergänzen, dann Charakter in der App entfernen und neu anmelden.

**„Port 8765 belegt"-Meldung beim Start** — es läuft schon eine Instanz (Tray prüfen).

**Diagramm bleibt leer** — es gibt schlicht noch keine Journal-Daten im Zeitraum;
nach dem ersten Abgleich kurz warten.

---

## Für Entwickler

```
EveIskTracker.Desktop\   App: WinForms-Fenster (WebView2) + interner Kestrel-Server
  Program.cs             Einstieg, Einzelinstanz-Schutz, --tray
  MainForm.cs            Fenster, Tray, dunkle Titelleiste, Laufzeit-Icon
  WebHost.cs             Alle HTTP-Endpunkte (Oberfläche, API, Widget, OAuth-Callback)
  Db.cs                  SQLite-Schema und Zugriff
  EsiClient.cs           HTTP mit ETag-Cache und Fehlerlimit-Beachtung
  Sso.cs                 EVE SSO v2 mit PKCE
  SyncService.cs         Hintergrundabgleich nach CCPs Cache-Zeiten
  Analytics.cs           FIFO-Engine und Modulauswertungen
  Sessions.cs            Session-Verfolgung
  wwwroot\               Oberfläche (in die EXE eingebettet)
EveIskTracker.Tests\     45 Rechentests gegen von Hand nachgerechnete Beispiele
```

```bash
dotnet run --project EveIskTracker.Tests
```

```bash
dotnet run --project EveIskTracker.Tests -- seed
```

```bash
dotnet run --project EveIskTracker.Tests -- unseed
```

```bash
dotnet publish EveIskTracker.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o release
```
