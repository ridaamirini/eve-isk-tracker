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

    public static string ClientId
    {
        get => Db.GetKv("client_id");
        set => Db.SetKv("client_id", value?.Trim());
    }

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

    public static readonly string[] KnownMetrics = { "wallet", "rate", "session", "mining" };

    /// <summary>Welche Werte das Stream-Widget zeigt — Komma-Liste, Reihenfolge fest.</summary>
    public static string OverlayMetrics
    {
        get => Db.GetKv("overlay_metrics", "wallet,rate,session,mining");
        set
        {
            // nur bekannte Schlüssel, Reihenfolge normiert; leer fällt auf alle zurück
            var wanted = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var clean = KnownMetrics.Where(wanted.Contains).ToArray();
            Db.SetKv("overlay_metrics", clean.Length > 0 ? string.Join(',', clean) : string.Join(',', KnownMetrics));
        }
    }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}
