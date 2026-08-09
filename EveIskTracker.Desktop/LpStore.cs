using System.Text.Json;

namespace EveIskTracker;

/// <summary>
/// Jita-Preise (The Forge) für beliebige Typen — gemeinsame Ablage für LP-Store-Vergleich
/// und Produkt-Research. Ein Request je Typ, gedrosselt über den EsiClient; dank
/// ETag-/Expires-Cache kostet ein erneuter Durchlauf binnen der Stunde fast nichts.
/// </summary>
public static class MarketPrices
{
    public const long JitaRegion = 10000002;

    /// <summary>Preise für alle IDs sicherstellen, die fehlen oder älter als eine Stunde sind.</summary>
    public static async Task EnsureAsync(EsiClient esi, IReadOnlyCollection<long> ids,
        Action<int, int> progress = null)
    {
        var stale = new List<long>();
        var cutoff = Util.ToIso(Util.UtcNow.AddHours(-1));
        var known = new Dictionary<long, string>();
        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, "SELECT type_id, updated_utc FROM market_prices"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) known[r.GetInt64(0)] = r.IsDBNull(1) ? "" : r.GetString(1);

        foreach (var id in ids.Distinct())
            if (!known.TryGetValue(id, out var u) || string.Compare(u, cutoff, StringComparison.Ordinal) < 0)
                stale.Add(id);

        var done = 0;
        var total = stale.Count;
        // Parallel loslassen — die echte Drosselung übernimmt das Semaphor im EsiClient
        await Task.WhenAll(stale.Select(async id =>
        {
            var r = await esi.GetAsync($"/v1/markets/{JitaRegion}/orders/?type_id={id}&order_type=all&page=1");
            if (r.Ok)
            {
                double? sell = null, buy = null;
                using var doc = JsonDocument.Parse(r.Body);
                foreach (var o in doc.RootElement.EnumerateArray())
                {
                    var price = o.TryGetProperty("price", out var pv) ? pv.GetDouble() : 0;
                    var isBuy = o.TryGetProperty("is_buy_order", out var b) && b.GetBoolean();
                    if (isBuy) { if (buy == null || price > buy) buy = price; }
                    else { if (sell == null || price < sell) sell = price; }
                }
                Db.Run(@"INSERT INTO market_prices(type_id,jita_sell,jita_buy,updated_utc) VALUES($t,$s,$b,$u)
                         ON CONFLICT(type_id) DO UPDATE SET jita_sell=$s, jita_buy=$b, updated_utc=$u",
                    ("$t", id), ("$s", (object)sell ?? DBNull.Value),
                    ("$b", (object)buy ?? DBNull.Value), ("$u", Util.NowIso()));
            }
            progress?.Invoke(Interlocked.Increment(ref done), total);
        }));
    }

    /// <summary>Alle gespeicherten Preise als Nachschlagewerk.</summary>
    public static Dictionary<long, (double? Sell, double? Buy)> Load()
    {
        var d = new Dictionary<long, (double?, double?)>();
        using var c = Db.Open();
        using var cmd = Db.Cmd(c, "SELECT type_id, jita_sell, jita_buy FROM market_prices");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            d[r.GetInt64(0)] = (r.IsDBNull(1) ? null : r.GetDouble(1), r.IsDBNull(2) ? null : r.GetDouble(2));
        return d;
    }
}

/// <summary>Ein LP-Store-Angebot mit allem, was die Bewertung braucht.</summary>
public sealed class LpOffer
{
    public long CorpId;
    public long OfferId;
    public long TypeId;
    public long Quantity;
    public long LpCost;
    public double IskCost;
    public List<(long TypeId, long Quantity)> Required = new();
}

/// <summary>
/// LP-Store-Vergleich: bewertet die Angebote aller Corps, bei denen der Charakter
/// Loyalitätspunkte hat, mit Jita-Preisen — Kern jeder Zeile ist ISK pro LP.
/// Nach dem Vorbild von LP-Comparator-Tools, aber mit den eigenen LP-Ständen.
/// </summary>
public static class LpStore
{
    private static readonly object Lock = new();
    private static Task _refresh;
    public static volatile bool Busy;
    public static volatile string Progress = "";
    public static volatile string LastError;

    /// <summary>
    /// Reine Bewertungslogik, testbar ohne Netz: Erlös minus ISK-Kosten minus
    /// Einkauf der benötigten Items, geteilt durch die LP-Kosten. Erlös je nach
    /// Basis zum Sell-Preis (eigene Order) oder Buy-Preis (Sofortverkauf);
    /// benötigte Items werden immer zum Sell-Preis eingekauft.
    /// </summary>
    public static (double? IskPerLp, double? Profit, double? Value, double? ReqCost) Evaluate(
        LpOffer o, IReadOnlyDictionary<long, (double? Sell, double? Buy)> prices, bool sellBasis)
    {
        if (o.LpCost <= 0) return (null, null, null, null);
        if (!prices.TryGetValue(o.TypeId, out var p)) return (null, null, null, null);
        var unit = sellBasis ? p.Sell : p.Buy;
        if (unit == null) return (null, null, null, null);

        double reqCost = 0;
        foreach (var (tid, qty) in o.Required)
        {
            if (!prices.TryGetValue(tid, out var rp) || rp.Sell == null)
                return (null, null, unit * o.Quantity, null);   // Zutat ohne Marktpreis: nicht bewertbar
            reqCost += rp.Sell.Value * qty;
        }

        var value = unit.Value * o.Quantity;
        var profit = value - o.IskCost - reqCost;
        return (profit / o.LpCost, profit, value, reqCost);
    }

