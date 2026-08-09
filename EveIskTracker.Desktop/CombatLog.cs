using System.Text;
using System.Text.RegularExpressions;

namespace EveIskTracker;

/// <summary>
/// Zerlegt Zeilen aus EVEs Game-Logs (Documents\EVE\logs\Gamelogs) in Schadensereignisse.
/// Reine Logik ohne Datei-Zugriff, damit die Tests sie direkt füttern können.
/// </summary>
public static class CombatParser
{
    // Schadenszeilen tragen feste Farbcodes, die in allen Client-Sprachen gleich sind:
    // 0xff00ffff = selbst ausgeteilt, 0xffcc0000 = selbst erlitten. Die Wortvarianten
    // (to/from im englischen, an/von im deutschen Client) sind nur die Rückfallebene,
    // falls CCP die Farben eines Tages ändert.
    private static readonly Regex LineRx = new(
        @"^\[ (\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}) \] \(combat\) (.*)$",
        RegexOptions.Compiled);
    private static readonly Regex DealtRx = new(@"^<color=0xff00ffff><b>(\d+)", RegexOptions.Compiled);
    private static readonly Regex TakenRx = new(@"^<color=0xffcc0000><b>(\d+)", RegexOptions.Compiled);
    private static readonly Regex NumRx = new(@"<b>(\d+)</b>", RegexOptions.Compiled);

    /// <summary>Liefert true für eine Schadenszeile; Treffer ohne Schaden (Miss) und alles andere false.</summary>
    public static bool TryParse(string line, out DateTime utc, out int amount, out bool dealt)
    {
        utc = default; amount = 0; dealt = false;
        if (line == null || !line.Contains("(combat)")) return false;

        var m = LineRx.Match(line);
        if (!m.Success) return false;
        if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy.MM.dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out utc))
            return false;

        var rest = m.Groups[2].Value;
        var d = DealtRx.Match(rest);
        if (d.Success && int.TryParse(d.Groups[1].Value, out amount)) { dealt = true; return amount > 0; }
        var t = TakenRx.Match(rest);
        if (t.Success && int.TryParse(t.Groups[1].Value, out amount)) { dealt = false; return amount > 0; }

        // Rückfallebene über die Richtungswörter (en: to/from, de: an/von)
        var n = NumRx.Match(rest);
        if (!n.Success || !int.TryParse(n.Groups[1].Value, out amount) || amount <= 0) return false;
        if (rest.Contains(">to<") || rest.Contains(">an<") || rest.Contains(">auf<")) { dealt = true; return true; }
        if (rest.Contains(">from<") || rest.Contains(">von<")) { dealt = false; return true; }
        return false;
    }
}

/// <summary>
/// Sekunden-Eimer für den DPS-Graph: Schaden wird auf die Sekunde des Log-Zeitstempels
/// gebucht, alte Eimer fliegen raus. Ebenfalls ohne Datei-Zugriff testbar.
/// </summary>
public sealed class DpsBuckets
{
    private readonly SortedDictionary<long, (long Dealt, long Taken)> _b = new();
    public long TotalDealt { get; private set; }
    public long TotalTaken { get; private set; }
    public DateTime LastEventUtc { get; private set; }

    private static long Sec(DateTime utc) => utc.Ticks / TimeSpan.TicksPerSecond;

    public void Add(DateTime utc, int amount, bool dealt)
    {
        var s = Sec(utc);
        _b.TryGetValue(s, out var v);
        _b[s] = dealt ? (v.Dealt + amount, v.Taken) : (v.Dealt, v.Taken + amount);
        if (dealt) TotalDealt += amount; else TotalTaken += amount;
        if (utc > LastEventUtc) LastEventUtc = utc;
    }

    /// <summary>Die letzten <paramref name="seconds"/> Sekunden bis "jetzt" als dichte Arrays.</summary>
    public (long[] Dealt, long[] Taken) Window(DateTime nowUtc, int seconds)
    {
        var dealt = new long[seconds];
        var taken = new long[seconds];
        var end = Sec(nowUtc);                    // aktuelle (angebrochene) Sekunde inklusive
        var start = end - seconds + 1;
        foreach (var (s, v) in _b)
        {
            if (s < start || s > end) continue;
            dealt[s - start] = v.Dealt;
            taken[s - start] = v.Taken;
        }
        return (dealt, taken);
    }

    public void Prune(DateTime nowUtc, int keepSeconds = 1200)
    {
        var cut = Sec(nowUtc) - keepSeconds;
        // SortedDictionary: von vorn löschen, bis die Einträge jung genug sind
        while (_b.Count > 0)
        {
            var first = _b.Keys.First();
            if (first >= cut) break;
            _b.Remove(first);
        }
    }

