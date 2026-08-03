using Microsoft.Data.Sqlite;

namespace EveIskTracker;

/// <summary>
/// Lokale SQLite-Datenbank. Der eigentliche Mehrwert gegenüber ESI: CCP liefert nur ein
/// rollierendes Fenster (Journal/Transaktionen ~30 Tage). Hier wird alles dauerhaft
/// angehäuft, damit Auswertungen über Monate hinweg möglich bleiben.
/// </summary>
public static class Db
{
    private static string _connStr;

    public static string Path { get; private set; }

    public static void Init(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        Path = System.IO.Path.Combine(dataDir, "eveisk.db");
        _connStr = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        // Selbstheilung mit Augenmaß: Nur ECHTE Korruption (SQLITE_CORRUPT/NOTADB)
        // führt zur Backup-Wiederherstellung. Direkt nach dem Windows-Start kann die
        // Datei kurzzeitig gesperrt sein (Virenscanner) — das ist KEINE Korruption
        // und wird mit Wartezyklen überbrückt, sonst würde eine gesunde Datenbank
        // fälschlich durch ein älteres Backup ersetzt.
        var healthy = false;
        for (var attempt = 0; attempt < 5 && !healthy; attempt++)
        {
            var state = TryPrepare();
            if (state == DbState.Ok) { healthy = true; break; }
            if (state == DbState.Corrupt)
            {
                RestoreFromBackup();
                if (TryPrepare() == DbState.Ok) { healthy = true; break; }
                throw new InvalidOperationException(
                    "Datenbank ist beschädigt und kein brauchbares Backup vorhanden. " +
                    "Bitte die Dateien unter " + dataDir + " prüfen.");
            }
            // Transient (gesperrt o.ä.): kurz warten und erneut versuchen
            Thread.Sleep(1500);
        }
        if (!healthy)
            throw new InvalidOperationException(
                "Datenbank ist dauerhaft nicht zugreifbar (gesperrt?). " +
                "Läuft ein Virenscan auf " + dataDir + "?");

        Checkpoint();
        BackupNow();   // frischer Schnappschuss bei jedem Start (2 Generationen)
    }

    private enum DbState { Ok, Corrupt, Transient }

    /// <summary>Pragmas, Integritätsprüfung, Schema — mit ehrlicher Fehlerdiagnose.</summary>
    private static DbState TryPrepare()
    {
        try
        {
            using var c = Open();
            Exec(c, "PRAGMA journal_mode=WAL;");
            // FULL statt NORMAL: im WAL-Modus fsynct NORMAL Commits nicht — bei einem
            // harten Neustart können dann Daten seit dem letzten Checkpoint verloren gehen.
            Exec(c, "PRAGMA synchronous=FULL;");
            Exec(c, "PRAGMA busy_timeout=8000;");

            using (var chk = Cmd(c, "PRAGMA quick_check(1);"))
            {
                var res = chk.ExecuteScalar() as string;
                if (!string.Equals(res, "ok", StringComparison.OrdinalIgnoreCase)) return DbState.Corrupt;
            }

            CreateSchema(c);
            return DbState.Ok;
        }
        catch (SqliteException ex)
        {
            // 11 = SQLITE_CORRUPT, 26 = SQLITE_NOTADB — nur das ist echte Korruption.
            // Alles andere (BUSY, LOCKED, CANTOPEN, IOERR) ist möglicherweise vorübergehend.
            return ex.SqliteErrorCode is 11 or 26 ? DbState.Corrupt : DbState.Transient;
        }
    }

    /// <summary>
    /// Kaputte Hauptdatei beiseitelegen (Forensik) und den jüngsten brauchbaren
    /// Schnappschuss einspielen. Gibt es keinen, startet die App mit leerer Datenbank.
    /// </summary>
    private static void RestoreFromBackup()
    {
        SqliteConnection.ClearAllPools();
        var dir = System.IO.Path.GetDirectoryName(Path);
        var backup = System.IO.Path.Combine(dir, "eveisk.backup.db");
        var backupOld = System.IO.Path.Combine(dir, "eveisk.backup.old.db");

        try
        {
            if (File.Exists(Path))
            {
                var quarantine = System.IO.Path.Combine(dir,
                    $"eveisk.corrupt-{Util.UtcNow:yyyyMMdd-HHmmss}.db");
                File.Move(Path, quarantine, true);
            }
            if (File.Exists(Path + "-wal")) File.Delete(Path + "-wal");
            if (File.Exists(Path + "-shm")) File.Delete(Path + "-shm");

            var src = File.Exists(backup) ? backup : (File.Exists(backupOld) ? backupOld : null);
            if (src != null) File.Copy(src, Path, true);
        }
        catch { /* schlimmstenfalls legt CreateSchema eine frische Datenbank an */ }
    }

