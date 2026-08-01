using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EveIskTracker;

public class EsiResponse
{
    public HttpStatusCode Status;
    public string Body;
    public int Pages = 1;
    public bool FromCache;
    public string Error;
    public bool Ok => Error == null && Body != null;
}

/// <summary>
/// HTTP-Zugriff auf ESI. Beachtet CCPs Spielregeln: eigener User-Agent, ETag-Caching
/// (304er zählen nicht gegen den Fehlerhaushalt) und Rückzug bei X-Esi-Error-Limit-Remain.
/// </summary>
public class EsiClient
{
    public const string Base = "https://esi.evetech.net";

    private readonly HttpClient _http;
    private static DateTime _errorLimitUntil = DateTime.MinValue;
    private static readonly SemaphoreSlim _gate = new(8, 8);

    public EsiClient(string contact)
    {
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        { Timeout = TimeSpan.FromSeconds(60) };

        // CCP verlangt einen erkennbaren User-Agent mit Kontaktmöglichkeit.
        var ua = string.IsNullOrWhiteSpace(contact) ? "unknown" : contact.Trim();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("EveIskTracker/1.0");
        _http.DefaultRequestHeaders.Add("X-User-Agent", $"EveIskTracker/1.0 ({ua})");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<EsiResponse> GetAsync(string path, string accessToken = null, bool useCache = true)
    {
        var url = Base + path;
        string etag = null;
        string cachedBody = null;

        if (useCache)
        {
            using var c = Db.Open();
            using var cmd = Db.Cmd(c, "SELECT etag, body, expires_utc FROM http_cache WHERE url=$u", ("$u", url));
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                etag = r.IsDBNull(0) ? null : r.GetString(0);
                cachedBody = r.IsDBNull(1) ? null : r.GetString(1);
                var exp = r.IsDBNull(2) ? DateTime.MinValue : Util.ParseIso(r.GetString(2));
                // Noch frisch laut CCP-Cache-Header: gar nicht erst fragen.
                if (exp > Util.UtcNow && cachedBody != null)
                    return new EsiResponse { Status = HttpStatusCode.OK, Body = cachedBody, FromCache = true, Pages = 1 };
            }
        }

        await WaitForErrorLimit();
        await _gate.WaitAsync();
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (accessToken != null)
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                if (etag != null) req.Headers.TryAddWithoutValidation("If-None-Match", etag);

                HttpResponseMessage resp;
                try { resp = await _http.SendAsync(req); }
                catch (Exception ex)
                {
                    if (attempt == 2) return new EsiResponse { Error = "Netzwerkfehler: " + ex.Message };
                    await Task.Delay(1500 * (attempt + 1));
                    continue;
                }

                using (resp)
                {
                    TrackErrorLimit(resp);

                    var pages = 1;
                    if (resp.Headers.TryGetValues("X-Pages", out var pv) &&
                        int.TryParse(pv.FirstOrDefault(), out var pp)) pages = pp;

                    if (resp.StatusCode == HttpStatusCode.NotModified && cachedBody != null)
                    {
                        StoreCache(url, etag, cachedBody, resp);
                        return new EsiResponse { Status = resp.StatusCode, Body = cachedBody, Pages = pages, FromCache = true };
                    }

                    var body = await resp.Content.ReadAsStringAsync();

                    if (resp.IsSuccessStatusCode)
                    {
                        var newEtag = resp.Headers.ETag?.Tag;
                        if (useCache) StoreCache(url, newEtag, body, resp);
                        return new EsiResponse { Status = resp.StatusCode, Body = body, Pages = pages };
                    }

                    if ((int)resp.StatusCode >= 500 || resp.StatusCode == (HttpStatusCode)420)
                    {
                        if (attempt < 2) { await Task.Delay(2000 * (attempt + 1)); continue; }
                    }

                    return new EsiResponse
                    {
                        Status = resp.StatusCode,
                        Error = $"HTTP {(int)resp.StatusCode} bei {path}: {Trim(body)}"
                    };
                }
            }
            return new EsiResponse { Error = "Aufgegeben nach 3 Versuchen: " + path };
        }
        finally { _gate.Release(); }
    }

    public async Task<EsiResponse> PostAsync(string path, string json, string accessToken = null)
    {
        await WaitForErrorLimit();
        using var req = new HttpRequestMessage(HttpMethod.Post, Base + path)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (accessToken != null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var resp = await _http.SendAsync(req);
            TrackErrorLimit(resp);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return new EsiResponse { Status = resp.StatusCode, Error = $"HTTP {(int)resp.StatusCode} bei {path}: {Trim(body)}" };
            return new EsiResponse { Status = resp.StatusCode, Body = body };
        }
        catch (Exception ex) { return new EsiResponse { Error = "Netzwerkfehler: " + ex.Message }; }
    }

    /// <summary>Holt alle Seiten einer per X-Pages paginierten Route und hängt die Arrays aneinander.</summary>
    public async Task<List<JsonElement>> GetAllPagesAsync(string path, string accessToken)
    {
        var all = new List<JsonElement>();
        var sep = path.Contains('?') ? "&" : "?";
        var first = await GetAsync($"{path}{sep}page=1", accessToken);
        if (!first.Ok) throw new EsiException(first.Error);
        Append(all, first.Body);

        for (var p = 2; p <= first.Pages; p++)
        {
            var r = await GetAsync($"{path}{sep}page={p}", accessToken);
            if (!r.Ok) throw new EsiException(r.Error);
            Append(all, r.Body);
        }
        return all;
    }

    private static void Append(List<JsonElement> into, string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
        foreach (var e in doc.RootElement.EnumerateArray()) into.Add(e.Clone());
    }

    private static void StoreCache(string url, string etag, string body, HttpResponseMessage resp)
    {
        // Expires-Header von CCP entspricht der dokumentierten Cache-Zeit der Route.
        var expires = resp.Content.Headers.Expires?.UtcDateTime
                      ?? resp.Headers.Date?.UtcDateTime.AddSeconds(60)
                      ?? Util.UtcNow.AddSeconds(60);
        Db.Run(@"INSERT INTO http_cache(url,etag,body,expires_utc,stored_utc) VALUES($u,$e,$b,$x,$s)
                 ON CONFLICT(url) DO UPDATE SET etag=$e, body=$b, expires_utc=$x, stored_utc=$s",
            ("$u", url), ("$e", (object)etag), ("$b", body),
            ("$x", Util.ToIso(expires)), ("$s", Util.NowIso()));
    }

    private static void TrackErrorLimit(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("X-Esi-Error-Limit-Remain", out var remv) &&
            int.TryParse(remv.FirstOrDefault(), out var remain) && remain < 10)
        {
            var reset = 60;
            if (resp.Headers.TryGetValues("X-Esi-Error-Limit-Reset", out var rsv) &&
                int.TryParse(rsv.FirstOrDefault(), out var rs)) reset = rs;
            _errorLimitUntil = Util.UtcNow.AddSeconds(reset + 1);
        }
    }

    private static async Task WaitForErrorLimit()
    {
        var wait = _errorLimitUntil - Util.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait);
    }

    private static string Trim(string s) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s[..300] : s);
}

public class EsiException : Exception
{
    public EsiException(string msg) : base(msg) { }
}
