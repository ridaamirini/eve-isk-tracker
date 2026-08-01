namespace EveIskTracker;

public class SessionState
{
    public bool Active { get; set; }
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string CharacterName { get; set; }
    public string Label { get; set; }
    public string StartedUtc { get; set; }
    public double StartBalance { get; set; }
    public double CurrentBalance { get; set; }
    public double Delta { get; set; }
    public double Hours { get; set; }
    public double IskPerHour { get; set; }
    /// <summary>Zeitpunkt des letzten Kontostand-Abrufs — ESI liefert höchstens alle 120 s neue Werte.</summary>
    public string LastSampleUtc { get; set; }
    public List<CategoryLine> Breakdown { get; set; } = new();
    public double MiningValue { get; set; }
    /// <summary>Die Aufschlüsselung stammt aus dem Journal und hängt der Realität bis zu einer Stunde hinterher.</summary>
    public bool BreakdownDelayed { get; set; } = true;
}

/// <summary>
/// Session-Verfolgung. Der Live-Wert kommt aus dem Kontostand (120 s Takt), weil das der
/// einzige zeitnah aktualisierte Endpunkt ist. Die Aufschlüsselung nach Einnahmequelle
/// stammt aus dem Journal und zieht mit bis zu einer Stunde Verzögerung nach.
/// </summary>
public static class Sessions
{
    public static SessionState Current(long charId)
    {
        using var c = Db.Open();
        using var cmd = Db.Cmd(c, @"
SELECT s.id, s.label, s.started_utc, s.start_balance, ch.name, ch.last_balance
FROM sessions s LEFT JOIN characters ch ON ch.character_id = s.character_id
WHERE s.character_id=$c AND s.ended_utc IS NULL
ORDER BY s.started_utc DESC LIMIT 1", ("$c", charId));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new SessionState { Active = false, CharacterId = charId };

        var st = new SessionState
        {
            Active = true,
            Id = r.GetInt64(0),
            CharacterId = charId,
            Label = r.IsDBNull(1) ? null : r.GetString(1),
            StartedUtc = r.GetString(2),
            StartBalance = r.IsDBNull(3) ? 0 : r.GetDouble(3),
            CharacterName = r.IsDBNull(4) ? "?" : r.GetString(4),
            CurrentBalance = r.IsDBNull(5) ? 0 : r.GetDouble(5),
        };

        var started = Util.ParseIso(st.StartedUtc);
        st.Delta = st.CurrentBalance - st.StartBalance;
        st.Hours = Math.Max((Util.UtcNow - started).TotalHours, 1.0 / 60);
        st.IskPerHour = st.Delta / st.Hours;
        st.LastSampleUtc = LastSample(st.Id);

        st.Breakdown = BreakdownFor(charId, started, Util.UtcNow);
        st.MiningValue = Analytics.MiningWindow(charId, started, Util.UtcNow).TotalValue;
        return st;
    }

    public static SessionState Start(long charId, string label)
    {
        // Doppelte offene Sessions vermeiden
        Db.Run("UPDATE sessions SET ended_utc=$t WHERE character_id=$c AND ended_utc IS NULL",
               ("$t", Util.NowIso()), ("$c", charId));

        var bal = CurrentBalance(charId);
        Db.Run(@"INSERT INTO sessions(character_id,label,started_utc,start_balance) VALUES($c,$l,$t,$b)",
            ("$c", charId), ("$l", (object)label), ("$t", Util.NowIso()), ("$b", bal));
        return Current(charId);
    }

    public static void Stop(long charId)
    {
        var bal = CurrentBalance(charId);
        Db.Run(@"UPDATE sessions SET ended_utc=$t, end_balance=$b
                 WHERE character_id=$c AND ended_utc IS NULL",
            ("$t", Util.NowIso()), ("$b", bal), ("$c", charId));
    }

    /// <summary>Wird vom Sync bei jedem neuen Kontostand aufgerufen.</summary>
    public static void OnBalance(long charId, double balance)
    {
        var id = Db.Scalar("SELECT id FROM sessions WHERE character_id=$c AND ended_utc IS NULL ORDER BY started_utc DESC LIMIT 1",
                           ("$c", charId));
        if (id == null || id == DBNull.Value) return;

        Db.Run("INSERT INTO session_samples(session_id,ts_utc,balance) VALUES($s,$t,$b) ON CONFLICT DO NOTHING",
               ("$s", (long)id), ("$t", Util.NowIso()), ("$b", balance));

        WriteOverlayText(charId);
    }

    public static double CurrentBalance(long charId)
    {
        var v = Db.Scalar("SELECT last_balance FROM characters WHERE character_id=$c", ("$c", charId));
        return v == null || v == DBNull.Value ? 0 : Convert.ToDouble(v);
    }

    private static string LastSample(long sessionId)
    {
        var v = Db.Scalar("SELECT MAX(ts_utc) FROM session_samples WHERE session_id=$s", ("$s", sessionId));
        return v == null || v == DBNull.Value ? null : (string)v;
    }

    public static List<CategoryLine> BreakdownFor(long charId, DateTime from, DateTime to)
    {
        var lines = new List<CategoryLine>();
        using var c = Db.Open();
        using var cmd = Db.Cmd(c, @"
SELECT ref_type, SUM(amount), COUNT(*) FROM journal
WHERE character_id=$c AND date_utc >= $f AND date_utc <= $t
GROUP BY ref_type ORDER BY ABS(SUM(amount)) DESC",
            ("$c", charId), ("$f", Util.ToIso(from)), ("$t", Util.ToIso(to)));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var amount = r.GetDouble(1);
            if (Math.Abs(amount) < 0.01) continue;
            lines.Add(new CategoryLine
            {
                Category = RefTypeLabel(r.GetString(0)),
                Amount = amount,
                Count = r.GetInt32(2)
            });
        }
        return lines;
    }