    public void Reset()
    {
        _b.Clear();
        TotalDealt = TotalTaken = 0;
        LastEventUtc = default;
    }
}

/// <summary>Momentaufnahme für /api/dps.</summary>
public sealed class DpsSnapshot
{
    public bool Tracking;
    public string File;
    public string Dir;
    public bool DirExists;
    public DateTime NowUtc;
    public long[] Dealt;
    public long[] Taken;
    public long TotalDealt;
    public long TotalTaken;
    public DateTime LastEventUtc;
    public string Listener;
    public long FileCharId;
}

/// <summary>
/// Liest das Game-Log des laufenden EVE-Clients live mit (rein lesend, EVE schreibt
/// parallel weiter). Läuft nur, solange jemand /api/dps abfragt — 5 Minuten ohne
/// Abfrage, und der Mitleser legt sich wieder schlafen.
/// </summary>
public static class CombatTracker
{
    private static readonly object Lock = new();
    private static System.Threading.Timer _timer;
    private static long _charId;
    private static string _charName;
    private static string _file;
    private static string _listener;
    private static long _fileCharId;
    private static FileStream _fs;
    private static Decoder _decoder;
    private static string _carry = "";
    private static readonly DpsBuckets Buckets = new();
    private static DateTime _lastPoll;
    private static DateTime _lastFileCheck;