    /// <summary>WAL in die Hauptdatei übernehmen — klein halten, was verloren gehen könnte.</summary>
    public static void Checkpoint()
    {
        try
        {
            using var c = Open();
            Exec(c, "PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch { /* Checkpoint ist Vorsorge; ein Fehlschlag (z.B. paralleler Leser) ist ok */ }
    }

    /// <summary>
    /// Konsistenten Schnappschuss neben die DB legen (VACUUM INTO liest über den WAL
    /// hinweg). Zwei Generationen: das vorige Backup rückt nach .old — falls die
    /// Hauptdatei just zwischen zwei Backups kippt, gibt es noch einen Stand davor.
    /// </summary>
    public static void BackupNow()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            var backup = System.IO.Path.Combine(dir, "eveisk.backup.db");
            var backupOld = System.IO.Path.Combine(dir, "eveisk.backup.old.db");
            var tmp = backup + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);

            using (var c = Open())
            using (var cmd = Cmd(c, "VACUUM INTO $p", ("$p", tmp)))
                cmd.ExecuteNonQuery();

            if (File.Exists(backup))
            {
                if (File.Exists(backupOld)) File.Delete(backupOld);
                File.Move(backup, backupOld);
            }
            File.Move(tmp, backup);
        }
        catch { /* Backup ist Beiwerk — die App muss auch ohne laufen */ }
    }

    /// <summary>Für lange Tray-Laufzeiten: höchstens einmal täglich nachlegen.</summary>
    public static void BackupIfDue()
    {
        var backup = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "eveisk.backup.db");
        if (File.Exists(backup) && (Util.UtcNow - File.GetLastWriteTimeUtc(backup)).TotalHours < 24)
            return;
        BackupNow();
    }

    public static SqliteConnection Open()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        using (var p = c.CreateCommand())
        {
            p.CommandText = "PRAGMA busy_timeout=8000;";
            p.ExecuteNonQuery();
        }
        return c;
    }

    private static void CreateSchema(SqliteConnection c)
    {
        Exec(c, @"
CREATE TABLE IF NOT EXISTS kv (
    key   TEXT PRIMARY KEY,
    value TEXT
);

CREATE TABLE IF NOT EXISTS characters (
    character_id   INTEGER PRIMARY KEY,
    name           TEXT,
    corporation_id INTEGER,
    added_utc      TEXT,
    last_sync_utc  TEXT,
    last_balance   REAL,
    enabled        INTEGER NOT NULL DEFAULT 1
);

-- refresh_token liegt DPAPI-verschlüsselt als BLOB (nur dieses Windows-Konto kann es lesen)
CREATE TABLE IF NOT EXISTS tokens (
    character_id  INTEGER PRIMARY KEY,
    refresh_blob  BLOB,
    scopes        TEXT,
    updated_utc   TEXT
);

CREATE TABLE IF NOT EXISTS transactions (
    character_id   INTEGER NOT NULL,
    transaction_id INTEGER NOT NULL,
    date_utc       TEXT    NOT NULL,
    type_id        INTEGER NOT NULL,
    location_id    INTEGER,
    unit_price     REAL    NOT NULL,
    quantity       INTEGER NOT NULL,
    is_buy         INTEGER NOT NULL,
    client_id      INTEGER,
    journal_ref_id INTEGER,
    PRIMARY KEY (character_id, transaction_id)
);
CREATE INDEX IF NOT EXISTS ix_tx_date ON transactions(character_id, date_utc);
CREATE INDEX IF NOT EXISTS ix_tx_type ON transactions(character_id, type_id, date_utc);

CREATE TABLE IF NOT EXISTS journal (
    character_id    INTEGER NOT NULL,
    entry_id        INTEGER NOT NULL,
    date_utc        TEXT    NOT NULL,
    ref_type        TEXT    NOT NULL,
    amount          REAL,
    balance         REAL,
    description     TEXT,
    reason          TEXT,
    tax             REAL,
    context_id      INTEGER,
    context_id_type TEXT,
    first_party_id  INTEGER,
    second_party_id INTEGER,
    PRIMARY KEY (character_id, entry_id)
);
CREATE INDEX IF NOT EXISTS ix_jr_date ON journal(character_id, date_utc);
CREATE INDEX IF NOT EXISTS ix_jr_ref  ON journal(character_id, ref_type, date_utc);

-- ESI liefert das Mining-Ledger nur tagesweise aggregiert (kein Zeitstempel).
CREATE TABLE IF NOT EXISTS mining (
    character_id    INTEGER NOT NULL,
    date_day        TEXT    NOT NULL,
    solar_system_id INTEGER NOT NULL,
    type_id         INTEGER NOT NULL,
    quantity        INTEGER NOT NULL,
    PRIMARY KEY (character_id, date_day, solar_system_id, type_id)
);

-- Deshalb zusätzlich die Zuwächse mit Beobachtungszeit: nur so lässt sich
-- geschürftes Erz einer laufenden Session zuordnen.
CREATE TABLE IF NOT EXISTS mining_delta (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    character_id    INTEGER NOT NULL,
    observed_utc    TEXT    NOT NULL,
    date_day        TEXT    NOT NULL,
    solar_system_id INTEGER NOT NULL,
    type_id         INTEGER NOT NULL,
    quantity        INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_md_obs ON mining_delta(character_id, observed_utc);

CREATE TABLE IF NOT EXISTS industry_jobs (
    character_id      INTEGER NOT NULL,
    job_id            INTEGER NOT NULL,
    activity_id       INTEGER,
    blueprint_type_id INTEGER,
    product_type_id   INTEGER,
    runs              INTEGER,
    cost              REAL,
    status            TEXT,
    start_utc         TEXT,
    end_utc           TEXT,
    completed_utc     TEXT,
    output_location_id INTEGER,
    PRIMARY KEY (character_id, job_id)
);
CREATE INDEX IF NOT EXISTS ix_ij_end ON industry_jobs(character_id, end_utc);

CREATE TABLE IF NOT EXISTS market_orders (
    character_id  INTEGER NOT NULL,
    order_id      INTEGER NOT NULL,
    type_id       INTEGER,
    region_id     INTEGER,
    location_id   INTEGER,
    is_buy        INTEGER,
    price         REAL,
    volume_total  INTEGER,
    volume_remain INTEGER,
    issued_utc    TEXT,
    duration      INTEGER,
    state         TEXT,
    is_open       INTEGER NOT NULL DEFAULT 1,
    seen_utc      TEXT,
    PRIMARY KEY (character_id, order_id)
);

CREATE TABLE IF NOT EXISTS prices (
    type_id        INTEGER PRIMARY KEY,
    average_price  REAL,
    adjusted_price REAL,
    updated_utc    TEXT
);

CREATE TABLE IF NOT EXISTS names (
    id       INTEGER PRIMARY KEY,
    name     TEXT,
    category TEXT
);

-- Killmails: ID+Hash von ESI, ISK-Wert von zKillboard nachgeschlagen
CREATE TABLE IF NOT EXISTS kills (
    character_id        INTEGER NOT NULL,
    killmail_id         INTEGER NOT NULL,
    hash                TEXT,
    time_utc            TEXT,
    is_loss             INTEGER NOT NULL DEFAULT 0,
    victim_ship_type_id INTEGER,
    victim_char_id      INTEGER,
    solar_system_id     INTEGER,
    value               REAL,
    PRIMARY KEY (character_id, killmail_id)
);
CREATE INDEX IF NOT EXISTS ix_kills_time ON kills(character_id, time_utc);

CREATE TABLE IF NOT EXISTS sessions (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    character_id  INTEGER NOT NULL,
    label         TEXT,
    started_utc   TEXT NOT NULL,
    ended_utc     TEXT,
    start_balance REAL,
    end_balance   REAL
);
CREATE INDEX IF NOT EXISTS ix_se_char ON sessions(character_id, started_utc);

CREATE TABLE IF NOT EXISTS session_samples (
    session_id INTEGER NOT NULL,
    ts_utc     TEXT    NOT NULL,
    balance    REAL    NOT NULL,
    PRIMARY KEY (session_id, ts_utc)
);

-- ETag-Cache: ESI belohnt If-None-Match mit 304 und die zählen nicht gegen den Fehlerhaushalt
CREATE TABLE IF NOT EXISTS http_cache (
    url         TEXT PRIMARY KEY,
    etag        TEXT,
    body        TEXT,
    expires_utc TEXT,
    stored_utc  TEXT
);

CREATE TABLE IF NOT EXISTS sync_state (
    character_id INTEGER NOT NULL,
    resource     TEXT    NOT NULL,
    last_run_utc TEXT,
    last_error   TEXT,
    PRIMARY KEY (character_id, resource)
);
");
    }

    // ---------- kleine Helfer ----------

    public static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static SqliteCommand Cmd(SqliteConnection c, string sql, params (string, object)[] ps)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd;
    }

    public static object Scalar(string sql, params (string, object)[] ps)
    {
        using var c = Open();
        using var cmd = Cmd(c, sql, ps);
        return cmd.ExecuteScalar();
    }

    public static int Run(string sql, params (string, object)[] ps)
    {
        using var c = Open();
        using var cmd = Cmd(c, sql, ps);
        return cmd.ExecuteNonQuery();
    }

    public static string GetKv(string key, string fallback = null)
    {
        var v = Scalar("SELECT value FROM kv WHERE key=$k", ("$k", key));
        return v == null || v == DBNull.Value ? fallback : (string)v;
    }

    public static void SetKv(string key, string value) =>
        Run("INSERT INTO kv(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v",
            ("$k", key), ("$v", (object)value));

    public static void MarkSync(long charId, string resource, string error = null) =>
        Run(@"INSERT INTO sync_state(character_id,resource,last_run_utc,last_error)
              VALUES($c,$r,$t,$e)
              ON CONFLICT(character_id,resource) DO UPDATE SET last_run_utc=$t, last_error=$e",
            ("$c", charId), ("$r", resource), ("$t", Util.NowIso()), ("$e", (object)error));

    public static DateTime? LastSync(long charId, string resource)
    {
        var v = Scalar("SELECT last_run_utc FROM sync_state WHERE character_id=$c AND resource=$r",
                       ("$c", charId), ("$r", resource));
        if (v == null || v == DBNull.Value) return null;
        return Util.ParseIso((string)v);
    }
}