    /// <summary>Die technischen ref_type-Bezeichner in lesbares Deutsch übersetzen.</summary>
    public static string RefTypeLabel(string refType) => refType switch
    {
        "bounty_prizes" or "bounty_prize" or "bounty" => "Bounties",
        "ess_escrow_transfer" => "ESS-Auszahlung",
        "agent_mission_reward" or "mission_reward" => "Missionsbelohnung",
        "agent_mission_time_bonus_reward" => "Missions-Zeitbonus",
        "market_transaction" => "Markt (Handel)",
        "brokers_fee" => "Broker-Gebühr",
        "transaction_tax" => "Verkaufssteuer",
        "insurance" => "Versicherung",
        "contract_price" => "Vertrag (Preis)",
        "contract_reward" => "Vertrag (Belohnung)",
        "contract_brokers_fee" => "Vertrag (Gebühr)",
        "industry_job_tax" => "Industrie-Jobkosten",
        "reprocessing_tax" => "Reprocessing-Steuer",
        "corporate_reward_payout" => "Corp-Auszahlung",
        "lp_store" => "LP-Store",
        "market_escrow" => "Order-Hinterlegung",
        "player_donation" => "Spielerspende",
        "corporation_account_withdrawal" => "Corp-Abbuchung",
        "planetary_import_tax" => "PI-Importsteuer",
        "planetary_export_tax" => "PI-Exportsteuer",
        "skill_purchase" => "Skill-Kauf",
        "jump_clone_installation_fee" => "Klon-Gebühr",
        "structure_gate_jump" => "Sprungtor-Gebühr",
        _ => refType.Replace('_', ' ')
    };

    /// <summary>Optionale .txt-Ausgabe für OBS-Textquellen (Alternative zur Browser-Quelle).</summary>
    private static void WriteOverlayText(long charId)
    {
        var path = Config.OverlayTextPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var st = Current(charId);
            var text = st.Active ? Util.IskShort(st.Delta) + " ISK" : "—";
            File.WriteAllText(path, text);
        }
        catch { /* Overlay-Datei ist Beiwerk; ein Schreibfehler darf den Sync nicht stoppen */ }
    }
}