    /// <summary>Standard: Documents\EVE\logs\Gamelogs; über kv "gamelog_dir" übersteuerbar.</summary>
    public static string GamelogDir
    {
        get
        {
            var own = Db.GetKv("gamelog_dir");
            if (!string.IsNullOrWhiteSpace(own)) return own;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "EVE", "logs", "Gamelogs");
        }
    }

    public static DpsSnapshot Snapshot(long charId, string charName, int windowSeconds)
    {
        lock (Lock)
        {
            _lastPoll = DateTime.UtcNow;
            if (charId != _charId)
            {
                // Charakterwechsel: Datei neu suchen, Zähler auf null
                CloseFileLocked();
                Buckets.Reset();
                _charId = charId;
                _lastFileCheck = default;
            }
            _charName = charName ?? "";
            _timer ??= new System.Threading.Timer(_ => Tick(), null, 0, 500);

            var dir = GamelogDir;
            var now = DateTime.UtcNow;
            var (dealt, taken) = Buckets.Window(now, windowSeconds);
            return new DpsSnapshot
            {
                Tracking = _fs != null,
                File = _file == null ? null : Path.GetFileName(_file),
                Dir = dir,
                DirExists = Directory.Exists(dir),
                NowUtc = now,
                Dealt = dealt,
                Taken = taken,
                TotalDealt = Buckets.TotalDealt,
                TotalTaken = Buckets.TotalTaken,
                LastEventUtc = Buckets.LastEventUtc,
                Listener = _listener,
                FileCharId = _fileCharId,
            };
        }
    }

    private static void Tick()
    {
        lock (Lock)
        {
            // Niemand fragt mehr — Datei zu, Timer aus, Speicher frei
            if (DateTime.UtcNow - _lastPoll > TimeSpan.FromMinutes(5))
            {
                CloseFileLocked();
                Buckets.Reset();
                _timer?.Dispose();
                _timer = null;
                return;
            }

            try
            {
                // Alle 5 s prüfen, ob es ein neueres/passenderes Log gibt (Relog, neuer
                // Client, Charakterwechsel). Gewechselt wird nur, wenn die Kandidatin
                // klar frischer beschrieben wird als die aktuelle Datei — sonst würden
                // zwei gleichzeitig loggende Clients die Anzeige hin- und herreißen.
                if (DateTime.UtcNow - _lastFileCheck > TimeSpan.FromSeconds(5))
                {
                    _lastFileCheck = DateTime.UtcNow;
                    var best = PickFile();
                    if (best != null && best != _file)
                    {
                        var cur = _file != null && System.IO.File.Exists(_file)
                            ? System.IO.File.GetLastWriteTimeUtc(_file) : DateTime.MinValue;
                        if (_file == null || System.IO.File.GetLastWriteTimeUtc(best) > cur.AddSeconds(3))
                            AttachLocked(best);
                    }
                }
                ReadNewLocked();
                Buckets.Prune(DateTime.UtcNow);
            }
            catch
            {
                // Datei weg/gesperrt: Zustand verwerfen, nächster Tick sucht neu
                CloseFileLocked();
            }
        }
    }

    /// <summary>
    /// Bestes Log: Vorrang hat die zuletzt beschriebene Datei der letzten 10 Minuten —
    /// wer gerade spielt, wird angezeigt, Charakterwechsel inklusive. Läuft gerade
    /// kein Client, fällt die Wahl auf das jüngste Log des App-Charakters (ID im
    /// Dateinamen wie 20260809_170000_2124575544.txt, ältere Clients: Name im Kopf
    /// unter "Listener: …").
    /// </summary>
    private static string PickFile()
    {
        var dir = GamelogDir;
        if (!Directory.Exists(dir)) return null;

        var files = new DirectoryInfo(dir).GetFiles("*.txt")
            .OrderByDescending(f => f.LastWriteTimeUtc).Take(12).ToList();
        if (files.Count == 0) return null;

        if (files[0].LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-10))
            return files[0].FullName;

        var suffix = "_" + _charId + ".txt";
        var byId = files.FirstOrDefault(f => f.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (byId != null) return byId.FullName;

        if (!string.IsNullOrEmpty(_charName))
            foreach (var f in files)
                if (HeaderContains(f.FullName, _charName))
                    return f.FullName;

        return null;
    }

    private static bool HeaderContains(string path, string needle)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[600];
            var n = fs.Read(buf, 0, buf.Length);
            var head = DetectEncoding(buf, n).GetString(buf, 0, n);
            return head.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Game-Logs sind normalerweise UTF-8; zur Sicherheit auch UTF-16 (BOM) erkennen,
    // das Format der alten Chat-Logs.
    private static Encoding DetectEncoding(byte[] buf, int n)
    {
        if (n >= 2 && buf[0] == 0xFF && buf[1] == 0xFE) return Encoding.Unicode;
        return Encoding.UTF8;
    }

    private static void AttachLocked(string path)
    {
        CloseFileLocked();
        Buckets.Reset();

        var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var head = new byte[600];
        var n = fs.Read(head, 0, head.Length);
        var enc = DetectEncoding(head, n);
        // hinter die BOM springen, sonst landet sie in der ersten Zeile
        fs.Position = enc.Equals(Encoding.Unicode) ? 2
            : (n >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0);

        // Wer spielt hier? Charakter-ID aus dem Dateinamen, Name aus dem Log-Kopf.
        // Die Beschriftung der Kopfzeile ist lokalisiert ("Listener:" / "Empfänger:" …),
        // aber die dritte Zeile trägt in jeder Sprache "<Beschriftung>: <Name>" —
        // deshalb schlicht die dritte Zeile am Doppelpunkt teilen.
        var idm = Regex.Match(Path.GetFileName(path), @"_(\d+)\.txt$", RegexOptions.IgnoreCase);
        _fileCharId = idm.Success && long.TryParse(idm.Groups[1].Value, out var fid) ? fid : 0;
        _listener = null;
        var headLines = enc.GetString(head, 0, n).Split('\n');
        if (headLines.Length > 2)
        {
            var ci = headLines[2].IndexOf(':');
            if (ci > 0 && ci < headLines[2].Length - 1)
            {
                var v = headLines[2][(ci + 1)..].Trim();
                if (v.Length > 0) _listener = v;
            }
        }

        _fs = fs;
        _file = path;
        _decoder = enc.GetDecoder();
        _carry = "";
        // Die Datei wird von Anfang an gelesen: so stimmen die Summen "seit Client-Start"
        ReadNewLocked();
    }

    private static void ReadNewLocked()
    {
        if (_fs == null) return;
        if (_fs.Position >= _fs.Length) return;

        var buf = new byte[64 * 1024];
        var chars = new char[64 * 1024];
        while (_fs.Position < _fs.Length)
        {
            var n = _fs.Read(buf, 0, buf.Length);
            if (n <= 0) break;
            var c = _decoder.GetChars(buf, 0, n, chars, 0);
            _carry += new string(chars, 0, c);

            int nl;
            while ((nl = _carry.IndexOf('\n')) >= 0)
            {
                var line = _carry[..nl].TrimEnd('\r');
                _carry = _carry[(nl + 1)..];
                if (CombatParser.TryParse(line, out var utc, out var amount, out var dealt))
                    Buckets.Add(utc, amount, dealt);
            }
        }
    }

    private static void CloseFileLocked()
    {
        try { _fs?.Dispose(); } catch { }
        _fs = null;
        _file = null;
        _listener = null;
        _fileCharId = 0;
        _decoder = null;
        _carry = "";
    }
}
