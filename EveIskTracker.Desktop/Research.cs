using System.Text.Json;

namespace EveIskTracker;

/// <summary>
/// Produkt-Research: Wo verkauft sich ein Item am teuersten (die fünf großen
/// Handels-Hubs im Vergleich), und was kostet es, es selbst zu bauen?
/// Marktdaten von ESI; Blueprint-Materialien vom öffentlichen EVE-Ref-Datendienst
/// (ESI liefert keine Baupläne) — gleiche Kategorie externer Quelle wie zKillboard.
/// </summary>
public static class Research
{
    // Hub -> Region (Preise regionsweit; die Hubs dominieren ihre Region ohnehin)
    public static readonly (string Hub, long Region)[] Hubs =
    {
        ("Jita",    10000002),   // The Forge
        ("Amarr",   10000043),   // Domain
        ("Dodixie", 10000032),   // Sinq Laison
        ("Rens",    10000030),   // Heimatar
        ("Hek",     10000042),   // Metropolis
    };

    private static readonly HttpClient RefData = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static bool _refInit;

    /// <summary>
    /// Item-Suche: mit Search-Scope echte Fuzzy-Suche über ESI, sonst exakter
    /// Namenstreffer über /universe/ids als Rückfallebene.
    /// </summary>
    public static async Task<(List<(long Id, string Name)> Results, bool Fuzzy)> SearchAsync(
        EsiClient esi, long charId, string query)
    {
        var ids = new List<long>();
        var fuzzy = SyncService.HasScope(charId, "esi-search");
        if (fuzzy)
        {
            var token = await Sso.GetAccessTokenAsync(charId);
            var r = await esi.GetAsync(
                $"/v3/characters/{charId}/search/?categories=inventory_type&search={Uri.EscapeDataString(query)}",
                token);
            if (r.Ok)
            {
                using var doc = JsonDocument.Parse(r.Body);
                if (doc.RootElement.TryGetProperty("inventory_type", out var arr))
                    foreach (var e in arr.EnumerateArray())
                    {
                        // großzügig einsammeln — ESI liefert unsortiert, erst nach der
                        // Namensauflösung wird sinnvoll gerankt (sonst verdrängen z.B.
                        // dutzende "Rifter … SKIN"-Treffer das eigentliche Schiff)
                        ids.Add(e.GetInt64());
                        if (ids.Count >= 150) break;
                    }
            }
        }
        else
        {
            var r = await esi.PostAsync("/v1/universe/ids/", JsonSerializer.Serialize(new[] { query.Trim() }));
            if (r.Ok)
            {
                using var doc = JsonDocument.Parse(r.Body);
                if (doc.RootElement.TryGetProperty("inventory_types", out var arr))
                    foreach (var e in arr.EnumerateArray())
                        ids.Add(e.GetProperty("id").GetInt64());
            }
        }

        var names = await ResolveNames(esi, ids);
        var q = query.Trim();
        var ranked = ids.Where(names.ContainsKey)
            .Select(i => (Id: i, Name: names[i]))
            .OrderBy(x => string.Equals(x.Name, q, StringComparison.OrdinalIgnoreCase) ? 0
                        : x.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(x => x.Name.Length)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
        return (ranked, fuzzy);
    }

    /// <summary>
    /// Beste Sell-/Buy-Preise, Ordervolumen und die Station der jeweils besten Order
    /// je Hub. Alle Order-Seiten werden gelesen — ESI sortiert nicht nach Preis,
    /// bei >1000 Orders wäre Seite 1 allein also ein Glücksspiel.
    /// </summary>
    public static async Task<List<object>> HubPricesAsync(EsiClient esi, long typeId)
    {
        var raw = await Task.WhenAll(Hubs.Select(async h =>
        {
            double? sell = null, buy = null;
            long sellVol = 0, buyVol = 0, sellLoc = 0, buyLoc = 0;
            try
            {
                var orders = await esi.GetAllPagesAsync(
                    $"/v1/markets/{h.Region}/orders/?type_id={typeId}&order_type=all", null);
                foreach (var o in orders)
                {
                    var price = o.GetProperty("price").GetDouble();
                    var vol = o.TryGetProperty("volume_remain", out var v) ? v.GetInt64() : 0;
                    var loc = o.TryGetProperty("location_id", out var l) ? l.GetInt64() : 0;
                    if (o.TryGetProperty("is_buy_order", out var b) && b.GetBoolean())
                    { buyVol += vol; if (buy == null || price > buy) { buy = price; buyLoc = loc; } }
                    else
                    { sellVol += vol; if (sell == null || price < sell) { sell = price; sellLoc = loc; } }
                }
            }
            catch { /* Region gerade nicht erreichbar: Zeile bleibt leer */ }
            return (Hub: h.Hub, Sell: sell, Buy: buy, SellVol: sellVol, BuyVol: buyVol,
                    SellLoc: sellLoc, BuyLoc: buyLoc);
        }));

        // Stationsnamen auflösen. Upwell-Strukturen (13-stellige IDs) gibt /universe/names
        // nicht her — die heißen pauschal so, wie jeder sie im Spiel nennt.
        var stationIds = raw.SelectMany(x => new[] { x.SellLoc, x.BuyLoc })
            .Where(id => id > 0 && id < 1_000_000_000).Distinct().ToList();
        var names = await ResolveNames(esi, stationIds);
        string Loc(long id) => id <= 0 ? null
            : id >= 1_000_000_000_000 ? "Upwell Structure"
            : names.GetValueOrDefault(id, null);

        return raw.Select(x => (object)new
        {
            hub = x.Hub,
            sell = x.Sell,
            buy = x.Buy,
            sellVol = x.SellVol,
            buyVol = x.BuyVol,
            sellStation = Loc(x.SellLoc),
            buyStation = Loc(x.BuyLoc),
        }).ToList();
    }

    /// <summary>
    /// Herstellungskosten: Blueprint heißt in EVE durchgängig "<Produkt> Blueprint" —
    /// darüber die ID auflösen, Materialliste von EVE Ref holen, Zutaten zu
    /// Jita-Sell-Preisen bewerten. Ohne Struktur-/Skill-Boni, also die Obergrenze.
    /// </summary>
    public static async Task<object> IndustryAsync(EsiClient esi, long typeId, string typeName)
    {
        // 1) Blueprint-Typ über den exakten Namen finden
        var idsResp = await esi.PostAsync("/v1/universe/ids/", JsonSerializer.Serialize(new[] { typeName + " Blueprint" }));
        long bpId = 0;
        if (idsResp.Ok)
        {
            using var doc = JsonDocument.Parse(idsResp.Body);
            if (doc.RootElement.TryGetProperty("inventory_types", out var arr) && arr.GetArrayLength() > 0)
                bpId = arr[0].GetProperty("id").GetInt64();
        }
        if (bpId == 0) return new { found = false };

        // 2) Materialien von EVE Ref (öffentliche Referenzdaten, 24h im HTTP-Cache)
        var bpJson = await FetchRefData($"https://ref-data.everef.net/blueprints/{bpId}");
        if (bpJson == null) return new { found = false };

        var mats = new List<(long TypeId, long Qty)>();
        long outQty = 1;
        try
        {
            using var doc = JsonDocument.Parse(bpJson);
            if (!doc.RootElement.TryGetProperty("activities", out var acts) ||
                !acts.TryGetProperty("manufacturing", out var man)) return new { found = false };

            if (man.TryGetProperty("materials", out var m))
                foreach (var e in Entries(m))
                    mats.Add((e.GetProperty("type_id").GetInt64(), e.GetProperty("quantity").GetInt64()));
            if (man.TryGetProperty("products", out var prods))
                foreach (var e in Entries(prods))
                    if (e.GetProperty("type_id").GetInt64() == typeId)
                        outQty = Math.Max(1, e.GetProperty("quantity").GetInt64());
        }
        catch { return new { found = false }; }
        if (mats.Count == 0) return new { found = false };

        // 3) Zutaten bewerten
        await MarketPrices.EnsureAsync(esi, mats.Select(m => m.TypeId).ToList());
        var prices = MarketPrices.Load();
        var names = await ResolveNames(esi, mats.Select(m => m.TypeId).ToList());

        double total = 0;
        var complete = true;
        var lines = new List<object>();
        foreach (var (tid, qty) in mats.OrderByDescending(m => m.Qty))
        {
            prices.TryGetValue(tid, out var p);
            var cost = p.Sell.HasValue ? p.Sell.Value * qty : (double?)null;
            if (cost.HasValue) total += cost.Value; else complete = false;
            lines.Add(new { typeId = tid, name = names.GetValueOrDefault(tid, "#" + tid), qty, unit = p.Sell, cost });
        }

        return new
        {
            found = true,
            blueprintId = bpId,
            outputQty = outQty,
            materials = lines,
            totalCost = complete ? total : (double?)null,
            costPerUnit = complete ? total / outQty : (double?)null,
        };
    }

    /// <summary>EVE-Ref-Daten liefern Sammlungen mal als Objekt (ID -> Eintrag), mal als Array.</summary>
    private static IEnumerable<JsonElement> Entries(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array)
            foreach (var x in e.EnumerateArray()) yield return x;
        else if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject()) yield return p.Value;
    }

    /// <summary>Referenzdaten mit 24h-Ablage im http_cache — Baupläne ändern sich selten.</summary>
    private static async Task<string> FetchRefData(string url)
    {
        var cached = Db.Scalar("SELECT body FROM http_cache WHERE url=$u AND expires_utc > $n",
            ("$u", url), ("$n", Util.NowIso()));
        if (cached != null && cached != DBNull.Value) return (string)cached;

        try
        {
            if (!_refInit)
            {
                RefData.DefaultRequestHeaders.UserAgent.ParseAdd("EveIskTracker/1.0");
                var contact = Config.Contact;
                if (!string.IsNullOrWhiteSpace(contact))
                    RefData.DefaultRequestHeaders.Add("X-User-Agent", $"EveIskTracker/1.0 ({contact.Trim()})");
                _refInit = true;
            }
            var body = await RefData.GetStringAsync(url);
            Db.Run(@"INSERT INTO http_cache(url,etag,body,expires_utc,stored_utc) VALUES($u,NULL,$b,$x,$s)
                     ON CONFLICT(url) DO UPDATE SET body=$b, expires_utc=$x, stored_utc=$s",
                ("$u", url), ("$b", body),
                ("$x", Util.ToIso(Util.UtcNow.AddHours(24))), ("$s", Util.NowIso()));
            return body;
        }
        catch { return null; }
    }

    /// <summary>Namen aus der lokalen Tabelle; Fehlendes über /universe/names nachschlagen.</summary>
    public static async Task<Dictionary<long, string>> ResolveNames(EsiClient esi, IReadOnlyCollection<long> ids)
    {
        var result = new Dictionary<long, string>();
        if (ids.Count == 0) return result;
        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, $"SELECT id, name FROM names WHERE id IN ({string.Join(',', ids.Distinct())})"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) result[r.GetInt64(0)] = r.GetString(1);

        var missing = ids.Distinct().Where(i => !result.ContainsKey(i)).ToList();
        foreach (var batch in missing.Chunk(1000))
        {
            var r = await esi.PostAsync("/v3/universe/names/", JsonSerializer.Serialize(batch));
            if (!r.Ok) continue;
            using var doc = JsonDocument.Parse(r.Body);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = e.GetProperty("id").GetInt64();
                var name = e.GetProperty("name").GetString() ?? "?";
                result[id] = name;
                Db.Run("INSERT INTO names(id,name,category) VALUES($i,$n,$c) ON CONFLICT(id) DO UPDATE SET name=$n",
                    ("$i", id), ("$n", name), ("$c", e.TryGetProperty("category", out var cat) ? cat.GetString() : ""));
            }
        }
        return result;
    }
}
