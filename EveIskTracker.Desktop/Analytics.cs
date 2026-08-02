namespace EveIskTracker;

// ---------- Ergebnistypen ----------

public class ItemProfit
{
    public long TypeId { get; set; }
    public string Name { get; set; }
    public long QuantitySold { get; set; }
    public double Revenue { get; set; }
    public double Cogs { get; set; }
    public double Gross => Revenue - Cogs;
    public double Margin => Revenue > 0 ? Gross / Revenue : 0;
    public long UnmatchedQty { get; set; }
}

public class TradingReport
{
    public double Revenue { get; set; }
    public double Cogs { get; set; }
    public double GrossProfit => Revenue - Cogs;
    public double BrokerFees { get; set; }
    public double SalesTax { get; set; }
    public double NetProfit => GrossProfit - BrokerFees - SalesTax;
    public double BuyVolume { get; set; }
    public long UnmatchedQty { get; set; }
    public int UnmatchedTypes { get; set; }
    public List<ItemProfit> Items { get; set; } = new();
}

public class CategoryLine
{
    public string Category { get; set; }
    public double Amount { get; set; }
    public int Count { get; set; }
}

public class RattingReport
{
    public double Total { get; set; }
    public List<CategoryLine> Lines { get; set; } = new();
}

public class MiningLine
{
    public long TypeId { get; set; }
    public string Name { get; set; }
    public long Quantity { get; set; }
    public double UnitPrice { get; set; }
    public double Value { get; set; }
}

public class MiningReport
{
    public double TotalValue { get; set; }
    public long TotalUnits { get; set; }
    public bool Estimated { get; set; } = true;
    public List<MiningLine> Lines { get; set; } = new();
}

public class IndustryJobLine
{
    public long JobId { get; set; }
    public string Product { get; set; }
    public long Runs { get; set; }
    public double Cost { get; set; }
    public double OutputValue { get; set; }
    public string Status { get; set; }
    public string EndUtc { get; set; }
}

public class IndustryReport
{
    public double TotalCost { get; set; }
    public double TotalOutputValue { get; set; }
    public double Balance => TotalOutputValue - TotalCost;
    public int JobCount { get; set; }
    public List<IndustryJobLine> Jobs { get; set; } = new();
}

/// <summary>
/// Rechnet aus den lokal gesammelten Rohdaten die eigentlichen Kennzahlen.
/// </summary>
public static class Analytics
{
    // ---------- Handel: FIFO ----------

