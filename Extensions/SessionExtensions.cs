using Microsoft.Extensions.Caching.Memory;

namespace MvcApp.Extensions;

public static class SessionExtensions
{
    /// <summary>Name of the session cookie — shared with Program.cs's AddSession
    /// config so both places (and the fixation-mitigation logic below, which
    /// has to delete this exact cookie) stay in sync from one source.</summary>
    public const string SessionCookieName = "wicrewsession";

    public static void SetUserSession(this ISession session, int userId, string email, string role, string? assignedName)
    {
        session.SetInt32("UserId", userId);
        session.SetString("Email", email);
        session.SetString("Role", role);
        session.SetString("AssignedName", assignedName ?? "");
    }

    public static int? GetUserId(this ISession session) => session.GetInt32("UserId");
    public static string GetEmail(this ISession session) => session.GetString("Email") ?? "";
    public static string GetRole(this ISession session) => session.GetString("Role") ?? "";
    public static string? GetAssignedName(this ISession session)
    {
        var v = session.GetString("AssignedName");
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public static bool IsAdmin(this ISession session) => session.GetRole() == "Admin";

    private record struct PendingLogin(int UserId, string Email, string Role, string? AssignedName);
    private const string PendingLoginPrefix = "pending-login:";

    /// <summary>
    /// Session-fixation mitigation: ASP.NET Core's session middleware has no
    /// public "regenerate session ID" call — it decides the session key once,
    /// from the incoming request's cookie, before the controller ever runs.
    /// So instead of writing the freshly-authenticated identity into whatever
    /// session (possibly attacker-fixed) the request arrived with, this
    /// discards that session entirely, stashes the identity behind a random
    /// one-time token in the already-registered IMemoryCache, and hands the
    /// token back so the caller can redirect. The follow-up request then
    /// arrives with no session cookie at all, which forces the middleware
    /// down its normal "no cookie present" path — the same path it always
    /// uses to issue a brand-new, unguessable session ID — and
    /// CompleteSessionRotation below writes the identity into that new
    /// session. Returns the one-time token to redirect with.
    /// </summary>
    public static string BeginSessionRotation(this HttpContext context, IMemoryCache cache, int userId, string email, string role, string? assignedName)
    {
        // Deliberately does NOT touch context.Session (no Clear()/Set* call):
        // doing so would mark the old session dirty and could race the
        // explicit cookie deletion below against the session middleware's own
        // end-of-request cookie-write for that (soon-abandoned) session. Since
        // the old session's identity is never written to and its cookie is
        // deleted here, it simply becomes orphaned — harmless whether or not
        // it was attacker-fixed.
        context.Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });

        var token = Guid.NewGuid().ToString("N");
        cache.Set(PendingLoginPrefix + token, new PendingLogin(userId, email, role, assignedName), TimeSpan.FromSeconds(60));
        return token;
    }

    /// <summary>Redeems a one-time token from BeginSessionRotation into the
    /// current (now-guaranteed-fresh) session. Returns false if the token is
    /// missing, expired, or already used.</summary>
    public static bool CompleteSessionRotation(this HttpContext context, IMemoryCache cache, string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var key = PendingLoginPrefix + token;
        if (!cache.TryGetValue(key, out PendingLogin pending)) return false;
        cache.Remove(key);
        context.Session.SetUserSession(pending.UserId, pending.Email, pending.Role, pending.AssignedName);
        return true;
    }
}
