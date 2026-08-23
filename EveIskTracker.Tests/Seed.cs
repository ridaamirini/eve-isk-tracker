using EveIskTracker;

/// <summary>
/// Legt einen klar erkennbaren Demo-Charakter mit erfundenen Daten an, damit sich die
/// Oberfläche ohne echten EVE-Login begutachten lässt. "unseed" räumt restlos auf.
/// </summary>
public static class Seed
{
    public const long DemoId = 90000001;

    public static void Run(bool create)
    {
        Wipe();
        if (!create) { Console.WriteLine("Demo-Daten entfernt."); return; }

        var now = Util.UtcNow;

        // enabled=0: der Hintergrund-Sync lässt den Demo-Charakter in Ruhe —
        // er hat keinen Login, jeder Versuch würde nur Fehlermeldungen erzeugen
        Db.Run(@"INSERT INTO characters(character_id,name,added_utc,last_sync_utc,last_balance,enabled)
                 VALUES($c,'DEMO Pilot',$t,$t,$b,0)
                 ON CONFLICT(character_id) DO UPDATE SET name='DEMO Pilot', last_balance=$b, enabled=0",
            ("$c", DemoId), ("$t", Util.NowIso()), ("$b", 42_000_000_000d));

        // Schein-Token nur mit Scope-Liste (kein echtes Refresh-Token): unterdrückt
        // die "Scope fehlt"-Hinweise in der Oberfläche; der Sync bleibt via enabled=0 aus
        Db.Run(@"INSERT INTO tokens(character_id,refresh_blob,scopes,updated_utc)
                 VALUES($c,NULL,$s,$t)
                 ON CONFLICT(character_id) DO UPDATE SET scopes=$s",
            ("$c", DemoId), ("$s", string.Join(" ", EveIskTracker.Sso.Scopes)), ("$t", Util.NowIso()));

        // --- Namen und Preise ---
        var items = new (long Id, string Name, double Price)[]
        {
            (34,   "Tritanium",                     6.2),
            (35,   "Pyerite",                      12.5),
            (1230, "Veldspar",                      4.8),
            (17470,"Compressed Veldspar",         480.0),
            (587,  "Rifter",                  1_150_000),
            (24698,"Drake",                  62_000_000),
            (2048, "Damage Control II",        720_000),
            (12058,"Warp Disruptor II",      1_240_000),
            (30000142, "Jita", 0),
        };
        foreach (var (id, name, price) in items)
        {
            Db.Run("INSERT INTO names(id,name,category) VALUES($i,$n,'inventory_type') ON CONFLICT(id) DO UPDATE SET name=$n",
                   ("$i", id), ("$n", name));
            if (price > 0)
                Db.Run(@"INSERT INTO prices(type_id,average_price,adjusted_price,updated_utc) VALUES($t,$p,$p,$u)
                         ON CONFLICT(type_id) DO UPDATE SET average_price=$p", ("$t", id), ("$p", price), ("$u", Util.NowIso()));
        }

        // --- Handel: Käufe und Verkäufe über die letzten Wochen ---
        long tx = 900000;
        void Tx(double daysAgo, long typeId, double price, long qty, bool isBuy) =>
            Db.Run(@"INSERT INTO transactions(character_id,transaction_id,date_utc,type_id,location_id,unit_price,quantity,is_buy,client_id,journal_ref_id)
                     VALUES($c,$t,$d,$ty,60003760,$u,$q,$b,0,0) ON CONFLICT DO NOTHING",
                ("$c", DemoId), ("$t", tx++), ("$d", Util.ToIso(now.AddDays(-daysAgo))),
                ("$ty", typeId), ("$u", price), ("$q", qty), ("$b", isBuy ? 1 : 0));

        Tx(25, 24698, 55_000_000, 4, true);
        Tx(20, 24698, 64_500_000, 3, false);
        Tx(18, 2048, 610_000, 200, true);
        Tx(12, 2048, 745_000, 180, false);
        Tx(15, 12058, 1_050_000, 150, true);
        Tx(9, 12058, 1_310_000, 120, false);
        Tx(8, 587, 900_000, 40, true);
        Tx(4, 587, 1_190_000, 35, false);
        Tx(6, 17470, 410, 12_000, true);
        Tx(2, 17470, 505, 10_500, false);
        Tx(3, 35, 15.5, 400_000, false);       // ohne bekannten Einkauf – zeigt den Hinweis

        // --- Journal ---
        long jr = 800000;
        void J(double daysAgo, string refType, double amount) =>
            Db.Run(@"INSERT INTO journal(character_id,entry_id,date_utc,ref_type,amount,balance)
                     VALUES($c,$i,$d,$r,$a,0) ON CONFLICT DO NOTHING",
                ("$c", DemoId), ("$i", jr++), ("$d", Util.ToIso(now.AddDays(-daysAgo))),
                ("$r", refType), ("$a", amount));

        var rnd = new Random(42);
        for (var d = 0; d < 28; d++)
        {
            J(d + 0.4, "bounty_prizes", 18_000_000 + rnd.Next(0, 14_000_000));
            if (d % 3 == 0) J(d + 0.5, "ess_escrow_transfer", 25_000_000 + rnd.Next(0, 20_000_000));
            if (d % 4 == 0) J(d + 0.6, "agent_mission_reward", 2_400_000 + rnd.Next(0, 3_000_000));
            if (d % 7 == 0) J(d + 0.7, "insurance", 8_900_000);
        }
        J(20, "brokers_fee", -3_480_000);
        J(12, "brokers_fee", -1_260_000);
        J(9, "transaction_tax", -6_140_000);
        J(4, "transaction_tax", -2_080_000);
        J(2, "brokers_fee", -930_000);

        // Buchungen innerhalb der Demo-Session (letzte ~3,5h), damit die
        // Bounty-/Missions-Kacheln des Widgets etwas zu zeigen haben
        J(0.04, "bounty_prizes", 6_800_000);
        J(0.07, "bounty_prizes", 2_500_000);
        J(0.10, "agent_mission_reward", 1_500_000);

        // Markterlöse, damit der Einnahmen-Donut ein Trading-Segment hat
        for (var d = 0; d < 28; d += 2)
        {
            J(d + 0.3, "market_transaction", 22_000_000 + rnd.Next(0, 40_000_000));
            if (d % 6 == 0) J(d + 0.35, "market_transaction", -(4_000_000 + rnd.Next(0, 9_000_000)));
        }

        // Laufende Kontostände nachtragen: vom Endstand rückwärts durch alle Buchungen,
        // damit die Verlaufskurve im Wallet-Chart echt aussieht.
        var entries = new List<(long Id, double Amount)>();
        using (var c = Db.Open())
        using (var cmd = Db.Cmd(c, "SELECT entry_id, COALESCE(amount,0) FROM journal WHERE character_id=$c ORDER BY date_utc DESC, entry_id DESC", ("$c", DemoId)))
        using (var rd = cmd.ExecuteReader())
            while (rd.Read()) entries.Add((rd.GetInt64(0), rd.GetDouble(1)));

        var runBal = 42_000_000_000d;
        using (var c = Db.Open())
        using (var dbtx = c.BeginTransaction())
        {
            using var up = c.CreateCommand();
            up.CommandText = "UPDATE journal SET balance=$b WHERE character_id=$ch AND entry_id=$i";
            var pb = up.Parameters.Add("$b", Microsoft.Data.Sqlite.SqliteType.Real);
            var pc = up.Parameters.Add("$ch", Microsoft.Data.Sqlite.SqliteType.Integer);
            var pi = up.Parameters.Add("$i", Microsoft.Data.Sqlite.SqliteType.Integer);
            foreach (var (id, amount) in entries)
            {
                pb.Value = runBal; pc.Value = DemoId; pi.Value = id;
                up.ExecuteNonQuery();
                runBal -= amount;   // Stand VOR dieser Buchung, für die nächstältere
            }
            dbtx.Commit();
        }

        // --- Mining ---
        for (var d = 0; d < 14; d++)
        {
            var day = now.AddDays(-d).ToString("yyyy-MM-dd");
            Db.Run(@"INSERT INTO mining(character_id,date_day,solar_system_id,type_id,quantity) VALUES($c,$d,30000142,1230,$q)
                     ON CONFLICT(character_id,date_day,solar_system_id,type_id) DO UPDATE SET quantity=$q",
                ("$c", DemoId), ("$d", day), ("$q", 120_000 + rnd.Next(0, 90_000)));
            Db.Run(@"INSERT INTO mining(character_id,date_day,solar_system_id,type_id,quantity) VALUES($c,$d,30000142,17470,$q)
                     ON CONFLICT(character_id,date_day,solar_system_id,type_id) DO UPDATE SET quantity=$q",
                ("$c", DemoId), ("$d", day), ("$q", 900 + rnd.Next(0, 700)));
        }

        // --- Industrie ---
        for (var i = 0; i < 6; i++)
            Db.Run(@"INSERT INTO industry_jobs(character_id,job_id,activity_id,blueprint_type_id,product_type_id,runs,cost,status,start_utc,end_utc)
                     VALUES($c,$j,1,587,587,$r,$co,'delivered',$s,$e) ON CONFLICT DO NOTHING",
                ("$c", DemoId), ("$j", 700000 + i), ("$r", 20 + i * 5),
                ("$co", 4_200_000 + i * 800_000),
                ("$s", Util.ToIso(now.AddDays(-(i * 3 + 4)))), ("$e", Util.ToIso(now.AddDays(-(i * 3 + 2)))));

        // --- Kills (Werte wie von zKillboard geliefert) ---
        Db.Run("INSERT INTO names(id,name,category) VALUES(90000202,'V. Ashkente','character') ON CONFLICT(id) DO NOTHING");
        Db.Run("INSERT INTO names(id,name,category) VALUES(90000203,'K. Dresc','character') ON CONFLICT(id) DO NOTHING");
        var killSeed = new (long Id, double DaysAgo, bool Loss, long Ship, long Victim, double Value)[]
        {
            (91000001, 0.06, false, 24698, 90000202, 62_400_000),   // Drake-Kill in der Session
            (91000002, 0.11, false, 587, 90000203, 8_100_000),      // Rifter-Kill in der Session
            (91000003, 1.3, false, 24698, 90000202, 71_800_000),
            (91000004, 3.7, true, 587, DemoId, 12_600_000),         // eigener Verlust
            (91000005, 6.2, false, 587, 90000203, 5_900_000),
        };
        foreach (var k in killSeed)
            Db.Run(@"INSERT INTO kills(character_id,killmail_id,hash,time_utc,is_loss,victim_ship_type_id,victim_char_id,solar_system_id,value)
                     VALUES($c,$k,'demo',$t,$l,$s,$v,30000142,$val) ON CONFLICT DO NOTHING",
                ("$c", DemoId), ("$k", k.Id), ("$t", Util.ToIso(now.AddDays(-k.DaysAgo))),
                ("$l", k.Loss ? 1 : 0), ("$s", k.Ship), ("$v", k.Victim), ("$val", k.Value));

        // --- laufende Session seit 3,5 Stunden ---
        Db.Run(@"INSERT INTO sessions(character_id,label,started_utc,start_balance)
                 VALUES($c,'Ratting Delve',$t,$b)",
            ("$c", DemoId), ("$t", Util.ToIso(now.AddHours(-3.5))), ("$b", 41_580_000_000d));

        var sid = Convert.ToInt64(Db.Scalar("SELECT MAX(id) FROM sessions WHERE character_id=$c", ("$c", DemoId)));
        for (var m = 0; m <= 210; m += 15)
            Db.Run("INSERT INTO session_samples(session_id,ts_utc,balance) VALUES($s,$t,$b) ON CONFLICT DO NOTHING",
                ("$s", sid), ("$t", Util.ToIso(now.AddHours(-3.5).AddMinutes(m))),
                ("$b", 41_580_000_000 + m * 2_000_000));

        Db.Run(@"INSERT INTO mining_delta(character_id,observed_utc,date_day,solar_system_id,type_id,quantity)
                 VALUES($c,$o,$d,30000142,1230,38000)",
            ("$c", DemoId), ("$o", Util.ToIso(now.AddHours(-2))), ("$d", now.ToString("yyyy-MM-dd")));

        // --- abgeschlossene Sessions ---
        for (var i = 1; i <= 5; i++)
        {
            var start = now.AddDays(-i * 2).AddHours(-4);
            Db.Run(@"INSERT INTO sessions(character_id,label,started_utc,ended_utc,start_balance,end_balance)
                     VALUES($c,$l,$s,$e,$sb,$eb)",
                ("$c", DemoId), ("$l", i % 2 == 0 ? "Mining Nacht" : "Ratting Delve"),
                ("$s", Util.ToIso(start)), ("$e", Util.ToIso(start.AddHours(3 + i * 0.5))),
                ("$sb", 30_000_000_000.0), ("$eb", 30_000_000_000.0 + i * 340_000_000));
        }

        // --- Loyalitaetspunkte + LP-Store-Angebote ---
        // Erfundene Corps/Items im 9000034x-Bereich; updated_utc steht auf "jetzt",
        // damit der Hintergrund-Refresh die Demo-Daten in Ruhe laesst (24h-Cache)
        var lpCorps = new[]
        {
            (Id: 90000301L, Name: "Demo Navy Command", Lp: 184_500L),
            (Id: 90000302L, Name: "Demo Mining Syndicate", Lp: 42_800L),
            (Id: 90000303L, Name: "Demo Relief Society", Lp: 9_150L),
        };
        foreach (var c in lpCorps)
        {
            Db.Run("INSERT INTO names(id,name,category) VALUES($i,$n,'corporation') ON CONFLICT(id) DO UPDATE SET name=$n",
                ("$i", c.Id), ("$n", c.Name));
            Db.Run(@"INSERT INTO loyalty(character_id,corp_id,lp,updated_utc) VALUES($c,$o,$l,$u)
                     ON CONFLICT(character_id,corp_id) DO UPDATE SET lp=$l, updated_utc=$u",
                ("$c", DemoId), ("$o", c.Id), ("$l", c.Lp), ("$u", Util.NowIso()));
        }

        // Angebot: Item, Menge, LP-Kosten, ISK-Kosten, Jita-Sell/Buy je Stueck
        var offers = new (long Corp, long Type, string Name, long Qty, long Lp, double Isk, double Sell, double Buy)[]
        {
            (90000301, 90000341, "Demo Navy Heavy Blaster",  1, 24_000,  9_600_000, 195_000_000, 178_000_000),
            (90000301, 90000342, "Demo Navy Uranium Charge L", 5_000, 2_400, 2_400_000, 3_100, 2_750),
            (90000302, 90000343, "Demo Beancounter Implant", 1, 10_875,  4_350_000, 100_000_000,  91_000_000),
            (90000302, 90000344, "Demo Mining Crystal Set",  10,  1_200,    600_000,   1_450_000,  1_180_000),
            (90000303, 90000345, "Demo Nexus Chip",           1,    500,          0,   2_895_000,  2_400_000),
            (90000303, 90000346, "Demo Combat Booster",       3,  3_600,  1_800_000,   4_900_000,  4_100_000),
        };
        long offerId = 1;
        foreach (var o in offers)
        {
            Db.Run("INSERT INTO names(id,name,category) VALUES($i,$n,'inventory_type') ON CONFLICT(id) DO UPDATE SET name=$n",
                ("$i", o.Type), ("$n", o.Name));
            Db.Run(@"INSERT INTO market_prices(type_id,jita_sell,jita_buy,updated_utc) VALUES($t,$s,$b,$u)
                     ON CONFLICT(type_id) DO UPDATE SET jita_sell=$s, jita_buy=$b, updated_utc=$u",
                ("$t", o.Type), ("$s", o.Sell), ("$b", o.Buy), ("$u", Util.NowIso()));
            Db.Run(@"INSERT INTO lp_offers(corp_id,offer_id,type_id,quantity,lp_cost,isk_cost,required_json,updated_utc)
                     VALUES($c,$o,$t,$q,$l,$i,'[]',$u)
                     ON CONFLICT(corp_id,offer_id) DO UPDATE SET type_id=$t, quantity=$q, lp_cost=$l, isk_cost=$i, updated_utc=$u",
                ("$c", o.Corp), ("$o", offerId++), ("$t", o.Type), ("$q", o.Qty),
                ("$l", o.Lp), ("$i", o.Isk), ("$u", Util.NowIso()));
        }

        Console.WriteLine("Demo-Charakter 'DEMO Pilot' angelegt (ID " + DemoId + ").");
    }

    private static void Wipe()
    {
        foreach (var t in new[] { "transactions", "journal", "mining", "mining_delta", "industry_jobs", "market_orders", "characters", "tokens", "sync_state", "kills" })
            Db.Run($"DELETE FROM {t} WHERE character_id=$c", ("$c", DemoId));
        Db.Run("DELETE FROM names WHERE id IN (90000202, 90000203)");
        Db.Run("DELETE FROM loyalty WHERE character_id=$c", ("$c", DemoId));
        Db.Run("DELETE FROM lp_offers WHERE corp_id BETWEEN 90000301 AND 90000303");
        Db.Run("DELETE FROM market_prices WHERE type_id BETWEEN 90000341 AND 90000346");
        Db.Run("DELETE FROM names WHERE id BETWEEN 90000301 AND 90000346");
        Db.Run("DELETE FROM session_samples WHERE session_id IN (SELECT id FROM sessions WHERE character_id=$c)", ("$c", DemoId));
        Db.Run("DELETE FROM sessions WHERE character_id=$c", ("$c", DemoId));
    }
}
