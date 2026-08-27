using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EveIskTracker;

/// <summary>
/// Der interne Webserver. Auch als Desktop-App bleibt er nötig: Streamlabs holt sich das
/// Widget als Browser-Quelle über HTTP, und CCPs Login leitet auf localhost zurück.
/// Das Fenster (MainForm) zeigt dieselbe Oberfläche über WebView2 an.
/// </summary>
public static class WebHost
{
    /// <summary>Wird von Program gesetzt; holt bei einem Zweitstart das Fenster nach vorn.</summary>
    public static Action ShowWindow;

    // LP-Auffrischung höchstens alle 5 Minuten automatisch anstoßen
    private static DateTime _lpLastKick = DateTime.MinValue;

    // Haken zum Game-Overlay-Fenster (von Program verdrahtet, laufen über BeginInvoke)
    public static Action<bool> SetGameOverlay;
    public static Action<bool> SetGameOverlayMove;
    public static Func<bool> GameOverlayVisible;
    public static Func<bool> GameOverlayMoving;
    public static Action ApplyGameOverlaySettings;

    public static WebApplication Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://localhost:{Config.Port}");
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SyncService>());

        var app = builder.Build();
        Map(app);
        app.Start();          // nicht blockierend — die Nachrichtenschleife gehört WinForms
        return app;
    }

    private static void Map(WebApplication app)
    {
        app.MapGet("/api/status", () =>
        {
            var chars = new List<object>();
            using (var c = Db.Open())
            using (var cmd = Db.Cmd(c, @"
SELECT ch.character_id, ch.name, ch.last_balance, ch.last_sync_utc, ch.enabled,
       (SELECT COUNT(*) FROM sessions s WHERE s.character_id=ch.character_id AND s.ended_utc IS NULL),
       COALESCE((SELECT t.scopes FROM tokens t WHERE t.character_id=ch.character_id), '')
FROM characters ch ORDER BY ch.name"))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    chars.Add(new
                    {
                        characterId = r.GetInt64(0),
                        name = r.IsDBNull(1) ? "?" : r.GetString(1),
                        balance = r.IsDBNull(2) ? 0d : r.GetDouble(2),
                        lastSync = r.IsDBNull(3) ? null : r.GetString(3),
                        enabled = r.GetInt64(4) == 1,
                        sessionActive = r.GetInt64(5) > 0,
                        // Bestandslogins von vor Scope-Erweiterungen: Feature-Flags je Charakter
                        hasKillScope = r.GetString(6).Contains("esi-killmails"),
                        hasLpScope = r.GetString(6).Contains("esi-characters.read_loyalty"),
                        hasSearchScope = r.GetString(6).Contains("esi-search"),
                    });

            var errors = new List<object>();
            using (var c = Db.Open())
            // character_id 0 sind globale Ressourcen (Preise); sonst nur Fehler von
            // Charakteren melden, die es noch gibt — verwaiste Einträge interessieren nicht
            using (var cmd = Db.Cmd(c, @"
SELECT character_id, resource, last_error FROM sync_state
WHERE last_error IS NOT NULL
  AND (character_id = 0 OR character_id IN (SELECT character_id FROM characters WHERE enabled=1))"))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    var msg = r.GetString(2);
                    errors.Add(new
                    {
                        characterId = r.GetInt64(0),
                        resource = r.GetString(1),
                        error = msg,
                        // Serverseitige Aussetzer (CCP-Downtime, 5xx, Netz) heilen von
                        // selbst — die Oberfläche zeigt sie als Hinweis statt als Alarm
                        transient = msg.Contains("Timeout contacting") || msg.Contains("HTTP 50") ||
                                    msg.Contains("HTTP 420") || msg.Contains("Netzwerkfehler"),
                    });
                }

            return Results.Ok(new
            {
                version = typeof(WebHost).Assembly.GetName().Version?.ToString(3) ?? "?",
                lang = Config.Lang,
                configured = Config.IsConfigured,
                // nur die selbst eingetragene ID anzeigen — leeres Feld heißt: Standard-App aktiv
                clientId = Config.ClientIdRaw,
                hasDefaultApp = !string.IsNullOrWhiteSpace(Config.DefaultClientId),
                usingDefaultApp = !string.IsNullOrWhiteSpace(Config.DefaultClientId) && string.IsNullOrWhiteSpace(Config.ClientIdRaw),
                contact = Config.Contact,
                activeCharId = Config.ActiveCharacterId,
                lpHiddenCorps = Config.LpHiddenCorps,
                overlayTextPath = Config.OverlayTextPath,
                overlayMetrics = Config.OverlayMetrics,
                rateHold = Config.RateHoldSeconds,
                overlayChar = Config.OverlayShowChar,
                sessionAutoStop = Config.SessionAutoStop,
                gameOverlayOn = GameOverlayVisible?.Invoke() ?? false,
                gameOverlayMove = GameOverlayMoving?.Invoke() ?? false,
                gameOverlayModules = Config.GameOverlayModules,
                gameOverlayOpacity = Config.GameOverlayOpacity,
                gameOverlayLayout = Config.GameOverlayLayout,
                redirectUri = Sso.RedirectUri,
                scopes = Sso.Scopes,
                characters = chars,
                syncBusy = SyncService.Busy,
                syncMessage = SyncService.LastMessage,
                errors,
                dbPath = Db.Path,
            });
        });

        app.MapPost("/api/config", async (HttpRequest req) =>
        {
            var form = await req.ReadFromJsonAsync<Dictionary<string, string>>();
            if (form == null) return Results.BadRequest(new { error = "Kein Inhalt." });
            // Leer ist gültig: bedeutet "eingebaute Standard-App verwenden"
            if (form.TryGetValue("clientId", out var cid)) Config.ClientId = cid ?? "";
            if (form.TryGetValue("contact", out var ct)) Config.Contact = ct;
            if (form.TryGetValue("overlayTextPath", out var op)) Config.OverlayTextPath = op;
            if (form.TryGetValue("overlayMetrics", out var om)) Config.OverlayMetrics = om;
            if (form.TryGetValue("rateHold", out var rh) && int.TryParse(rh, out var rhv)) Config.RateHoldSeconds = rhv;
            if (form.TryGetValue("lang", out var lg)) Config.Lang = lg;
            if (form.TryGetValue("overlayChar", out var oc)) Config.OverlayShowChar = oc != "0";
            if (form.TryGetValue("sessionAutoStop", out var sa)) Config.SessionAutoStop = sa != "0";
            if (form.TryGetValue("gameOverlayModules", out var gm)) Config.GameOverlayModules = gm;
            if (form.TryGetValue("gameOverlayLayout", out var gl)) Config.GameOverlayLayout = gl;
            if (form.TryGetValue("lpHiddenCorps", out var lh)) Config.LpHiddenCorps = lh;
            if (form.TryGetValue("gameOverlayOpacity", out var go) && int.TryParse(go, out var gov))
            {
                Config.GameOverlayOpacity = gov;
                ApplyGameOverlaySettings?.Invoke();
            }
            return Results.Ok(new { ok = true });
        });

        // Navigiert das ganze Fenster zu CCPs Login. Fehler müssen deshalb als
        // HTML-Seite mit Rückweg kommen — rohes JSON wäre hier eine Sackgasse.
        app.MapGet("/api/login", () =>
        {
            if (!Config.IsConfigured)
                return Results.Content(
                    HtmlPage("<h1>Client-ID fehlt</h1>" +
                             "<p>Bitte zuerst unter <strong>Settings</strong> die Client-ID deiner " +
                             "CCP-Anwendung eintragen und speichern.</p>" +
                             "<p><a href='/#settings'>Zurück zu den Einstellungen</a></p>" +
                             "<script>setTimeout(function(){ location.href='/#settings'; }, 4000)</script>"),
                    "text/html");
            try
            {
                return Results.Redirect(Sso.BuildAuthorizeUrl(Config.ClientId));
            }
            catch (Exception ex)
            {
                return Results.Content(
                    HtmlPage("<h1>Login konnte nicht gestartet werden</h1>" +
                             $"<pre>{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre>" +
                             "<p><a href='/#settings'>Zurück</a></p>"),
                    "text/html");
            }
        });

        app.MapGet("/callback", async (string code, string state) =>
        {
            try
            {
                var token = await Sso.HandleCallbackAsync(code, state, Config.ClientId);
                Sso.SaveToken(token);
                return Results.Content(
                    HtmlPage($"<h1>Angemeldet</h1><p><strong>{System.Net.WebUtility.HtmlEncode(token.CharacterName)}</strong> " +
                             "wurde hinzugefügt.</p>" +
                             "<script>setTimeout(function(){ location.href='/'; }, 1200)</script>"),
                    "text/html");
            }
            catch (Exception ex)
            {
                return Results.Content(
                    HtmlPage($"<h1>Login fehlgeschlagen</h1><pre>{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre>" +
                             "<p><a href='/'>Zur&uuml;ck</a></p>"),
                    "text/html");
            }
        });

        app.MapPost("/api/sync", async (SyncService sync) =>
        {
            await sync.RunOnce(default, force: true);
            return Results.Ok(new { ok = true, message = SyncService.LastMessage });
        });

        app.MapDelete("/api/character/{id:long}", (long id) =>
        {
            Sso.Forget(id);
            Db.Run("DELETE FROM characters WHERE character_id=$c", ("$c", id));
            Db.Run("DELETE FROM sync_state WHERE character_id=$c", ("$c", id));
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/report", (long charId, string range) =>
        {
            var (from, to) = ParseRange(range);
            return Results.Ok(new
            {
                from = Util.ToIso(from),
                to = Util.ToIso(to),
                trading = Analytics.Trading(charId, from, to),
                ratting = Analytics.Ratting(charId, from, to),
                mining = Analytics.Mining(charId, from, to),
                industry = Analytics.Industry(charId, from, to),
            });
        });

        // Tagesverlauf fürs Balkendiagramm: Einnahmen/Ausgaben je Tag aus dem Journal.
        // market_escrow ist ausgenommen — das ist nur hin- und hergeschobenes Geld für
        // offene Orders, kein Gewinn und kein Verlust, würde die Balken aber dominieren.
        app.MapGet("/api/stats/daily", (long charId, string range) =>
        {
            var (from, to) = ParseRange(range);
            var days = new List<object>();
            using var c = Db.Open();
            using var cmd = Db.Cmd(c, @"
SELECT substr(date_utc,1,10) AS day,
       SUM(CASE WHEN amount > 0 THEN amount ELSE 0 END),
       SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END),
       COUNT(*)
FROM journal
WHERE character_id=$c AND date_utc >= $f AND date_utc <= $t
  AND ref_type NOT IN ('market_escrow')
GROUP BY day ORDER BY day",
                ("$c", charId), ("$f", Util.ToIso(from)), ("$t", Util.ToIso(to)));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                days.Add(new
                {
                    day = r.GetString(0),
                    income = r.GetDouble(1),
                    expense = r.GetDouble(2),
                    net = r.GetDouble(1) + r.GetDouble(2),
                    count = r.GetInt32(3),
                });
            return Results.Ok(days);
        });

        // Kontostand-Verlauf der laufenden Session für die Sparkline.
        app.MapGet("/api/session/samples", (long charId) =>
        {
            var samples = new List<object>();
            using var c = Db.Open();
            using var cmd = Db.Cmd(c, @"
SELECT s.ts_utc, s.balance FROM session_samples s
WHERE s.session_id = (SELECT id FROM sessions WHERE character_id=$c AND ended_utc IS NULL
                      ORDER BY started_utc DESC LIMIT 1)
ORDER BY s.ts_utc", ("$c", charId));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                samples.Add(new { ts = r.GetString(0), balance = r.GetDouble(1) });
            return Results.Ok(samples);
        });

        app.MapGet("/api/session", (long charId) => Results.Ok(Sessions.Current(charId)));

        app.MapPost("/api/session/start", async (long charId, string label, SyncService sync) =>
        {
            // Erst den echten Kontostand holen, dann die Session verankern —
            // sonst startet sie mit einem veralteten (oder gar keinem) Startwert.
            try { await sync.FetchWalletNow(charId); }
            catch { /* zur Not nimmt Start den letzten bekannten Stand */ }
            return Results.Ok(Sessions.Start(charId, label));
        });

        app.MapPost("/api/session/stop", (long charId) =>
        {
            Sessions.Stop(charId);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/session/history", (long charId) =>
        {
            var list = new List<object>();
            using var c = Db.Open();
            using var cmd = Db.Cmd(c, @"
SELECT id, label, started_utc, ended_utc, start_balance, end_balance
FROM sessions WHERE character_id=$c AND ended_utc IS NOT NULL
ORDER BY started_utc DESC LIMIT 50", ("$c", charId));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var start = Util.ParseIso(r.GetString(2));
                var end = Util.ParseIso(r.GetString(3));
                var sb = r.IsDBNull(4) ? 0 : r.GetDouble(4);
                var eb = r.IsDBNull(5) ? 0 : r.GetDouble(5);
                // echte Dauer ausweisen; nur die Rate gegen Mini-Sessions klemmen
                var hours = (end - start).TotalHours;
                list.Add(new
                {
                    id = r.GetInt64(0),
                    label = r.IsDBNull(1) ? null : r.GetString(1),
                    startedUtc = r.GetString(2),
                    endedUtc = r.GetString(3),
                    delta = eb - sb,
                    hours,
                    iskPerHour = (eb - sb) / Math.Max(hours, 1.0 / 60),
                });
            }
            return Results.Ok(list);
        });

        // Schlanker Endpunkt für die Browser-Quelle in Streamlabs.
        // Kontostand ist immer dabei, damit das Widget auch ohne Session etwas zeigt.
        // charId ist optional: ohne Angabe gilt der in der App gewählte Charakter,
        // damit Browser-Quellen einem Wechsel automatisch folgen
        app.MapGet("/api/overlay-data", ([FromQuery(Name = "charId")] long? charIdOpt) =>
        {
            var charId = ResolveChar(charIdOpt);
            var st = Sessions.Current(charId);
            var balance = Sessions.CurrentBalance(charId);
            var name = Db.Scalar("SELECT name FROM characters WHERE character_id=$c", ("$c", charId));

            // Aufschlüsselung fürs HUD: Bounties und Missionen im Session-Fenster.
            // Quelle ist das Journal — hinkt bis zu 1h nach, daher zeigt das Widget "ca."
            double bounties = 0, missions = 0, destroyed = 0;
            long killCount = 0;
            if (st.Active)
            {
                var started = Util.ParseIso(st.StartedUtc);
                bounties = Analytics.SumRefTypes(charId, started, Util.UtcNow,
                    "bounty_prizes", "bounty_prize", "bounty", "ess_escrow_transfer");
                missions = Analytics.SumRefTypes(charId, started, Util.UtcNow,
                    "agent_mission_reward", "agent_mission_time_bonus_reward", "mission_reward", "mission_completion",
                    "daily_goal_payouts", "freelance_jobs_reward");

                using var c = Db.Open();
                using var cmd = Db.Cmd(c, @"
SELECT COUNT(*), COALESCE(SUM(value),0) FROM kills
WHERE character_id=$c AND is_loss=0 AND time_utc >= $f",
                    ("$c", charId), ("$f", Util.ToIso(started)));
                using var r = cmd.ExecuteReader();
                if (r.Read()) { killCount = r.GetInt64(0); destroyed = r.GetDouble(1); }
            }

            return Results.Ok(new
            {
                active = st.Active,
                name = name == null || name == DBNull.Value ? st.CharacterName : (string)name,
                balance,
                delta = st.Delta,
                iskPerHour = st.IskPerHour,
                mining = st.MiningValue,
                bounties,
                missions,
                kills = killCount,
                destroyed,
                hours = st.Hours,
                // Auswahl der Kacheln und Haltezeit wandern mit — so greifen Änderungen
                // im Widget binnen Sekunden, ohne die Browser-Quelle anzufassen
                metrics = Config.OverlayMetrics.Split(','),
                rateHold = Config.RateHoldSeconds,
                lang = Config.Lang,
                showChar = Config.OverlayShowChar,
                charId,
            });
        });

        // ---- Endpunkte für das PULSAR-Dashboard ----

        app.MapGet("/api/wallet/summary", (long charId) =>
        {
            var from = Util.ToIso(Util.UtcNow.AddHours(-24));
            double inSum = 0, outSum = 0;
            using (var c = Db.Open())
            using (var cmd = Db.Cmd(c, @"
SELECT COALESCE(SUM(CASE WHEN amount > 0 THEN amount ELSE 0 END),0),
       COALESCE(SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END),0)
FROM journal WHERE character_id=$c AND date_utc >= $f AND ref_type NOT IN ('market_escrow')",
                ("$c", charId), ("$f", from)))
            using (var r = cmd.ExecuteReader())
                if (r.Read()) { inSum = r.GetDouble(0); outSum = r.GetDouble(1); }

            return Results.Ok(new
            {
                balance = Sessions.CurrentBalance(charId),
                in24 = inSum,
                out24 = outSum,
                net24 = inSum + outSum,
            });
        });

        // Kontostand-Verlauf aus dem Journal (jede Buchung trägt den Stand danach).
        app.MapGet("/api/wallet/series", (long charId, string range) =>
        {
            var from = (range ?? "24h") switch
            {
                "24h" => Util.UtcNow.AddHours(-24),
                "7d" => Util.UtcNow.AddDays(-7),
                _ => Util.UtcNow.AddDays(-30),
            };
            var pts = new List<(string T, double B)>();
            using (var c = Db.Open())
            using (var cmd = Db.Cmd(c, @"
SELECT date_utc, balance FROM journal
WHERE character_id=$c AND date_utc >= $f AND balance IS NOT NULL
ORDER BY date_utc", ("$c", charId), ("$f", Util.ToIso(from))))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) pts.Add((r.GetString(0), r.GetDouble(1)));

            // aktuellen Stand ans Ende hängen, damit die Linie bis "jetzt" reicht
            pts.Add((Util.NowIso(), Sessions.CurrentBalance(charId)));

            // auf höchstens 140 Punkte eindampfen, das reicht für 640px Breite
            if (pts.Count > 140)
            {
                var step = (double)pts.Count / 140;
                var thin = new List<(string, double)>();
                for (var i = 0.0; i < pts.Count; i += step) thin.Add(pts[(int)i]);
                if (thin[^1].Item1 != pts[^1].T) thin.Add(pts[^1]);
                pts = thin;
            }
            return Results.Ok(pts.Select(p => new { t = p.T, balance = p.B }));
        });

        app.MapGet("/api/journal/recent", (long charId, int limit) =>
        {
            var list = new List<object>();
            using var c = Db.Open();
            using var cmd = Db.Cmd(c, @"
SELECT date_utc, ref_type, description, amount, balance FROM journal
WHERE character_id=$c ORDER BY date_utc DESC LIMIT $l",
                ("$c", charId), ("$l", (long)Math.Clamp(limit == 0 ? 8 : limit, 1, 200)));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new
                {
                    time = r.GetString(0),
                    refType = r.GetString(1),
                    label = Sessions.RefTypeLabel(r.GetString(1)),
                    desc = r.IsDBNull(2) ? "" : r.GetString(2),
                    amount = r.IsDBNull(3) ? 0 : r.GetDouble(3),
                    balance = r.IsDBNull(4) ? 0 : r.GetDouble(4),
                });
            return Results.Ok(list);
        });

        // Einnahmen-Aufteilung der letzten 7 Tage für den Donut.
        app.MapGet("/api/stats/split", (long charId) =>
        {
            var from = Util.UtcNow.AddDays(-7);
            var to = Util.UtcNow;

            double trading = 0, bounty = 0, other = 0;
            using (var c = Db.Open())
            using (var cmd = Db.Cmd(c, @"
SELECT ref_type, SUM(amount) FROM journal
WHERE character_id=$c AND date_utc >= $f AND amount > 0
  AND ref_type NOT IN ('market_escrow')
GROUP BY ref_type", ("$c", charId), ("$f", Util.ToIso(from))))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    var rt = r.GetString(0);
                    var v = r.GetDouble(1);
                    if (rt == "market_transaction") trading += v;
                    else if (rt.StartsWith("bounty") || rt == "ess_escrow_transfer" ||
                             rt.StartsWith("agent_mission") || rt.StartsWith("mission") ||
                             rt == "insurance" || rt.StartsWith("corporate_reward")) bounty += v;
                    else other += v;
                }

            var mining = Analytics.Mining(charId, from, to).TotalValue;

            return Results.Ok(new[]
            {
                new { label = "Trading", value = trading },
                new { label = "Bounties & Loot", value = bounty },
                new { label = "Mining", value = mining },
                new { label = "Sonstiges", value = other },
            });
        });

        // Kills & Verluste im Zeitraum, Werte von zKillboard, Links dorthin
        app.MapGet("/api/kills", (long charId, string range) =>
        {
            var (from, to) = ParseRange(range);
            var names = Analytics.Names();
            var rows = new List<object>();
            double destroyed = 0, lost = 0;
            int killCount = 0, lossCount = 0;

            using var c = Db.Open();
            using var cmd = Db.Cmd(c, @"
SELECT killmail_id, time_utc, is_loss, victim_ship_type_id, victim_char_id, solar_system_id, value
FROM kills WHERE character_id=$c AND time_utc >= $f AND time_utc <= $t
ORDER BY time_utc DESC LIMIT 200",
                ("$c", charId), ("$f", Util.ToIso(from)), ("$t", Util.ToIso(to)));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var isLoss = r.GetInt64(2) == 1;
                var value = r.IsDBNull(6) ? 0 : r.GetDouble(6);
                if (isLoss) { lossCount++; lost += value; } else { killCount++; destroyed += value; }
                rows.Add(new
                {
                    killmailId = r.GetInt64(0),
                    time = r.GetString(1),
                    isLoss,
                    ship = names.GetValueOrDefault(r.IsDBNull(3) ? 0 : r.GetInt64(3), "?"),
                    victim = names.GetValueOrDefault(r.IsDBNull(4) ? 0 : r.GetInt64(4), ""),
                    system = names.GetValueOrDefault(r.IsDBNull(5) ? 0 : r.GetInt64(5), "?"),
                    value,
                });
            }
            return Results.Ok(new { killCount, lossCount, destroyed, lost, rows });
        });

        // Erz-Tabelle im Stil von ore.cerlestes.de: ISK/m³ mit Jita-Preisen.
        // Komprimiert ist 1:1 (1 Erz -> 1 komprimiertes) — der Wert pro GESCHÜRFTEM m³
        // mit Komprimiert-Preis ist daher compPrice / rawVolume.
        app.MapGet("/api/ores", () =>
        {
            static string SecOf(string group, string name)
            {
                if (group.Contains("Moon", StringComparison.OrdinalIgnoreCase)) return "MOON";
                if (group.Contains("Ice", StringComparison.OrdinalIgnoreCase)) return "ICE";
                if (group.Contains("Abyssal", StringComparison.OrdinalIgnoreCase)) return "TRIG";
                string[] trig = { "Bezdnacine", "Rakovene", "Talassonite" };
                foreach (var f in trig) if (name.Contains(f)) return "TRIG";
                string[] hs = { "Veldspar", "Scordite", "Pyroxeres", "Plagioclase", "Omber", "Kernite" };
                string[] ls = { "Jaspet", "Hemorphite", "Hedbergite", "Mordunium" };
                string[] ns = { "Gneiss", "Dark Ochre", "Spodumain", "Crokite", "Bistot", "Arkonor",
                                "Mercoxit", "Griemeer", "Hezorime", "Nocxite", "Ueasoh", "Ytirium" };
                foreach (var f in hs) if (name.Contains(f)) return "HS";
                foreach (var f in ls) if (name.Contains(f)) return "LS";
                foreach (var f in ns) if (name.Contains(f)) return "NS";
                return "";
            }

            // alles einlesen: Typen + Preise, dann Roh <-> Komprimiert über den Namen koppeln
            var types = new List<(long Id, string Name, string Group, double Vol, bool Comp)>();
            var prices = new Dictionary<long, (double? Sell, double? Buy)>();
            string updated = null;
            using (var c = Db.Open())
            {
                using (var cmd = Db.Cmd(c, "SELECT type_id,name,group_name,volume,is_compressed FROM ore_types"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        types.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetInt64(4) == 1));
                using (var cmd = Db.Cmd(c, "SELECT type_id,jita_sell,jita_buy,MAX(updated_utc) OVER () FROM ore_prices"))
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        prices[r.GetInt64(0)] = (r.IsDBNull(1) ? null : r.GetDouble(1), r.IsDBNull(2) ? null : r.GetDouble(2));
                        updated ??= r.IsDBNull(3) ? null : r.GetString(3);
                    }
            }

            var compByName = types.Where(t => t.Comp).ToDictionary(t => t.Name, t => t);
            var rows = new List<object>();
            foreach (var t in types.Where(t => !t.Comp))
            {
                prices.TryGetValue(t.Id, out var rawP);
                (double? Sell, double? Buy) compP = (null, null);
                long? compId = null;
                if (compByName.TryGetValue("Compressed " + t.Name, out var comp))
                {
                    compId = comp.Id;
                    prices.TryGetValue(comp.Id, out compP);
                }
                rows.Add(new
                {
                    typeId = t.Id,
                    name = t.Name,
                    group = t.Group,
                    sec = SecOf(t.Group, t.Name),
                    volume = t.Vol,
                    rawSell = rawP.Sell, rawBuy = rawP.Buy,
                    compSell = compP.Sell, compBuy = compP.Buy,
                    compTypeId = compId,
                    m3RawSell = rawP.Sell / t.Vol, m3RawBuy = rawP.Buy / t.Vol,
                    m3CompSell = compP.Sell / t.Vol, m3CompBuy = compP.Buy / t.Vol,
                });
            }
            return Results.Ok(new { updated, rows });
        });

        app.MapGet("/api/mining/today", (long charId) =>
        {
            var rep = Analytics.Mining(charId, Util.UtcNow.Date, Util.UtcNow);
            return Results.Ok(new
            {
                total = rep.TotalValue,
                units = rep.TotalUnits,
                ores = rep.Lines.Take(4).Select(l => new { name = l.Name, qty = l.Quantity, value = l.Value }),
            });
        });

        // Wallet-Journal als CSV (Excel-tauglich mit Semikolon)
        app.MapGet("/api/wallet/export.csv", (long charId) =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Datum;Typ;Beschreibung;Betrag;Kontostand");
            using var c = Db.Open();
            using var cmd = Db.Cmd(c, @"
SELECT date_utc, ref_type, description, amount, balance FROM journal
WHERE character_id=$c ORDER BY date_utc DESC", ("$c", charId));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var desc = (r.IsDBNull(2) ? "" : r.GetString(2)).Replace(';', ',');
                sb.AppendLine(string.Join(';',
                    r.GetString(0), r.GetString(1), desc,
                    r.IsDBNull(3) ? "" : r.GetDouble(3).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    r.IsDBNull(4) ? "" : r.GetDouble(4).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            }
            return Results.File(System.Text.Encoding.UTF8.GetPreamble()
                    .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
                "text/csv", $"wallet-{charId}.csv");
        });

        // ---- LP-Store-Vergleich ----

        app.MapGet("/api/lp", (long charId, string basis) =>
        {
            var hasScope = SyncService.HasScope(charId, "esi-characters.read_loyalty");
            var corps = LpStore.CorpsWithLp(charId);

            // Bei Bedarf im Hintergrund auffrischen (höchstens alle 5 Minuten anstoßen);
            // die Antwort kommt sofort aus dem Bestand, die Oberfläche pollt nach
            if (hasScope && corps.Count > 0 && !LpStore.Busy &&
                Util.UtcNow - _lpLastKick > TimeSpan.FromMinutes(5))
            {
                _lpLastKick = Util.UtcNow;
                LpStore.KickRefresh(charId);
            }

            var balances = new List<object>();
            var lpByCorp = new Dictionary<long, long>();
            using (var c = Db.Open())
            using (var cmd = Db.Cmd(c, @"
SELECT l.corp_id, COALESCE(n.name, '#' || l.corp_id), l.lp
FROM loyalty l LEFT JOIN names n ON n.id = l.corp_id
WHERE l.character_id=$c AND l.lp > 0 ORDER BY l.lp DESC", ("$c", charId)))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    lpByCorp[r.GetInt64(0)] = r.GetInt64(2);
                    balances.Add(new
                    {
                        corpId = r.GetInt64(0),
                        corp = r.GetString(1),
                        lp = r.GetInt64(2),
                        hidden = Config.IsLpCorpHidden(r.GetInt64(0)),
                    });
                }

            // Angebote ausgeblendeter Corps gar nicht erst bewerten
            var visible = corps.Where(c => !Config.IsLpCorpHidden(c)).ToList();
            var offers = LpStore.LoadOffers(visible);
            var prices = MarketPrices.Load();
            var names = Analytics.Names();

            var sellBasis = basis != "buy";
            var computed = new List<(double? IskPerLp, object Row)>();
            foreach (var o in offers)
            {
                var (iskPerLp, profit, value, reqCost) = LpStore.Evaluate(o, prices, sellBasis);
                lpByCorp.TryGetValue(o.CorpId, out var myLp);
                computed.Add((iskPerLp, new
                {
                    corpId = o.CorpId,
                    corp = names.GetValueOrDefault(o.CorpId, "#" + o.CorpId),
                    typeId = o.TypeId,
                    item = names.GetValueOrDefault(o.TypeId, "#" + o.TypeId),
                    qty = o.Quantity,
                    lpCost = o.LpCost,
                    iskCost = o.IskCost,
                    reqCost,
                    value,
                    profit,
                    iskPerLp,
                    // was deine LP bei diesem Angebot insgesamt wert wären
                    myTotal = iskPerLp.HasValue && o.LpCost > 0
                        ? Math.Floor((double)myLp / o.LpCost) * profit
                        : (double?)null,
                }));
            }

            return Results.Ok(new
            {
                hasScope,
                busy = LpStore.Busy,
                progress = LpStore.Progress,
                error = LpStore.LastError,
                balances,
                rows = computed.OrderByDescending(x => x.IskPerLp ?? double.MinValue)
                               .Select(x => x.Row),
            });
        });

        app.MapPost("/api/lp/refresh", (long charId) =>
        {
            _lpLastKick = Util.UtcNow;
            LpStore.KickRefresh(charId);
            return Results.Ok(new { ok = true });
        });

        // ---- Produkt-Research ----

        app.MapGet("/api/research/search", async (long charId, string q) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3)
                return Results.Ok(new { fuzzy = false, results = Array.Empty<object>() });
            try
            {
                var esi = new EsiClient(Config.Contact);
                var (results, fuzzy) = await Research.SearchAsync(esi, charId, q);
                return Results.Ok(new { fuzzy, results = results.Select(x => new { typeId = x.Id, name = x.Name }) });
            }
            catch (Exception ex) { return Results.Ok(new { fuzzy = false, results = Array.Empty<object>(), error = ex.Message }); }
        });

        app.MapGet("/api/research/item", async (long typeId) =>
        {
            var esi = new EsiClient(Config.Contact);
            var names = await Research.ResolveNames(esi, new[] { typeId });
            var name = names.GetValueOrDefault(typeId, "#" + typeId);
            var hubs = await Research.HubPricesAsync(esi, typeId);
            object industry;
            try { industry = await Research.IndustryAsync(esi, typeId, name); }
            catch { industry = new { found = false }; }
            return Results.Ok(new { typeId, name, hubs, industry });
        });

        // Charakterwechsel in der App: alle Overlays hängen daran und ziehen mit
        app.MapPost("/api/active-char", (long charId) =>
        {
            Config.ActiveCharacterId = charId;
            return Results.Ok(new { ok = true, activeCharId = Config.ActiveCharacterId });
        });

        app.MapPost("/api/show-window", () => { ShowWindow?.Invoke(); return Results.Ok(); });

        // ---- Game-Overlay (In-Game-HUD mit DPS-Graph) ----

        // Live-Schadensdaten aus dem EVE-Game-Log. Der erste Aufruf weckt den
        // Log-Mitleser; ohne Abfragen legt er sich nach 5 Minuten wieder schlafen.
        // demo=1 liefert simulierte Kurven — für die Widget-Vorschau in der App,
        // damit man den Graphen auch ohne laufenden Kampf beurteilen kann.
        app.MapGet("/api/dps", ([FromQuery(Name = "charId")] long? charIdOpt, int? window, int? demo) =>
        {
            var charId = ResolveChar(charIdOpt);
            if (demo == 1)
            {
                var win = Math.Clamp(window ?? 180, 30, 900);
                var end = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var demoDealt = new long[win];
                var demoTaken = new long[win];
                for (var i = 0; i < win; i++)
                {
                    // deterministisch aus der absoluten Sekunde, damit die Kurven beim
                    // Sekunden-Polling ruhig weiterlaufen statt zufällig zu flackern
                    var ts = end - win + 1 + i;
                    demoDealt[i] = ts % 4 == 0 ? 380 + ts * 37 % 240 : (ts % 4 == 2 ? 140 : 0);
                    demoTaken[i] = 70 + ts * 17 % 110 + (ts % 45 < 8 ? 210 : 0);
                }
                return Results.Ok(new
                {
                    tracking = true,
                    file = "demo",
                    dir = "",
                    dirExists = true,
                    now = Util.NowIso(),
                    dealt = demoDealt,
                    taken = demoTaken,
                    totalDealt = 84911L,
                    totalTaken = 7442L,
                    lastEvent = Util.NowIso(),
                    listener = "DEMO",
                    fileCharId = 0L,
                    modules = Config.GameOverlayModules.Split(','),
                    layout = Config.GameOverlayLayout,
                    lang = Config.Lang,
                });
            }
            var name = Db.Scalar("SELECT name FROM characters WHERE character_id=$c", ("$c", charId));
            var s = CombatTracker.Snapshot(charId,
                name == null || name == DBNull.Value ? "" : (string)name,
                Math.Clamp(window ?? 180, 30, 900));
            return Results.Ok(new
            {
                tracking = s.Tracking,
                file = s.File,
                dir = s.Dir,
                dirExists = s.DirExists,
                now = Util.ToIso(s.NowUtc),
                dealt = s.Dealt,
                taken = s.Taken,
                totalDealt = s.TotalDealt,
                totalTaken = s.TotalTaken,
                lastEvent = s.LastEventUtc == default ? null : Util.ToIso(s.LastEventUtc),
                // wessen Log gerade mitgelesen wird — folgt automatisch dem aktiven Client;
                // fehlt der Name im Log-Kopf, hilft die Charakter-Tabelle über die ID aus
                listener = s.Listener ?? (s.FileCharId > 0
                    ? Db.Scalar("SELECT name FROM characters WHERE character_id=$c", ("$c", s.FileCharId)) as string
                    : null),
                fileCharId = s.FileCharId,
                // Modul-Auswahl und Sprache wandern mit, damit das Overlay ohne
                // zweiten Endpunkt auskommt und Änderungen binnen Sekunden greifen
                modules = Config.GameOverlayModules.Split(','),
                layout = Config.GameOverlayLayout,
                lang = Config.Lang,
            });
        });

        app.MapPost("/api/gameoverlay/show", (string on) =>
        {
            SetGameOverlay?.Invoke(on != "0");
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/gameoverlay/movemode", (string on) =>
        {
            SetGameOverlayMove?.Invoke(on != "0");
            return Results.Ok(new { ok = true });
        });

        // Wann ist der angezeigte Stand entstanden, wann kommt frischer Nachschub?
        // serverNow wird mitgeliefert, damit der Countdown im Client nicht an einer
        // schief gehenden PC-Uhr hängt.
        app.MapGet("/api/sync-info", (long charId) =>
        {
            string Iso(DateTime? d) => d.HasValue ? Util.ToIso(d.Value) : null;
            var wallet = Db.LastSync(charId, "wallet");
            var journal = Db.LastSync(charId, "journal");
            return Results.Ok(new
            {
                serverNow = Util.NowIso(),
                walletLast = Iso(wallet),
                walletNext = Iso(wallet?.AddSeconds(120)),
                journalLast = Iso(journal),
                journalNext = Iso(journal?.AddSeconds(3600)),
            });
        });

        app.MapGet("/", () => ServeEmbedded("index.html"));
        app.MapGet("/overlay", () => ServeEmbedded("overlay.html"));
        app.MapGet("/gameoverlay", () => ServeEmbedded("gameoverlay.html"));
        app.MapGet("/dpswidget", () => ServeEmbedded("dpswidget.html"));
        app.MapGet("/{file}", (string file) => ServeEmbedded(file));
    }

    /// <summary>Übergebene Charakter-ID, sonst der in der App gewählte (Overlays folgen so mit).</summary>
    private static long ResolveChar(long? id) =>
        id.HasValue && id.Value > 0 ? id.Value : Config.ActiveCharacterId;

    private static (DateTime From, DateTime To) ParseRange(string range)
    {
        var now = Util.UtcNow;
        var from = (range ?? "30d") switch
        {
            "today" => now.Date,
            "7d" => now.AddDays(-7),
            "30d" => now.AddDays(-30),
            "90d" => now.AddDays(-90),
            "all" => new DateTime(2003, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => now.AddDays(-30),
        };
        return (DateTime.SpecifyKind(from, DateTimeKind.Utc), now);
    }

    private static IResult ServeEmbedded(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) file = "index.html";
        if (file.Contains("..") || file.Contains('/') || file.Contains('\\')) return Results.NotFound();

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream($"EveIskTracker.wwwroot.{file}");
        if (stream == null) return Results.NotFound();

        using var reader = new StreamReader(stream);
        var body = reader.ReadToEnd();

        // Nach App-Updates sollen OBS-Browserquellen und WebView2 sofort die neue
        // Oberfläche laden. Wichtig: Die Kennung muss sich pro BUILD ändern, nicht
        // pro Versionsnummer — sonst klebt der Cache zwischen zwei 0.1.0-Builds an
        // alten Dateien. Die ModuleVersionId ist bei jedem Kompilat neu.
        var ver = asm.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 8);
        if (file.EndsWith(".html"))
            body = body.Replace("href=\"app.css\"", $"href=\"app.css?v={ver}\"")
                       .Replace("src=\"app.js\"", $"src=\"app.js?v={ver}\"");

        var mime = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            _ => "text/plain; charset=utf-8",
        };

        // HTML nie cachen (OBS hält Seiten sonst über App-Updates hinweg fest);
        // CSS/JS dürfen dank Versions-Query lange gecacht werden
        var res = Results.Content(body, mime);
        return new NoCacheResult(res, file.EndsWith(".html"));
    }

    /// <summary>Setzt Cache-Header um ein bestehendes Result herum.</summary>
    private sealed class NoCacheResult : IResult
    {
        private readonly IResult _inner;
        private readonly bool _noStore;
        public NoCacheResult(IResult inner, bool noStore) { _inner = inner; _noStore = noStore; }
        public Task ExecuteAsync(HttpContext ctx)
        {
            ctx.Response.Headers.CacheControl = _noStore
                ? "no-store, no-cache, must-revalidate"
                : "public, max-age=31536000, immutable";
            return _inner.ExecuteAsync(ctx);
        }
    }

    private static string HtmlPage(string inner) =>
        "<!doctype html><html lang='de'><head><meta charset='utf-8'><title>EVE ISK Tracker</title>" +
        "<style>body{background:#12161c;color:#e8eef7;font-family:Segoe UI,system-ui,sans-serif;" +
        "display:flex;align-items:center;justify-content:center;height:100vh;margin:0;text-align:center}" +
        "h1{color:#6cb2f5}a{color:#6cb2f5}pre{white-space:pre-wrap;text-align:left;background:#1b2028;" +
        "padding:12px;border-radius:8px;max-width:600px}</style></head><body><div>" + inner + "</div></body></html>";
}
