using System.Globalization;
using System.Text;

namespace EveIskTracker;

public static class Util
{
    public static DateTime UtcNow => DateTime.UtcNow;

    public static string NowIso() => UtcNow.ToString("o", CultureInfo.InvariantCulture);

    public static string ToIso(DateTime dt) =>
        dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    public static DateTime ParseIso(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DateTime.MinValue;
        return DateTime.Parse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    /// <summary>
    /// Deutsches Zahlenformat von Hand gebaut. Die App läuft mit InvariantGlobalization,
    /// damit die EXE ohne ICU-Bibliotheken auskommt — CultureInfo.GetCultureInfo("de-DE")
    /// würde dabei eine Ausnahme werfen.
    /// </summary>
    private static readonly NumberFormatInfo De = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = new[] { 3 },
        NegativeSign = "-",
    };

    /// <summary>1.234.567.890 -> "1,23 Mrd" — kurze Anzeige fürs Overlay.</summary>
    public static string IskShort(double v)
    {
        var de = De;
        var a = Math.Abs(v);
        var sign = v < 0 ? "-" : "";
        if (a >= 1e12) return sign + (a / 1e12).ToString("0.##", de) + " Bio";
        if (a >= 1e9) return sign + (a / 1e9).ToString("0.##", de) + " Mrd";
        if (a >= 1e6) return sign + (a / 1e6).ToString("0.##", de) + " Mio";
        if (a >= 1e3) return sign + (a / 1e3).ToString("0.#", de) + " K";
        return sign + a.ToString("0", de);
    }

    /// <summary>Voller Betrag mit Tausenderpunkten, z.B. "1.234.567.890 ISK".</summary>
    public static string IskFull(double v) => v.ToString("#,##0.##", De) + " ISK";

    public static string Sha256Base64Url(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(input)));
    }

    public static string RandomUrlSafe(int bytes = 32)
    {
        var buf = new byte[bytes];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
        return Base64Url(buf);
    }

    /// <summary>Windows-DPAPI: nur dasselbe Windows-Benutzerkonto kann wieder entschlüsseln.</summary>
    public static byte[] Protect(string plain)
    {
        if (!OperatingSystem.IsWindows()) return Encoding.UTF8.GetBytes(plain);
        return System.Security.Cryptography.ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), null,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
    }

    public static string Unprotect(byte[] blob)
    {
        if (blob == null || blob.Length == 0) return null;
        try
        {
            if (!OperatingSystem.IsWindows()) return Encoding.UTF8.GetString(blob);
            var raw = System.Security.Cryptography.ProtectedData.Unprotect(
                blob, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(raw);
        }
        catch { return null; }
    }
}