    /// <summary>
    /// Verkäufe werden nach dem First-in-first-out-Prinzip gegen frühere Käufe verrechnet —
    /// so entsteht ein echter Einstandspreis statt einer bloßen Umsatzsumme.
    ///
    /// Wichtig: die Lager-Warteschlange wird über die *gesamte* Historie aufgebaut, gezählt
    /// werden aber nur Verkäufe im gewählten Zeitraum. Sonst hätten Verkäufe am Anfang des
    /// Zeitraums keinen Einstandspreis.
    /// </summary>
    public static TradingReport Trading(long charId, DateTime fromUtc, DateTime toUtc)
    {
        var rep = new TradingReport();

        // Käufe je Typ als verkettete Liste von Posten (Menge, Stückpreis).
        // LinkedList statt Queue, weil ein Posten auch nur teilweise verbraucht werden kann
        // und dann vorne stehen bleiben muss.
        var lots = new Dictionary<long, LinkedList<(long Qty, double Price)>>();
        var perItem = new Dictionary<long, ItemProfit>();

        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, @"
SELECT date_utc, type_id, unit_price, quantity, is_buy
FROM transactions WHERE character_id=$c ORDER BY date_utc ASC, transaction_id ASC", ("$c", charId)))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var date = Util.ParseIso(r.GetString(0));
                var typeId = r.GetInt64(1);
                var price = r.GetDouble(2);
                var qty = r.GetInt64(3);
                var isBuy = r.GetInt64(4) == 1;
                var inRange = date >= fromUtc && date <= toUtc;

                if (isBuy)
                {
                    if (!lots.TryGetValue(typeId, out var q)) lots[typeId] = q = new LinkedList<(long, double)>();
                    q.AddLast((qty, price));
                    if (inRange) rep.BuyVolume += price * qty;
                    continue;
                }

                // Verkauf: Einstandspreis aus den ältesten Posten abtragen
                double cogs = 0;
                long remaining = qty, unmatched = 0;
                lots.TryGetValue(typeId, out var lot);

                while (remaining > 0)
                {
                    if (lot == null || lot.Count == 0) { unmatched = remaining; break; }
                    var node = lot.First;
                    var (lq, lp) = node.Value;
                    var take = Math.Min(lq, remaining);
                    cogs += take * lp;
                    remaining -= take;
                    if (take == lq) lot.RemoveFirst();
                    else node.Value = (lq - take, lp);   // Rest bleibt vorne liegen
                }

                if (!inRange) continue;

                var revenue = price * qty;
                rep.Revenue += revenue;
                rep.Cogs += cogs;
                rep.UnmatchedQty += unmatched;

                if (!perItem.TryGetValue(typeId, out var ip))
                    perItem[typeId] = ip = new ItemProfit { TypeId = typeId };
                ip.QuantitySold += qty;
                ip.Revenue += revenue;
                ip.Cogs += cogs;
                ip.UnmatchedQty += unmatched;
            }
        }

        rep.UnmatchedTypes = perItem.Values.Count(i => i.UnmatchedQty > 0);

        // Gebühren kommen aus dem Journal, nicht aus den Transaktionen
        rep.BrokerFees = Math.Abs(SumRefTypes(charId, fromUtc, toUtc, "brokers_fee"));
        rep.SalesTax = Math.Abs(SumRefTypes(charId, fromUtc, toUtc, "transaction_tax"));

        var names = Names();
        foreach (var ip in perItem.Values) ip.Name = names.GetValueOrDefault(ip.TypeId, $"Typ {ip.TypeId}");
        rep.Items = perItem.Values.OrderByDescending(i => i.Gross).ToList();
        return rep;
    }

    // ---------- Ratting / Missionen ----------

    private static readonly Dictionary<string, string[]> RattingGroups = new()
    {
        ["Bounties"] = new[] { "bounty_prizes", "bounty_prize", "bounty", "bounty_surcharge", "bounty_reimbursement" },
        ["ESS-Auszahlung"] = new[] { "ess_escrow_transfer" },
        // daily_goal_payouts/freelance_jobs_reward: neuere PvE-Belohnungen (AIR Daily
        // Goals, Freelance Jobs) — gehören sinngemäß zu den Missionen
        ["Missionen"] = new[] { "agent_mission_reward", "agent_mission_time_bonus_reward", "mission_reward", "mission_completion",
                                "daily_goal_payouts", "freelance_jobs_reward" },
        ["Versicherung"] = new[] { "insurance" },
        ["Verträge"] = new[] { "contract_reward", "contract_price", "contract_reward_deposited" },
        ["Corp-Auszahlung"] = new[] { "corporate_reward_payout" },
        ["LP-Store"] = new[] { "lp_store" },
        ["Planetary"] = new[] { "planetary_export_tax", "planetary_import_tax", "planetary_construction" },
        ["Abzüge (Steuern/Gebühren)"] = new[] { "bounty_prize_corporation_tax", "agent_mission_reward_corporation_tax", "industry_job_tax", "reprocessing_tax", "market_provider_tax" },
    };

    public static RattingReport Ratting(long charId, DateTime fromUtc, DateTime toUtc)
    {
        var rep = new RattingReport();
        foreach (var (label, refs) in RattingGroups)
        {
            var (sum, count) = SumRefTypesCounted(charId, fromUtc, toUtc, refs);
            if (count == 0) continue;
            rep.Lines.Add(new CategoryLine { Category = label, Amount = sum, Count = count });
            rep.Total += sum;
        }
        rep.Lines = rep.Lines.OrderByDescending(l => Math.Abs(l.Amount)).ToList();
        return rep;
    }

    // ---------- Mining ----------

    public static MiningReport Mining(long charId, DateTime fromUtc, DateTime toUtc)
    {
        var rep = new MiningReport();
        var prices = Prices();
        var names = Names();
        var agg = new Dictionary<long, long>();

        // Ledger ist tagesweise: ganze Tage im Zeitraum zählen.
        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, @"
SELECT type_id, SUM(quantity) FROM mining
WHERE character_id=$c AND date_day >= $f AND date_day <= $t GROUP BY type_id",
            ("$c", charId), ("$f", fromUtc.ToString("yyyy-MM-dd")), ("$t", toUtc.ToString("yyyy-MM-dd"))))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) agg[r.GetInt64(0)] = r.GetInt64(1);

        BuildMining(rep, agg, prices, names);
        return rep;
    }

    /// <summary>Für Sessions: die protokollierten Zuwächse im Zeitfenster, nicht ganze Tage.</summary>
    public static MiningReport MiningWindow(long charId, DateTime fromUtc, DateTime toUtc)
    {
        var rep = new MiningReport();
        var agg = new Dictionary<long, long>();
        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, @"
SELECT type_id, SUM(quantity) FROM mining_delta
WHERE character_id=$c AND observed_utc >= $f AND observed_utc <= $t GROUP BY type_id",
            ("$c", charId), ("$f", Util.ToIso(fromUtc)), ("$t", Util.ToIso(toUtc))))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) agg[r.GetInt64(0)] = r.GetInt64(1);

        BuildMining(rep, agg, Prices(), Names());
        return rep;
    }

    private static void BuildMining(MiningReport rep, Dictionary<long, long> agg,
                                    Dictionary<long, double> prices, Dictionary<long, string> names)
    {
        foreach (var (typeId, qty) in agg)
        {
            var unit = prices.GetValueOrDefault(typeId, 0);
            rep.Lines.Add(new MiningLine
            {
                TypeId = typeId,
                Name = names.GetValueOrDefault(typeId, $"Typ {typeId}"),
                Quantity = qty,
                UnitPrice = unit,
                Value = unit * qty
            });
            rep.TotalUnits += qty;
            rep.TotalValue += unit * qty;
        }
        rep.Lines = rep.Lines.OrderByDescending(l => l.Value).ToList();
    }

    // ---------- Industrie ----------

    public static IndustryReport Industry(long charId, DateTime fromUtc, DateTime toUtc)
    {
        var rep = new IndustryReport();
        var prices = Prices();
        var names = Names();

        using var c = Db.Open();
        using var cmd = Db.Cmd(c, @"
SELECT job_id, product_type_id, runs, cost, status, end_utc
FROM industry_jobs
WHERE character_id=$c AND end_utc >= $f AND end_utc <= $t
ORDER BY end_utc DESC", ("$c", charId), ("$f", Util.ToIso(fromUtc)), ("$t", Util.ToIso(toUtc)));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var productId = r.IsDBNull(1) ? 0 : r.GetInt64(1);
            var runs = r.IsDBNull(2) ? 0 : r.GetInt64(2);
            var cost = r.IsDBNull(3) ? 0 : r.GetDouble(3);
            var outputValue = prices.GetValueOrDefault(productId, 0) * runs;

            rep.Jobs.Add(new IndustryJobLine
            {
                JobId = r.GetInt64(0),
                Product = names.GetValueOrDefault(productId, productId > 0 ? $"Typ {productId}" : "—"),
                Runs = runs,
                Cost = cost,
                OutputValue = outputValue,
                Status = r.IsDBNull(4) ? "" : r.GetString(4),
                EndUtc = r.IsDBNull(5) ? "" : r.GetString(5),
            });
            rep.TotalCost += cost;
            rep.TotalOutputValue += outputValue;
            rep.JobCount++;
        }
        return rep;
    }

    // ---------- gemeinsame Helfer ----------

    public static double SumRefTypes(long charId, DateTime from, DateTime to, params string[] refTypes) =>
        SumRefTypesCounted(charId, from, to, refTypes).Sum;

    public static (double Sum, int Count) SumRefTypesCounted(long charId, DateTime from, DateTime to, params string[] refTypes)
    {
        var placeholders = string.Join(",", refTypes.Select((_, i) => "$r" + i));
        var ps = new List<(string, object)>
        {
            ("$c", charId), ("$f", Util.ToIso(from)), ("$t", Util.ToIso(to))
        };
        for (var i = 0; i < refTypes.Length; i++) ps.Add(("$r" + i, refTypes[i]));

        using var c = Db.Open();
        using var cmd = Db.Cmd(c, $@"
SELECT COALESCE(SUM(amount),0), COUNT(*) FROM journal
WHERE character_id=$c AND date_utc >= $f AND date_utc <= $t AND ref_type IN ({placeholders})",
            ps.ToArray());
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetDouble(0), r.GetInt32(1)) : (0d, 0);
    }

    public static Dictionary<long, string> Names()
    {
        var d = new Dictionary<long, string>();
        using var c = Db.Open();
        using var cmd = Db.Cmd(c, "SELECT id,name FROM names");
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetInt64(0)] = r.GetString(1);
        return d;
    }

    public static Dictionary<long, double> Prices()
    {
        var d = new Dictionary<long, double>();
        using var c = Db.Open();
        using var cmd = Db.Cmd(c, "SELECT type_id, COALESCE(average_price, adjusted_price, 0) FROM prices");
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetInt64(0)] = r.GetDouble(1);
        return d;
    }
}
