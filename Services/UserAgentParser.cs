namespace MvcApp.Services;

/// <summary>
/// Turns a raw User-Agent header into a short "Browser · OS" label for the
/// Login History page. Deliberately simple string-matching (not a full UA
/// database) — good enough for "which browser/OS did this login come from"
/// at a glance, not for feature-detection or analytics-grade accuracy.
/// </summary>
public static class UserAgentParser
{
    public static (string Browser, string Os) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return ("Unknown", "Unknown");
        var ua = userAgent;

        // Order matters: every Chromium-based browser also carries "Chrome"
        // and "Safari" tokens for backwards compatibility, so the more
        // specific token (Edg/, OPR/) has to be checked first.
        string browser =
            ua.Contains("Edg/") ? "Edge" :
            ua.Contains("OPR/") || ua.Contains("Opera") ? "Opera" :
            ua.Contains("Chrome/") ? "Chrome" :
            ua.Contains("Firefox/") ? "Firefox" :
            ua.Contains("Safari/") ? "Safari" :
            "Unknown";

        string os =
            ua.Contains("Windows") ? "Windows" :
            ua.Contains("Android") ? "Android" :
            ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS" :
            ua.Contains("Macintosh") || ua.Contains("Mac OS X") ? "macOS" :
            ua.Contains("Linux") ? "Linux" :
            "Unknown";

        return (browser, os);
    }
}
