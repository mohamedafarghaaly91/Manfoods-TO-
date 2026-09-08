using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuthService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContext;

    // Per-account lockout on top of the per-IP "login" rate limiter: the
    // limiter caps request *volume* from one IP, but a patient attacker
    // spread across IPs (or just slow) could otherwise keep guessing one
    // known account indefinitely. Reuses IMemoryCache — already registered
    // (AddMemoryCache) and used elsewhere in the app — rather than adding a
    // new store, mirroring the same "N failed attempts locks it out" shape
    // OtpService already uses for password-reset codes.
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);
    private static string FailKey(string email) => $"login-fail:{email.Trim().ToLowerInvariant()}";

    public AuthService(AppDbContext db, ILogger<AuthService> logger, IMemoryCache cache, IHttpContextAccessor httpContext)
    {
        _db = db;
        _logger = logger;
        _cache = cache;
        _httpContext = httpContext;
    }

    public async Task<(User? User, string? FailReason)> ValidateAsync(string email, string password)
    {
        var failKey = FailKey(email);
        if (_cache.TryGetValue(failKey, out int failCount) && failCount >= MaxFailedAttempts)
        {
            _logger.LogWarning("Login blocked: too many recent failed attempts for '{Email}'.", email.ToLower());
            // Same generic reason shape as every other failure below — the
            // caller already discards this and always shows one generic
            // "invalid credentials" message, so a locked account is not
            // distinguishable from a wrong password (no extra enumeration
            // signal from the lockout itself).
            return (null, "Account temporarily locked after repeated failed attempts.");
        }

        void RecordFailure() => _cache.Set(failKey, failCount + 1, LockoutWindow);

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == email.ToLower());

        if (user == null)
        {
            var reason = $"No user found for email '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            RecordFailure();
            return (null, reason);
        }
        // Bulk-created accounts start with no password set (pending activation
        // via the OTP flow) — reject the login attempt instead of letting
        // BCrypt.Verify throw on a null/empty hash.
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            var reason = $"User '{email.ToLower()}' has no password hash set.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            RecordFailure();
            return (null, reason);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            var reason = $"Password mismatch for '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            RecordFailure();
            return (null, reason);
        }

        _cache.Remove(failKey);
        await LogLoginAsync(user);
        return (user, null);
    }

    // Records the login the moment credentials check out — shared by both the
    // Home and Admin AccountControllers' Login actions, since they both call
    // ValidateAsync. Best-effort: a failure here must never block the login
    // itself, so it's swallowed (logged) rather than surfaced to the caller.
    private async Task LogLoginAsync(User user)
    {
        try
        {
            var http = _httpContext.HttpContext;
            _db.LoginHistories.Add(new LoginHistory
            {
                UserId = user.Id,
                Email = user.Email,
                IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = http?.Request.Headers.UserAgent.ToString(),
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record login history for user {UserId}.", user.Id);
        }
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash)) return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();
        return true;
    }
}
