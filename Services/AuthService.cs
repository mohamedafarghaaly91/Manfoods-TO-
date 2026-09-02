using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext db, ILogger<AuthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(User? User, string? FailReason)> ValidateAsync(string email, string password)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == email.ToLower());

        if (user == null)
        {
            var reason = $"No user found for email '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            return (null, reason);
        }
        // Bulk-created accounts start with no password set (pending activation
        // via the OTP flow) — reject the login attempt instead of letting
        // BCrypt.Verify throw on a null/empty hash.
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            var reason = $"User '{email.ToLower()}' has no password hash set.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            return (null, reason);
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            var reason = $"Password mismatch for '{email.ToLower()}'.";
            _logger.LogWarning("Login failed: {Reason}", reason);
            return (null, reason);
        }

        return (user, null);
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash)) return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync();
        return true;
    }
}