    /// <summary>Angebote und Preise im Hintergrund auffrischen (ein Lauf zur Zeit).</summary>
    public static void KickRefresh(long charId)
    {
        lock (Lock)
        {
            if (Busy) return;
            Busy = true;
            LastError = null;
            Progress = "";
            _refresh = Task.Run(async () =>
            {
                try { await RefreshAsync(charId); }
                catch (Exception ex) { LastError = ex.Message; }
                finally { Busy = false; Progress = ""; }
            });
        }
    }

    private static async Task RefreshAsync(long charId)
    {
        var esi = new EsiClient(Config.Contact);
        var corps = CorpsWithLp(charId);
        if (corps.Count == 0) return;

        // 1) Angebote je Corp (24h-Cache — LP-Stores ändern sich praktisch nie)
        foreach (var corp in corps)
        {
            var last = Db.Scalar("SELECT MAX(updated_utc) FROM lp_offers WHERE corp_id=$c", ("$c", corp));
            if (last != null && last != DBNull.Value &&
                Util.ParseIso((string)last) > Util.UtcNow.AddHours(-24)) continue;

            Progress = $"Angebote {corp} …";
            var r = await esi.GetAsync($"/v1/loyalty/stores/{corp}/offers/");
            if (!r.Ok) continue;

            using var doc = JsonDocument.Parse(r.Body);
            using var c = Db.Open();
            using var tx = c.BeginTransaction();
            using (var del = c.CreateCommand())
            {
                del.CommandText = "DELETE FROM lp_offers WHERE corp_id=$c";
                del.Parameters.AddWithValue("$c", corp);
                del.ExecuteNonQuery();
            }
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO lp_offers(corp_id,offer_id,type_id,quantity,lp_cost,isk_cost,required_json,updated_utc)
VALUES($c,$o,$t,$q,$l,$i,$r,$u)";
                foreach (var n in new[] { "$c", "$o", "$t", "$q", "$l", "$i", "$r", "$u" })
                    cmd.Parameters.Add(n, Microsoft.Data.Sqlite.SqliteType.Text);
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    var req = new List<long[]>();
                    if (e.TryGetProperty("required_items", out var ri))
                        foreach (var x in ri.EnumerateArray())
                            req.Add(new[] { x.GetProperty("type_id").GetInt64(), x.GetProperty("quantity").GetInt64() });
                    cmd.Parameters["$c"].Value = corp;
                    cmd.Parameters["$o"].Value = e.GetProperty("offer_id").GetInt64();
                    cmd.Parameters["$t"].Value = e.GetProperty("type_id").GetInt64();
                    cmd.Parameters["$q"].Value = e.GetProperty("quantity").GetInt64();
                    cmd.Parameters["$l"].Value = e.GetProperty("lp_cost").GetInt64();
                    cmd.Parameters["$i"].Value = e.TryGetProperty("isk_cost", out var ic) ? ic.GetDouble() : 0d;
                    cmd.Parameters["$r"].Value = JsonSerializer.Serialize(req);
                    cmd.Parameters["$u"].Value = Util.NowIso();
                    cmd.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }

        // 2) Jita-Preise für alle beteiligten Typen (Ware + Zutaten)
        var types = new HashSet<long>();
        foreach (var o in LoadOffers(corps))
        {
            types.Add(o.TypeId);
            foreach (var (tid, _) in o.Required) types.Add(tid);
        }
        await MarketPrices.EnsureAsync(esi, types.ToList(),
            (done, total) => Progress = $"Preise {done}/{total}");

        // 3) Namen für neue Items/Corps nachziehen
        try { await new SyncService(Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance).ResolveMissingNames(); }
        catch { /* Namen kommen sonst mit dem nächsten Sync */ }
    }

    public static List<long> CorpsWithLp(long charId)
    {
        var list = new List<long>();
        using var c = Db.Open();
        using var cmd = Db.Cmd(c, "SELECT corp_id FROM loyalty WHERE character_id=$c AND lp > 0", ("$c", charId));
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }

    public static List<LpOffer> LoadOffers(IReadOnlyCollection<long> corps)
    {
        var list = new List<LpOffer>();
        if (corps.Count == 0) return list;
        using var c = Db.Open();
        using var cmd = Db.Cmd(c, $@"
SELECT corp_id, offer_id, type_id, quantity, lp_cost, isk_cost, required_json
FROM lp_offers WHERE corp_id IN ({string.Join(',', corps)})");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var o = new LpOffer
            {
                CorpId = r.GetInt64(0),
                OfferId = r.GetInt64(1),
                TypeId = r.GetInt64(2),
                Quantity = r.GetInt64(3),
                LpCost = r.GetInt64(4),
                IskCost = r.GetDouble(5),
            };
            if (!r.IsDBNull(6))
                foreach (var x in JsonSerializer.Deserialize<List<long[]>>(r.GetString(6)))
                    o.Required.Add((x[0], x[1]));
            list.Add(o);
        }
        return list;
    }
}
