using MvcApp.Models;

namespace MvcApp.Services;

public interface IAuthService
{
    Task<(User? User, string? FailReason)> ValidateAsync(string email, string password);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
    /// <summary>Sets a new password without verifying the old one — only for the
    /// forced first-login flow (a temporary/OTP-issued password), where the
    /// caller has already proven possession of the current credential by
    /// authenticating with it to reach this session in the first place.</summary>
    Task<bool> SetPasswordAsync(int userId, string newPassword);
}
