using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EveIskTracker;

/// <summary>
/// Holt turnusmäßig alle Daten von ESI und legt sie dauerhaft ab.
/// Die Intervalle entsprechen den Cache-Zeiten aus CCPs Spezifikation — häufiger zu fragen
/// bringt nichts außer Last, weil ESI bis dahin ohnehin dieselbe Antwort ausliefert.
/// </summary>
public class SyncService : BackgroundService
{
    private readonly EsiClient _esi;
    private readonly ILogger<SyncService> _log;

    public static volatile string LastMessage = "noch nicht gelaufen";
    public static volatile bool Busy;

    // Ressource -> Mindestabstand in Sekunden (aus swagger x-cached-seconds)
    private static readonly (string Res, int Seconds)[] Plan =
    {
        ("wallet",       120),
        ("journal",     3600),
        ("transactions",3600),
        ("orders",      1200),
        ("orderhistory",3600),
        ("mining",       600),
        ("jobs",         300),
        ("kills",        300),
    };

    public SyncService(ILogger<SyncService> log)
    {
        _log = log;
        _esi = new EsiClient(Config.Contact);
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Kurz warten, damit der Webserver zuerst oben ist.
        await Task.Delay(2000, stop);
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnce(stop); }
            catch (Exception ex) { _log.LogError(ex, "Sync-Durchlauf fehlgeschlagen"); LastMessage = "Fehler: " + ex.Message; }
            // Kurzer Prüftakt, damit frische CCP-Daten zeitnah abgeholt werden.
            // Die eigentlichen Abrufintervalle je Ressource (Plan oben) bleiben unberührt —
            // zwischen zwei echten Abrufen passiert hier nichts außer einem Zeitvergleich.
            await Task.Delay(TimeSpan.FromSeconds(20), stop);
        }
    }

    public async Task RunOnce(CancellationToken stop = default, bool force = false)
    {
        if (!Config.IsConfigured) { LastMessage = "warte auf Client-ID"; return; }
        Busy = true;
        try
        {
            await SyncPrices(force);

            foreach (var charId in EnabledCharacters())
            {
                string token;
                try { token = await Sso.GetAccessTokenAsync(charId); }
                catch (Exception ex) { Db.MarkSync(charId, "token", ex.Message); continue; }

                foreach (var (res, seconds) in Plan)
                {
                    if (stop.IsCancellationRequested) return;
                    var last = Db.LastSync(charId, res);
                    if (!force && last.HasValue && (Util.UtcNow - last.Value).TotalSeconds < seconds) continue;

                    try
                    {
                        switch (res)
                        {
                            case "wallet": await SyncWallet(charId, token); break;
                            case "journal": await SyncJournal(charId, token); break;
                            case "transactions": await SyncTransactions(charId, token); break;
                            case "orders": await SyncOrders(charId, token); break;
                            case "orderhistory": await SyncOrderHistory(charId, token); break;
                            case "mining": await SyncMining(charId, token); break;
                            case "jobs": await SyncJobs(charId, token); break;
                            case "kills": await SyncKills(charId, token); break;
                        }
                        Db.MarkSync(charId, res);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning("{res} für {c} fehlgeschlagen: {m}", res, charId, ex.Message);
                        Db.MarkSync(charId, res, ex.Message);
                    }
                }

                Db.Run("UPDATE characters SET last_sync_utc=$t WHERE character_id=$c",
                       ("$t", Util.NowIso()), ("$c", charId));
            }

            await ResolveMissingNames();
            LastMessage = "zuletzt " + DateTime.Now.ToString("HH:mm:ss");
        }
        finally { Busy = false; }
    }

    /// <summary>
    /// Holt den Kontostand sofort (statt auf den nächsten Turnus zu warten).
    /// Wichtig beim Session-Start: ohne frischen Stand würde 0 als Startwert
    /// gespeichert und die Session zeigte den kompletten Kontostand als Gewinn.
    /// </summary>
    public async Task<double> FetchWalletNow(long charId)
    {
        var token = await Sso.GetAccessTokenAsync(charId);
        var r = await _esi.GetAsync($"/v1/characters/{charId}/wallet/", token);
        if (!r.Ok) throw new EsiException(r.Error);
        var balance = JsonSerializer.Deserialize<double>(r.Body);
        Db.Run("UPDATE characters SET last_balance=$b WHERE character_id=$c",
               ("$b", balance), ("$c", charId));
        Db.MarkSync(charId, "wallet");
        return balance;
    }

    public static List<long> EnabledCharacters()
    {
        var list = new List<long>();
        using var c = Db.Open();
        using var cmd = Db.Cmd(c, "SELECT character_id FROM characters WHERE enabled=1");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }

    // ---------------- einzelne Ressourcen ----------------

    private async Task SyncWallet(long charId, string token)
    {
        var r = await _esi.GetAsync($"/v1/characters/{charId}/wallet/", token);
        if (!r.Ok) throw new EsiException(r.Error);
        var balance = JsonSerializer.Deserialize<double>(r.Body);

        Db.Run("UPDATE characters SET last_balance=$b WHERE character_id=$c",
               ("$b", balance), ("$c", charId));

        Sessions.OnBalance(charId, balance);
    }

    private async Task SyncJournal(long charId, string token)
    {
        var rows = await _esi.GetAllPagesAsync($"/v6/characters/{charId}/wallet/journal/", token);
        using var c = Db.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO journal(character_id,entry_id,date_utc,ref_type,amount,balance,description,reason,tax,context_id,context_id_type,first_party_id,second_party_id)
VALUES($c,$i,$d,$rt,$a,$b,$de,$re,$tx,$ci,$ct,$fp,$sp)
ON CONFLICT(character_id,entry_id) DO NOTHING";
        var p = Bind(cmd, "$c", "$i", "$d", "$rt", "$a", "$b", "$de", "$re", "$tx", "$ci", "$ct", "$fp", "$sp");

        foreach (var e in rows)
        {
            p["$c"].Value = charId;
            p["$i"].Value = e.GetProperty("id").GetInt64();
            p["$d"].Value = Norm(Str(e, "date"));
            p["$rt"].Value = Str(e, "ref_type") ?? "unknown";
            p["$a"].Value = Num(e, "amount");
            p["$b"].Value = Num(e, "balance");
            p["$de"].Value = (object)Str(e, "description") ?? DBNull.Value;
            p["$re"].Value = (object)Str(e, "reason") ?? DBNull.Value;
            p["$tx"].Value = Num(e, "tax");
            p["$ci"].Value = NumL(e, "context_id");
            p["$ct"].Value = (object)Str(e, "context_id_type") ?? DBNull.Value;
            p["$fp"].Value = NumL(e, "first_party_id");
            p["$sp"].Value = NumL(e, "second_party_id");
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Transaktionen paginieren nicht über Seitenzahlen, sondern rückwärts über from_id.
    /// Wir laufen so weit zurück, bis nur noch bereits bekannte Einträge kommen.
    /// </summary>
    private async Task SyncTransactions(long charId, string token)
    {
        long? fromId = null;
        var guard = 0;
        while (guard++ < 25)
        {
            var path = $"/v1/characters/{charId}/wallet/transactions/"
                     + (fromId.HasValue ? $"?from_id={fromId.Value}" : "");
            var r = await _esi.GetAsync(path, token);
            if (!r.Ok) throw new EsiException(r.Error);

            using var doc = JsonDocument.Parse(r.Body);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) break;

            var inserted = 0;
            long min = long.MaxValue;

            using (var c = Db.Open())
            using (var tx = c.BeginTransaction())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO transactions(character_id,transaction_id,date_utc,type_id,location_id,unit_price,quantity,is_buy,client_id,journal_ref_id)
VALUES($c,$t,$d,$ty,$l,$u,$q,$b,$cl,$j)
ON CONFLICT(character_id,transaction_id) DO NOTHING";
                var p = Bind(cmd, "$c", "$t", "$d", "$ty", "$l", "$u", "$q", "$b", "$cl", "$j");
                foreach (var e in arr.EnumerateArray())
                {
                    var id = e.GetProperty("transaction_id").GetInt64();
                    if (id < min) min = id;
                    p["$c"].Value = charId;
                    p["$t"].Value = id;
                    p["$d"].Value = Norm(Str(e, "date"));
                    p["$ty"].Value = e.GetProperty("type_id").GetInt32();
                    p["$l"].Value = NumL(e, "location_id");
                    p["$u"].Value = Num(e, "unit_price");
                    p["$q"].Value = e.GetProperty("quantity").GetInt32();
                    p["$b"].Value = e.GetProperty("is_buy").GetBoolean() ? 1 : 0;
                    p["$cl"].Value = NumL(e, "client_id");
                    p["$j"].Value = NumL(e, "journal_ref_id");
                    inserted += cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }

            // Nichts Neues mehr in dieser Charge -> wir sind auf bekanntem Gebiet angekommen.
            if (inserted == 0) break;
            if (min == long.MaxValue) break;
            fromId = min;
        }
    }

    private async Task SyncOrders(long charId, string token)
    {
        var r = await _esi.GetAsync($"/v2/characters/{charId}/orders/", token);
        if (!r.Ok) throw new EsiException(r.Error);
        using var doc = JsonDocument.Parse(r.Body);

        var seen = new HashSet<long>();
        using (var c = Db.Open())
        using (var tx = c.BeginTransaction())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
INSERT INTO market_orders(character_id,order_id,type_id,region_id,location_id,is_buy,price,volume_total,volume_remain,issued_utc,duration,state,is_open,seen_utc)
VALUES($c,$o,$t,$r,$l,$b,$p,$vt,$vr,$i,$d,'open',1,$s)
ON CONFLICT(character_id,order_id) DO UPDATE SET
  price=$p, volume_remain=$vr, is_open=1, state='open', seen_utc=$s";
            var p = Bind(cmd, "$c", "$o", "$t", "$r", "$l", "$b", "$p", "$vt", "$vr", "$i", "$d", "$s");
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var oid = e.GetProperty("order_id").GetInt64();
                seen.Add(oid);
                p["$c"].Value = charId;
                p["$o"].Value = oid;
                p["$t"].Value = e.GetProperty("type_id").GetInt32();
                p["$r"].Value = NumL(e, "region_id");
                p["$l"].Value = NumL(e, "location_id");
                p["$b"].Value = e.TryGetProperty("is_buy_order", out var ib) && ib.GetBoolean() ? 1 : 0;
                p["$p"].Value = Num(e, "price");
                p["$vt"].Value = NumL(e, "volume_total");
                p["$vr"].Value = NumL(e, "volume_remain");
                p["$i"].Value = Norm(Str(e, "issued"));
                p["$d"].Value = NumL(e, "duration");
                p["$s"].Value = Util.NowIso();
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // Was vorher offen war und jetzt fehlt, ist erfüllt oder storniert.
        var stale = new List<long>();
        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, "SELECT order_id FROM market_orders WHERE character_id=$c AND is_open=1", ("$c", charId)))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read()) { var id = rd.GetInt64(0); if (!seen.Contains(id)) stale.Add(id); }

        foreach (var id in stale)
            Db.Run("UPDATE market_orders SET is_open=0, state='closed' WHERE character_id=$c AND order_id=$o",
                   ("$c", charId), ("$o", id));
    }

    private async Task SyncOrderHistory(long charId, string token)
    {
        var rows = await _esi.GetAllPagesAsync($"/v1/characters/{charId}/orders/history/", token);
        using var c = Db.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO market_orders(character_id,order_id,type_id,region_id,location_id,is_buy,price,volume_total,volume_remain,issued_utc,duration,state,is_open,seen_utc)
VALUES($c,$o,$t,$r,$l,$b,$p,$vt,$vr,$i,$d,$st,0,$s)
ON CONFLICT(character_id,order_id) DO UPDATE SET state=$st, is_open=0, volume_remain=$vr";
        var p = Bind(cmd, "$c", "$o", "$t", "$r", "$l", "$b", "$p", "$vt", "$vr", "$i", "$d", "$st", "$s");
        foreach (var e in rows)
        {
            p["$c"].Value = charId;
            p["$o"].Value = e.GetProperty("order_id").GetInt64();
            p["$t"].Value = e.GetProperty("type_id").GetInt32();
            p["$r"].Value = NumL(e, "region_id");
            p["$l"].Value = NumL(e, "location_id");
            p["$b"].Value = e.TryGetProperty("is_buy_order", out var ib) && ib.GetBoolean() ? 1 : 0;
            p["$p"].Value = Num(e, "price");
            p["$vt"].Value = NumL(e, "volume_total");
            p["$vr"].Value = NumL(e, "volume_remain");
            p["$i"].Value = Norm(Str(e, "issued"));
            p["$d"].Value = NumL(e, "duration");
            p["$st"].Value = (object)Str(e, "state") ?? "closed";
            p["$s"].Value = Util.NowIso();
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Das Mining-Ledger kennt nur Tage, keine Uhrzeiten. Um Erz einer Session zuordnen zu
    /// können, wird jeder Zuwachs gegenüber dem letzten Abruf mit Beobachtungszeit protokolliert.
    /// </summary>
    private async Task SyncMining(long charId, string token)
    {
        var rows = await _esi.GetAllPagesAsync($"/v1/characters/{charId}/mining/", token);

        var previous = new Dictionary<(string, long, long), long>();
        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, "SELECT date_day,solar_system_id,type_id,quantity FROM mining WHERE character_id=$c", ("$c", charId)))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read())
                previous[(rd.GetString(0), rd.GetInt64(1), rd.GetInt64(2))] = rd.GetInt64(3);

        var now = Util.NowIso();
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        using var up = conn.CreateCommand();
        up.CommandText = @"
