namespace EveIskTracker;

/// <summary>
/// Einstellungen. Client-ID und Kontakt landen in der lokalen Datenbank, damit die
/// EXE ohne Konfigurationsdatei auskommt und beim ersten Start danach fragen kann.
/// </summary>
public static class Config
{
    public const int Port = 8765;

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EveIskTracker");

    /// <summary>
    /// Eingebaute Standard-App (PKCE, kein Secret — die Client-ID ist öffentlich und steht
    /// ohnehin in jeder Login-URL). Sie steht bewusst NICHT im Quellcode, sondern wird beim
    /// offiziellen Release-Build über -p:DefaultClientId=... (aus release.env, gitignoriert)
    /// als Assembly-Metadatum eingebettet. Selbstgebaute Forks haben keine Standard-App und
    /// fragen nach einer eigenen Client-ID.
    /// </summary>
    public static string DefaultClientId { get; } =
        typeof(Config).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DefaultClientId")?.Value ?? "";

    /// <summary>Wirksame Client-ID: eigene, sonst die eingebaute.</summary>
    public static string ClientId
    {
        get { var own = Db.GetKv("client_id"); return string.IsNullOrWhiteSpace(own) ? DefaultClientId : own; }
        set => Db.SetKv("client_id", value?.Trim());
    }

    /// <summary>Nur die selbst eingetragene ID (leer = Standard-App aktiv), für die Anzeige.</summary>
    public static string ClientIdRaw => Db.GetKv("client_id") ?? "";

    /// <summary>Kontaktangabe für den User-Agent — CCP möchte wissen, wer da anfragt.</summary>
    public static string Contact
    {
        get => Db.GetKv("contact", "");
        set => Db.SetKv("contact", value?.Trim());
    }

    /// <summary>Optionaler Pfad, unter dem der Session-Wert zusätzlich als .txt landet (für OBS-Textquellen).</summary>
    public static string OverlayTextPath
    {
        get => Db.GetKv("overlay_txt", "");
        set => Db.SetKv("overlay_txt", value?.Trim());
    }

    /// <summary>Reihenfolge hier = Anzeige-Reihenfolge im Widget (nach dem Vorbild klassischer Session-HUDs).</summary>
    public static readonly string[] KnownMetrics = { "time", "session", "rate", "bounties", "missions", "kills", "destroyed", "mining", "wallet" };

    /// <summary>Welche Werte das Stream-Widget zeigt — Komma-Liste, Reihenfolge fest.</summary>
    public static string OverlayMetrics
    {
        get => Db.GetKv("overlay_metrics", "time,session,rate,mining");
        set
        {
            // nur bekannte Schlüssel, Reihenfolge normiert; leer fällt auf alle zurück
            var wanted = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var clean = KnownMetrics.Where(wanted.Contains).ToArray();
            Db.SetKv("overlay_metrics", clean.Length > 0 ? string.Join(',', clean) : string.Join(',', KnownMetrics));
        }
    }

    /// <summary>
    /// Wie lange das Widget einen berechneten ISK/h-Wert stehen lässt (Sekunden).
    /// ISK/h hat die laufende Zeit im Nenner und würde ohne Haltezeit im Stream
    /// sichtbar kriechen. Einstellbar in den Settings, erklärt ebendort.
    /// </summary>
    public static int RateHoldSeconds
    {
        get => int.TryParse(Db.GetKv("rate_hold", "300"), out var v) ? Math.Clamp(v, 60, 3600) : 300;
        set => Db.SetKv("rate_hold", Math.Clamp(value, 60, 3600).ToString());
    }

    /// <summary>Sessions automatisch beenden, wenn der EVE-Client (exefile.exe) schließt.</summary>
    public static bool SessionAutoStop
    {
        get => Db.GetKv("session_autostop", "1") != "0";
        set => Db.SetKv("session_autostop", value ? "1" : "0");
    }

    /// <summary>Zeigt das Widget Charakter-Portrait und -Namen im Kopf? (Opsec-Schalter)</summary>
    public static bool OverlayShowChar
    {
        get => Db.GetKv("overlay_char", "1") != "0";
        set => Db.SetKv("overlay_char", value ? "1" : "0");
    }

    // ---- Game-Overlay (In-Game-HUD über dem EVE-Client) ----

    /// <summary>Bausteine, die das Game-Overlay zeigt; Reihenfolge = Anzeige-Reihenfolge.</summary>
    public static readonly string[] KnownOverlayModules = { "dps", "session" };

    /// <summary>Komma-Liste der aktiven Overlay-Bausteine (dps = Schadensgraph, session = ISK-Zeile).</summary>
    public static string GameOverlayModules
    {
        get => Db.GetKv("gameoverlay_modules", "dps,session");
        set
        {
            var wanted = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var clean = KnownOverlayModules.Where(wanted.Contains).ToArray();
            Db.SetKv("gameoverlay_modules", clean.Length > 0 ? string.Join(',', clean) : "dps,session");
        }
    }

    /// <summary>Deckkraft des Overlay-Fensters in Prozent (10–100, stufenloser Regler).</summary>
    public static int GameOverlayOpacity
    {
        get => int.TryParse(Db.GetKv("gameoverlay_opacity", "95"), out var v) ? Math.Clamp(v, 10, 100) : 95;
        set => Db.SetKv("gameoverlay_opacity", Math.Clamp(value, 10, 100).ToString());
    }

    /// <summary>Overlay-Ansicht: "detail" (Graph + Summen) oder "compact" (schmale Zahlenzeile).</summary>
    public static string GameOverlayLayout
    {
        get => Db.GetKv("gameoverlay_layout", "detail") == "compact" ? "compact" : "detail";
        set => Db.SetKv("gameoverlay_layout", value == "compact" ? "compact" : "detail");
    }

    /// <summary>War das Overlay beim letzten Beenden sichtbar? Wird beim Start wiederhergestellt.</summary>
    public static bool GameOverlayOn
    {
        get => Db.GetKv("gameoverlay_on", "0") == "1";
        set => Db.SetKv("gameoverlay_on", value ? "1" : "0");
    }

    /// <summary>Gemerkte Fensterposition "x,y"; leer = Standard (rechts oben).</summary>
    public static string GameOverlayPos
    {
        get => Db.GetKv("gameoverlay_pos", "");
        set => Db.SetKv("gameoverlay_pos", value);
    }

    /// <summary>Oberflächensprache: "de" oder "en". Standard Englisch (internationales Publikum).</summary>
    public static string Lang
    {
        get => Db.GetKv("lang", "en") == "de" ? "de" : "en";
        set => Db.SetKv("lang", value == "de" ? "de" : "en");
    }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}
