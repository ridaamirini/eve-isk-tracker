using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace EveIskTracker;

public record TokenSet(string AccessToken, DateTime ExpiresUtc, string RefreshToken, long CharacterId, string CharacterName, string Scopes);

/// <summary>
/// EVE SSO v2 mit PKCE. Für Desktop-Anwendungen ist das der vorgeschriebene Weg —
/// es gibt bewusst kein Client-Secret, das in einer verteilten EXE ohnehin niemals
/// geheim bliebe. Der Schutz kommt aus dem code_verifier, der die Anwendung nie verlässt.
/// </summary>
public static class Sso
{
    public const string AuthorizeUrl = "https://login.eveonline.com/v2/oauth/authorize/";
    public const string TokenUrl = "https://login.eveonline.com/v2/oauth/token";
    public const string Issuer = "login.eveonline.com";

    public static readonly string[] Scopes = {
        "esi-wallet.read_character_wallet.v1",     // Kontostand, Journal, Transaktionen
        "esi-markets.read_character_orders.v1",    // offene und historische Marktorders
        "esi-industry.read_character_mining.v1",   // Mining-Ledger
        "esi-industry.read_character_jobs.v1",     // Produktionsjobs
        "esi-assets.read_assets.v1",               // Bestände zur Bewertung
        "esi-killmails.read_killmails.v1",         // Kills & Verluste (ISK-Werte via zKillboard)
        "esi-characters.read_loyalty.v1",          // LP-Stände für den LP-Store-Vergleich
        "esi-search.search_structures.v1",         // Item-Suche für das Produkt-Research
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // laufende Login-Vorgänge: state -> code_verifier
    private static readonly ConcurrentDictionary<string, string> Pending = new();

    // Zugriffstoken im Speicher, damit nicht bei jedem Aufruf erneuert wird
    private static readonly ConcurrentDictionary<long, TokenSet> Live = new();

    public static string RedirectUri => $"http://localhost:{Config.Port}/callback";

    public static string BuildAuthorizeUrl(string clientId)
    {
        var verifier = Util.RandomUrlSafe(32);
        var state = Util.RandomUrlSafe(16);
        Pending[state] = verifier;

        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["scope"] = string.Join(" ", Scopes),
            ["state"] = state,
            ["code_challenge"] = Util.Sha256Base64Url(verifier),
            ["code_challenge_method"] = "S256",
        };
        return AuthorizeUrl + "?" + string.Join("&",
            q.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
    }

    public static async Task<TokenSet> HandleCallbackAsync(string code, string state, string clientId)
    {
        if (!Pending.TryRemove(state, out var verifier))
            throw new InvalidOperationException(
                "Unbekannter oder abgelaufener Login-Vorgang (state passt nicht). Bitte den Login neu starten.");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        };
        return await ExchangeAsync(form);
    }

    public static async Task<TokenSet> RefreshAsync(string refreshToken, string clientId)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        };
        return await ExchangeAsync(form);
    }

    private static async Task<TokenSet> ExchangeAsync(Dictionary<string, string> form)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        { Content = new FormUrlEncodedContent(form) };
        req.Headers.Host = Issuer;
        req.Headers.UserAgent.ParseAdd("EveIskTracker/1.0");

        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token-Anfrage fehlgeschlagen (HTTP {(int)resp.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString();
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 1199;
        var refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

        var claims = ReadJwtPayload(access);
        var sub = claims.GetValueOrDefault("sub") as string ?? "";
        var name = claims.GetValueOrDefault("name") as string ?? "Unbekannt";

        // sub hat die Form "CHARACTER:EVE:12345"
        var idPart = sub.Split(':').LastOrDefault();
        if (!long.TryParse(idPart, out var charId))
            throw new InvalidOperationException($"Charakter-ID nicht aus Token lesbar (sub='{sub}').");

        var iss = claims.GetValueOrDefault("iss") as string ?? "";
        if (!iss.Contains(Issuer, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Token stammt nicht von CCP (iss='{iss}').");

        var scopes = claims.GetValueOrDefault("scp") switch
        {
            string s => s,
            List<object> l => string.Join(" ", l),
            _ => string.Join(" ", Scopes)
        };

        var ts = new TokenSet(access, Util.UtcNow.AddSeconds(expiresIn - 30), refresh, charId, name, scopes);
        Live[charId] = ts;
        return ts;
    }

    /// <summary>Gültiges Zugriffstoken für einen Charakter, erneuert bei Bedarf automatisch.</summary>
    public static async Task<string> GetAccessTokenAsync(long characterId)
    {
        if (Live.TryGetValue(characterId, out var t) && t.ExpiresUtc > Util.UtcNow)
            return t.AccessToken;

        var blob = Db.Scalar("SELECT refresh_blob FROM tokens WHERE character_id=$c", ("$c", characterId));
        if (blob == null || blob == DBNull.Value)
            throw new InvalidOperationException($"Kein gespeicherter Login für Charakter {characterId}. Bitte neu anmelden.");

        var refresh = Util.Unprotect((byte[])blob);
        if (refresh == null)
            throw new InvalidOperationException(
                "Gespeicherter Login konnte nicht entschlüsselt werden. Das passiert, wenn die Datenbank " +
                "von einem anderen Windows-Konto kopiert wurde. Bitte den Charakter neu anmelden.");

        var clientId = Config.ClientId
            ?? throw new InvalidOperationException("Keine Client-ID hinterlegt.");
        var fresh = await RefreshAsync(refresh, clientId);
        SaveToken(fresh);
        return fresh.AccessToken;
    }

    public static void SaveToken(TokenSet t)
    {
        Db.Run(@"INSERT INTO characters(character_id,name,added_utc,enabled) VALUES($c,$n,$t,1)
                 ON CONFLICT(character_id) DO UPDATE SET name=$n",
            ("$c", t.CharacterId), ("$n", t.CharacterName), ("$t", Util.NowIso()));

        if (t.RefreshToken != null)
        {
            Db.Run(@"INSERT INTO tokens(character_id,refresh_blob,scopes,updated_utc) VALUES($c,$b,$s,$t)
                     ON CONFLICT(character_id) DO UPDATE SET refresh_blob=$b, scopes=$s, updated_utc=$t",
                ("$c", t.CharacterId), ("$b", Util.Protect(t.RefreshToken)),
                ("$s", t.Scopes), ("$t", Util.NowIso()));
        }
        Live[t.CharacterId] = t;
    }

    public static void Forget(long characterId)
    {
        Live.TryRemove(characterId, out _);
        Db.Run("DELETE FROM tokens WHERE character_id=$c", ("$c", characterId));
    }

    private static Dictionary<string, object> ReadJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) throw new InvalidOperationException("Token hat kein JWT-Format.");
        var json = Encoding.UTF8.GetString(Util.FromBase64Url(parts[1]));
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, object>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            dict[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.GetDouble(),
                JsonValueKind.Array => p.Value.EnumerateArray().Select(x => (object)x.ToString()).ToList(),
                _ => p.Value.ToString()
            };
        }
        return dict;
    }
}
