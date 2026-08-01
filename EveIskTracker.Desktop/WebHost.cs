using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
       (SELECT COUNT(*) FROM sessions s WHERE s.character_id=ch.character_id AND s.ended_utc IS NULL)
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
                    errors.Add(new { characterId = r.GetInt64(0), resource = r.GetString(1), error = r.GetString(2) });

            return Results.Ok(new
            {
                configured = Config.IsConfigured,
                clientId = Config.ClientId,
                contact = Config.Contact,
                overlayTextPath = Config.OverlayTextPath,
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
            if (form.TryGetValue("clientId", out var cid) && !string.IsNullOrWhiteSpace(cid)) Config.ClientId = cid;
            if (form.TryGetValue("contact", out var ct)) Config.Contact = ct;
            if (form.TryGetValue("overlayTextPath", out var op)) Config.OverlayTextPath = op;
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
                var hours = Math.Max((end - start).TotalHours, 1.0 / 60);
                list.Add(new
                {
                    id = r.GetInt64(0),
                    label = r.IsDBNull(1) ? null : r.GetString(1),
                    startedUtc = r.GetString(2),
                    endedUtc = r.GetString(3),
                    delta = eb - sb,
                    hours,
                    iskPerHour = (eb - sb) / hours,
                });
            }
            return Results.Ok(list);
        });

        // Schlanker Endpunkt für die Browser-Quelle in Streamlabs.
        // Kontostand ist immer dabei, damit das Widget auch ohne Session etwas zeigt.
        app.MapGet("/api/overlay-data", (long charId) =>
        {
            var st = Sessions.Current(charId);
            var balance = Sessions.CurrentBalance(charId);
            var name = Db.Scalar("SELECT name FROM characters WHERE character_id=$c", ("$c", charId));
            return Results.Ok(new
            {
                active = st.Active,
                name = name == null || name == DBNull.Value ? st.CharacterName : (string)name,
                balance,
                delta = st.Delta,
                iskPerHour = st.IskPerHour,
                mining = st.MiningValue,
                hours = st.Hours,
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

        app.MapPost("/api/show-window", () => { ShowWindow?.Invoke(); return Results.Ok(); });

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
        app.MapGet("/{file}", (string file) => ServeEmbedded(file));
    }

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
        var mime = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            _ => "text/plain; charset=utf-8",
        };
        return Results.Content(body, mime);
    }

    private static string HtmlPage(string inner) =>
        "<!doctype html><html lang='de'><head><meta charset='utf-8'><title>EVE ISK Tracker</title>" +
        "<style>body{background:#12161c;color:#e8eef7;font-family:Segoe UI,system-ui,sans-serif;" +
        "display:flex;align-items:center;justify-content:center;height:100vh;margin:0;text-align:center}" +
        "h1{color:#6cb2f5}a{color:#6cb2f5}pre{white-space:pre-wrap;text-align:left;background:#1b2028;" +
        "padding:12px;border-radius:8px;max-width:600px}</style></head><body><div>" + inner + "</div></body></html>";
}