INSERT INTO mining(character_id,date_day,solar_system_id,type_id,quantity) VALUES($c,$d,$s,$t,$q)
ON CONFLICT(character_id,date_day,solar_system_id,type_id) DO UPDATE SET quantity=$q";
        var pu = Bind(up, "$c", "$d", "$s", "$t", "$q");

        using var del = conn.CreateCommand();
        del.CommandText = @"
INSERT INTO mining_delta(character_id,observed_utc,date_day,solar_system_id,type_id,quantity)
VALUES($c,$o,$d,$s,$t,$q)";
        var pd = Bind(del, "$c", "$o", "$d", "$s", "$t", "$q");

        foreach (var e in rows)
        {
            var day = Str(e, "date");
            var sys = e.GetProperty("solar_system_id").GetInt64();
            var typ = e.GetProperty("type_id").GetInt64();
            var qty = e.GetProperty("quantity").GetInt64();

            previous.TryGetValue((day, sys, typ), out var before);
            var delta = qty - before;

            pu["$c"].Value = charId; pu["$d"].Value = day; pu["$s"].Value = sys;
            pu["$t"].Value = typ; pu["$q"].Value = qty;
            up.ExecuteNonQuery();

            if (delta > 0 && previous.Count > 0)
            {
                pd["$c"].Value = charId; pd["$o"].Value = now; pd["$d"].Value = day;
                pd["$s"].Value = sys; pd["$t"].Value = typ; pd["$q"].Value = delta;
                del.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    private async Task SyncJobs(long charId, string token)
    {
        var r = await _esi.GetAsync($"/v1/characters/{charId}/industry/jobs/?include_completed=true", token);
        if (!r.Ok) throw new EsiException(r.Error);
        using var doc = JsonDocument.Parse(r.Body);

        using var c = Db.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO industry_jobs(character_id,job_id,activity_id,blueprint_type_id,product_type_id,runs,cost,status,start_utc,end_utc,completed_utc,output_location_id)
VALUES($c,$j,$a,$bp,$pt,$r,$co,$st,$s,$e,$cd,$ol)
ON CONFLICT(character_id,job_id) DO UPDATE SET status=$st, completed_utc=$cd";
        var p = Bind(cmd, "$c", "$j", "$a", "$bp", "$pt", "$r", "$co", "$st", "$s", "$e", "$cd", "$ol");
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            p["$c"].Value = charId;
            p["$j"].Value = e.GetProperty("job_id").GetInt64();
            p["$a"].Value = NumL(e, "activity_id");
            p["$bp"].Value = NumL(e, "blueprint_type_id");
            p["$pt"].Value = NumL(e, "product_type_id");
            p["$r"].Value = NumL(e, "runs");
            p["$co"].Value = Num(e, "cost");
            p["$st"].Value = (object)Str(e, "status") ?? "unknown";
            p["$s"].Value = Norm(Str(e, "start_date"));
            p["$e"].Value = Norm(Str(e, "end_date"));
            p["$cd"].Value = (object)Norm(Str(e, "completed_date")) ?? DBNull.Value;
            p["$ol"].Value = NumL(e, "output_location_id");
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Prüft, ob das gespeicherte Token einen Scope enthält.</summary>
    public static bool HasScope(long charId, string scopeFragment)
    {
        var sc = Db.Scalar("SELECT scopes FROM tokens WHERE character_id=$c", ("$c", charId));
        return sc != null && sc != DBNull.Value && ((string)sc).Contains(scopeFragment);
    }

    // zKillboard möchte einen erkennbaren User-Agent; Werte werden je Kill genau einmal geholt
    private static readonly HttpClient Zkb = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static bool _zkbInit;

    /// <summary>
    /// Killmails: Liste (ID+Hash) kommt von ESI, Details vom öffentlichen Killmail-Endpunkt,
    /// der ISK-Wert von zKillboard. Läuft nur, wenn das Token den Killmail-Scope hat —
    /// Bestandslogins von vor der Scope-Erweiterung werden still übersprungen, bis der
    /// Charakter neu angemeldet wird.
    /// </summary>
    private async Task SyncKills(long charId, string token)
    {
        if (!HasScope(charId, "esi-killmails")) return;

        var r = await _esi.GetAsync($"/v1/characters/{charId}/killmails/recent/", token);
        if (!r.Ok) throw new EsiException(r.Error);

        var known = new HashSet<long>();
        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, "SELECT killmail_id FROM kills WHERE character_id=$c", ("$c", charId)))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read()) known.Add(rd.GetInt64(0));

        using var doc = JsonDocument.Parse(r.Body);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var id = e.GetProperty("killmail_id").GetInt64();
            if (known.Contains(id)) continue;
            var hash = e.GetProperty("killmail_hash").GetString();

            // Detail ist öffentlich (ID+Hash sind das Geheimnis)
            var det = await _esi.GetAsync($"/v1/killmails/{id}/{hash}/");
            if (!det.Ok) continue;

            using var dd = JsonDocument.Parse(det.Body);
            var root = dd.RootElement;
            var victim = root.GetProperty("victim");
            var victimChar = victim.TryGetProperty("character_id", out var vc) ? vc.GetInt64() : 0;
            var shipType = victim.TryGetProperty("ship_type_id", out var stp) ? stp.GetInt64() : 0;
            var system = root.TryGetProperty("solar_system_id", out var ss) ? ss.GetInt64() : 0;
            var time = Str(root, "killmail_time");

            var value = await FetchZkbValue(id);

            Db.Run(@"INSERT INTO kills(character_id,killmail_id,hash,time_utc,is_loss,victim_ship_type_id,victim_char_id,solar_system_id,value)
                     VALUES($c,$k,$h,$t,$l,$s,$v,$sy,$val) ON CONFLICT DO NOTHING",
                ("$c", charId), ("$k", id), ("$h", hash),
                ("$t", Norm(time)), ("$l", victimChar == charId ? 1 : 0),
                ("$s", shipType), ("$v", victimChar), ("$sy", system), ("$val", value));
        }
    }

    private async Task<double> FetchZkbValue(long killmailId)
    {
        try
        {
            if (!_zkbInit)
            {
                Zkb.DefaultRequestHeaders.UserAgent.ParseAdd("EveIskTracker/1.0");
                var contact = Config.Contact;
                if (!string.IsNullOrWhiteSpace(contact))
                    Zkb.DefaultRequestHeaders.Add("X-User-Agent", $"EveIskTracker/1.0 ({contact.Trim()})");
                _zkbInit = true;
            }
            var body = await Zkb.GetStringAsync($"https://zkillboard.com/api/killID/{killmailId}/");
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var zkb = doc.RootElement[0].GetProperty("zkb");
                if (zkb.TryGetProperty("totalValue", out var tv)) return tv.GetDouble();
            }
        }
        catch { /* zKillboard nicht erreichbar: Wert bleibt 0, Kill wird trotzdem gelistet */ }
        return 0;
    }

    private async Task SyncPrices(bool force)
    {
        var last = Db.LastSync(0, "prices");
        if (!force && last.HasValue && (Util.UtcNow - last.Value).TotalSeconds < 3600) return;

        var r = await _esi.GetAsync("/v1/markets/prices/");
        if (!r.Ok) { Db.MarkSync(0, "prices", r.Error); return; }
        using var doc = JsonDocument.Parse(r.Body);

        using var c = Db.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO prices(type_id,average_price,adjusted_price,updated_utc) VALUES($t,$a,$j,$u)
ON CONFLICT(type_id) DO UPDATE SET average_price=$a, adjusted_price=$j, updated_utc=$u";
        var p = Bind(cmd, "$t", "$a", "$j", "$u");
        var now = Util.NowIso();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            p["$t"].Value = e.GetProperty("type_id").GetInt32();
            p["$a"].Value = Num(e, "average_price");
            p["$j"].Value = Num(e, "adjusted_price");
            p["$u"].Value = now;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        Db.MarkSync(0, "prices");
    }

    /// <summary>Unbekannte IDs in Namen auflösen — /universe/names/ nimmt bis zu 1000 auf einmal.</summary>
    public async Task ResolveMissingNames()
    {
        var missing = new List<long>();
        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, @"
SELECT DISTINCT id FROM (
    SELECT type_id AS id FROM transactions
    UNION SELECT type_id FROM mining
    UNION SELECT type_id FROM market_orders
    UNION SELECT solar_system_id FROM mining
    UNION SELECT product_type_id FROM industry_jobs WHERE product_type_id IS NOT NULL
    UNION SELECT blueprint_type_id FROM industry_jobs WHERE blueprint_type_id IS NOT NULL
    UNION SELECT victim_ship_type_id FROM kills WHERE victim_ship_type_id IS NOT NULL
    UNION SELECT victim_char_id FROM kills WHERE victim_char_id IS NOT NULL
    UNION SELECT solar_system_id FROM kills WHERE solar_system_id IS NOT NULL
) WHERE id IS NOT NULL AND id > 0 AND id NOT IN (SELECT id FROM names) LIMIT 3000"))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read()) missing.Add(rd.GetInt64(0));

        foreach (var batch in missing.Chunk(1000))
        {
            var r = await _esi.PostAsync("/v3/universe/names/", JsonSerializer.Serialize(batch));
            if (!r.Ok) return;
            using var doc = JsonDocument.Parse(r.Body);
            using var c = Db.Open();
            using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO names(id,name,category) VALUES($i,$n,$c) ON CONFLICT(id) DO UPDATE SET name=$n";
            var p = Bind(cmd, "$i", "$n", "$c");
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                p["$i"].Value = e.GetProperty("id").GetInt64();
                p["$n"].Value = Str(e, "name") ?? "?";
                p["$c"].Value = (object)Str(e, "category") ?? "";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    // ---------------- kleine Helfer ----------------

    private static Dictionary<string, SqliteParameter> Bind(SqliteCommand cmd, params string[] names)
    {
        var d = new Dictionary<string, SqliteParameter>();
        foreach (var n in names) d[n] = cmd.Parameters.Add(n, SqliteType.Text);
        return d;
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0d;

    private static object NumL(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : DBNull.Value;

    /// <summary>ESI-Datum auf ein sortierbares ISO-Format bringen (SQLite vergleicht Text).</summary>
    private static string Norm(string iso) =>
        string.IsNullOrEmpty(iso) ? null : Util.ToIso(Util.ParseIso(iso));
}
