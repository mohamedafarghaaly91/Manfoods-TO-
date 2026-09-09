using MvcApp.Models;

namespace MvcApp.Services;

public interface IAuthService
{
    /// <summary>`portal` is "Home" or "Admin" — which login form the attempt came
    /// through, recorded on the resulting login-history row (success or failure)
    /// for any attempt against a known account.</summary>
    Task<(User? User, string? FailReason)> ValidateAsync(string email, string password, string portal);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
    /// <summary>Sets a new password without verifying the old one — only for the
    /// forced first-login flow (a temporary/OTP-issued password), where the
    /// caller has already proven possession of the current credential by
    /// authenticating with it to reach this session in the first place.</summary>
    Task<bool> SetPasswordAsync(int userId, string newPassword);
}
