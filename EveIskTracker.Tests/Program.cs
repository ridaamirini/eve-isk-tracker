using EveIskTracker;

// Prüft die Rechenlogik gegen von Hand nachgerechnete Beispiele.
// Läuft gegen eine frische Wegwerf-Datenbank, rührt echte Daten nicht an.
//
// Zusätzlich: "seed"/"unseed" legen einen Demo-Charakter in der echten App-Datenbank an
// bzw. entfernen ihn wieder — nur zum Ansehen der Oberfläche ohne EVE-Login.

if (args.Length > 0 && (args[0] == "seed" || args[0] == "unseed"))
{
    Db.Init(Config.DataDir);
    Seed.Run(args[0] == "seed");
    return 0;
}

// Repariert offene Sessions, deren Startwert 0 ist (Session vor dem ersten
// Wallet-Abgleich gestartet): nimmt den Journal-Kontostand zum Startzeitpunkt.
if (args.Length > 0 && args[0] == "fixsessions")
{
    Db.Init(Config.DataDir);
    var fixes = new List<(long Id, long CharId, string Started)>();
    using (var c = Db.Open())
    using (var cmd = Db.Cmd(c, @"
SELECT id, character_id, started_utc FROM sessions
WHERE ended_utc IS NULL AND (start_balance IS NULL OR start_balance = 0)"))
    using (var r = cmd.ExecuteReader())
        while (r.Read()) fixes.Add((r.GetInt64(0), r.GetInt64(1), r.GetString(2)));

    foreach (var (id, charId, started) in fixes)
    {
        var bal = Db.Scalar(@"
SELECT balance FROM journal
WHERE character_id=$c AND date_utc <= $t AND balance IS NOT NULL
ORDER BY date_utc DESC LIMIT 1", ("$c", charId), ("$t", started));
        if (bal == null || bal == DBNull.Value)
        {
            Console.WriteLine($"Session {id}: kein Journal-Stand vor {started} gefunden - unveraendert.");
            continue;
        }
        Db.Run("UPDATE sessions SET start_balance=$b WHERE id=$i", ("$b", (double)bal), ("$i", id));
        Console.WriteLine($"Session {id} (Charakter {charId}): Startwert auf {(double)bal:N0} gesetzt.");
    }
    if (fixes.Count == 0) Console.WriteLine("Keine reparaturbeduerftige Session gefunden.");
    return 0;
}

var tmp = Path.Combine(Path.GetTempPath(), "eveisk_test_" + Guid.NewGuid().ToString("N")[..8]);
Db.Init(tmp);

int passed = 0, failed = 0;
const long C = 1001;          // Test-Charakter
var T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

void Check(string name, double actual, double expected, double tol = 0.001)
{
    var ok = Math.Abs(actual - expected) <= tol;
    Console.WriteLine($"  {(ok ? "OK  " : "FEHL")}  {name,-46} erwartet {expected,14:N2}   erhalten {actual,14:N2}");
    if (ok) passed++; else failed++;
}

void Reset()
{
    Db.Run("DELETE FROM transactions WHERE character_id=$c", ("$c", C));
    Db.Run("DELETE FROM journal WHERE character_id=$c", ("$c", C));
    Db.Run("DELETE FROM mining WHERE character_id=$c", ("$c", C));
    Db.Run("DELETE FROM mining_delta WHERE character_id=$c", ("$c", C));
    Db.Run("DELETE FROM industry_jobs WHERE character_id=$c", ("$c", C));
}

long txSeq = 1;
void Tx(int dayOffset, long typeId, double price, long qty, bool isBuy)
{
    Db.Run(@"INSERT INTO transactions(character_id,transaction_id,date_utc,type_id,location_id,unit_price,quantity,is_buy,client_id,journal_ref_id)
             VALUES($c,$t,$d,$ty,60003760,$u,$q,$b,0,0)",
        ("$c", C), ("$t", txSeq++), ("$d", Util.ToIso(T0.AddDays(dayOffset))),
        ("$ty", typeId), ("$u", price), ("$q", qty), ("$b", isBuy ? 1 : 0));
}

long jrSeq = 1;
void Journal(int dayOffset, string refType, double amount)
{
    Db.Run(@"INSERT INTO journal(character_id,entry_id,date_utc,ref_type,amount,balance)
             VALUES($c,$i,$d,$r,$a,0)",
        ("$c", C), ("$i", jrSeq++), ("$d", Util.ToIso(T0.AddDays(dayOffset))),
        ("$r", refType), ("$a", amount));
}

var Wide = (From: T0.AddDays(-10), To: T0.AddDays(100));

Console.WriteLine("\n=== 1. Einfacher Kauf und Verkauf ===");
Reset();
Tx(1, 34, 10, 100, true);      // 100 Tritanium zu je 10 gekauft
Tx(2, 34, 15, 100, false);     // alle 100 zu je 15 verkauft
{
    var r = Analytics.Trading(C, Wide.From, Wide.To);
    Check("Umsatz", r.Revenue, 1500);
    Check("Wareneinsatz", r.Cogs, 1000);
    Check("Rohertrag", r.GrossProfit, 500);
    Check("ohne Einstand verkauft", r.UnmatchedQty, 0);
}

Console.WriteLine("\n=== 2. Posten nur teilweise verbraucht ===");
Reset();
Tx(1, 34, 10, 100, true);
Tx(2, 34, 20, 30, false);      // erst 30 raus, 70 bleiben im Posten liegen
Tx(3, 34, 20, 50, false);      // dann 50 aus demselben Posten
{
    var r = Analytics.Trading(C, Wide.From, Wide.To);
    Check("Umsatz", r.Revenue, 80 * 20);
    Check("Wareneinsatz", r.Cogs, 80 * 10);
    Check("Rohertrag", r.GrossProfit, 800);
    Check("ohne Einstand verkauft", r.UnmatchedQty, 0);
}

Console.WriteLine("\n=== 3. Reihenfolge: aeltester Posten zuerst ===");
Reset();
Tx(1, 34, 10, 50, true);       // zuerst billig
Tx(2, 34, 20, 50, true);       // dann teuer
Tx(3, 34, 30, 75, false);      // 75 verkauft: 50 aus dem billigen, 25 aus dem teuren
{
    var r = Analytics.Trading(C, Wide.From, Wide.To);
    Check("Umsatz", r.Revenue, 2250);
    Check("Wareneinsatz (50*10 + 25*20)", r.Cogs, 1000);
    Check("Rohertrag", r.GrossProfit, 1250);
}

Console.WriteLine("\n=== 4. Verkauf ohne bekannten Einkauf ===");
Reset();
Tx(2, 34, 5, 10, false);       // Kauf liegt vor dem Datenfenster
{
    var r = Analytics.Trading(C, Wide.From, Wide.To);
    Check("Umsatz", r.Revenue, 50);
    Check("Wareneinsatz", r.Cogs, 0);
    Check("als unbekannt markiert", r.UnmatchedQty, 10);
    Check("betroffene Item-Typen", r.UnmatchedTypes, 1);
}

Console.WriteLine("\n=== 5. Kauf vor dem Zeitraum, Verkauf darin ===");
Reset();
Tx(1, 34, 10, 100, true);      // Tag 1 - ausserhalb des Auswertungsfensters
Tx(50, 34, 25, 100, false);    // Tag 50 - im Fenster
{
    // Fenster erst ab Tag 40: der Einstandspreis muss trotzdem gefunden werden
    var r = Analytics.Trading(C, T0.AddDays(40), T0.AddDays(60));
    Check("Umsatz", r.Revenue, 2500);
    Check("Wareneinsatz aus altem Kauf", r.Cogs, 1000);
    Check("Rohertrag", r.GrossProfit, 1500);
    Check("nichts faelschlich unbekannt", r.UnmatchedQty, 0);
}

Console.WriteLine("\n=== 6. Gebuehren aus dem Journal ===");
Reset();
Tx(1, 34, 10, 100, true);
Tx(2, 34, 15, 100, false);
Journal(2, "brokers_fee", -100);
Journal(2, "transaction_tax", -50);
{
    var r = Analytics.Trading(C, Wide.From, Wide.To);
    Check("Broker-Gebuehr", r.BrokerFees, 100);
    Check("Verkaufssteuer", r.SalesTax, 50);
    Check("Rohertrag", r.GrossProfit, 500);
    Check("Nettogewinn (500-150)", r.NetProfit, 350);
}

Console.WriteLine("\n=== 7. Mehrere Item-Typen getrennt ===");
Reset();
Tx(1, 34, 10, 100, true);
Tx(1, 35, 100, 10, true);
Tx(2, 34, 12, 100, false);     // +200
Tx(2, 35, 90, 10, false);      // -100
{
    var r = Analytics.Trading(C, Wide.From, Wide.To);
    Check("Rohertrag gesamt", r.GrossProfit, 100);
    Check("Anzahl Positionen", r.Items.Count, 2);
    var best = r.Items[0];
    Check("bestes Item ist Typ 34", best.TypeId, 34);
    Check("Gewinn des besten Items", best.Gross, 200);
    Check("Verlust des zweiten Items", r.Items[1].Gross, -100);
}

Console.WriteLine("\n=== 8. Ratting-Kategorien ===");
Reset();
Journal(1, "bounty_prizes", 5_000_000);
Journal(1, "bounty_prizes", 3_000_000);
Journal(2, "ess_escrow_transfer", 12_000_000);
Journal(2, "agent_mission_reward", 1_500_000);
Journal(2, "brokers_fee", -99);        // gehoert nicht zum Ratting
{
    var r = Analytics.Ratting(C, Wide.From, Wide.To);
    Check("Summe", r.Total, 21_500_000);
    var bounty = r.Lines.First(l => l.Category == "Bounties");
    Check("Bounties Betrag", bounty.Amount, 8_000_000);
    Check("Bounties Buchungen", bounty.Count, 2);
    Check("ESS", r.Lines.First(l => l.Category == "ESS-Auszahlung").Amount, 12_000_000);
    Check("Broker nicht enthalten", r.Lines.Count(l => l.Category.Contains("Broker")), 0);
}

Console.WriteLine("\n=== 9. Zeitraumgrenzen ===");
Reset();
Journal(5, "bounty_prizes", 1_000_000);
Journal(15, "bounty_prizes", 2_000_000);
Journal(25, "bounty_prizes", 4_000_000);
{
    var r = Analytics.Ratting(C, T0.AddDays(10), T0.AddDays(20));
    Check("nur die mittlere Buchung", r.Total, 2_000_000);
}

Console.WriteLine("\n=== 10. Mining-Bewertung ===");
Reset();
Db.Run("INSERT INTO prices(type_id,average_price,adjusted_price,updated_utc) VALUES(1230,5.5,5,$u) ON CONFLICT(type_id) DO UPDATE SET average_price=5.5", ("$u", Util.NowIso()));
Db.Run(@"INSERT INTO mining(character_id,date_day,solar_system_id,type_id,quantity) VALUES($c,$d,30000142,1230,10000)
         ON CONFLICT(character_id,date_day,solar_system_id,type_id) DO UPDATE SET quantity=10000",
    ("$c", C), ("$d", T0.AddDays(5).ToString("yyyy-MM-dd")));
{
    var r = Analytics.Mining(C, T0, T0.AddDays(10));
    Check("Menge", r.TotalUnits, 10000);
    Check("Marktwert (10000 * 5,5)", r.TotalValue, 55000);
}

Console.WriteLine("\n=== 11. Session-Fenster beim Mining ===");
Reset();
Db.Run(@"INSERT INTO mining_delta(character_id,observed_utc,date_day,solar_system_id,type_id,quantity)
         VALUES($c,$o,'2026-01-06',30000142,1230,4000)", ("$c", C), ("$o", Util.ToIso(T0.AddDays(5).AddHours(2))));
Db.Run(@"INSERT INTO mining_delta(character_id,observed_utc,date_day,solar_system_id,type_id,quantity)
         VALUES($c,$o,'2026-01-06',30000142,1230,6000)", ("$c", C), ("$o", Util.ToIso(T0.AddDays(5).AddHours(9))));
{
    // Fenster deckt nur den ersten Zuwachs ab
    var r = Analytics.MiningWindow(C, T0.AddDays(5), T0.AddDays(5).AddHours(4));
    Check("nur der Zuwachs im Fenster", r.TotalUnits, 4000);
    Check("Wert", r.TotalValue, 22000);
}

Console.WriteLine("\n=== 12. Industrie-Saldo ===");
Reset();
Db.Run("INSERT INTO prices(type_id,average_price,adjusted_price,updated_utc) VALUES(587,1000000,1000000,$u) ON CONFLICT(type_id) DO UPDATE SET average_price=1000000", ("$u", Util.NowIso()));
Db.Run(@"INSERT INTO industry_jobs(character_id,job_id,activity_id,product_type_id,runs,cost,status,start_utc,end_utc)
         VALUES($c,1,1,587,3,500000,'delivered',$s,$e)",
    ("$c", C), ("$s", Util.ToIso(T0.AddDays(1))), ("$e", Util.ToIso(T0.AddDays(2))));
{
    var r = Analytics.Industry(C, T0, T0.AddDays(10));
    Check("Jobkosten", r.TotalCost, 500000);
    Check("Produktwert (3 * 1 Mio)", r.TotalOutputValue, 3_000_000);
    Check("Saldo", r.Balance, 2_500_000);
}

Console.WriteLine("\n=== 13. ISK-Formatierung ===");
{
    void CheckS(string name, string actual, string expected)
    {
        var ok = actual == expected;
        Console.WriteLine($"  {(ok ? "OK  " : "FEHL")}  {name,-46} erwartet {expected,14}   erhalten {actual,14}");
        if (ok) passed++; else failed++;
    }
    CheckS("Millionen", Util.IskShort(412_700_000), "412,7 Mio");
    CheckS("Milliarden", Util.IskShort(1_234_000_000), "1,23 Mrd");
    CheckS("negativ", Util.IskShort(-5_000_000), "-5 Mio");
    CheckS("klein", Util.IskShort(750), "750");
}

Console.WriteLine($"\n{new string('=', 70)}");
Console.WriteLine($"  {passed} bestanden, {failed} fehlgeschlagen");
Console.WriteLine(new string('=', 70));

try { Directory.Delete(tmp, true); } catch { }
return failed == 0 ? 0 : 1;
