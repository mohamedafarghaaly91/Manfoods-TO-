using MvcApp.Models;

namespace MvcApp.Services;

public interface IAuthService
{
    Task<(User? User, string? FailReason)> ValidateAsync(string email, string password);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}
