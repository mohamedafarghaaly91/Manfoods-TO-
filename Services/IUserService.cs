using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IUserService
{
    Task<List<UserViewModel>> GetAllAsync();
    Task<UserViewModel?> GetByIdAsync(int id);
    /// <summary>Returns (null, "duplicate-email") when a user with this email
    /// already exists.</summary>
    Task<(UserViewModel? user, string? error)> CreateAsync(CreateUserViewModel vm);
    /// <summary>Returns (null, error) when the update is rejected — the user
    /// wasn't found, this is the last remaining Admin and the edit would take
    /// away their Admin role (would lock everyone out of user management), or
    /// another user already has the email being changed to
    /// ("duplicate-email").</summary>
    Task<(UserViewModel? user, string? error)> UpdateAsync(int id, EditUserViewModel vm);
    /// <summary>Returns (false, error) when deletion is rejected — this is the
    /// last remaining Admin account.</summary>
    Task<(bool success, string? error)> DeleteAsync(int id);
    Task<(int created, int skipped)> UploadBulkUsersAsync(IFormFile file);
    Task<bool> VerifyRecoveryKeyAsync(string key);
    Task<bool> ResetAdminPasswordAsync(string email, string newPassword);
    /// <summary>Regenerates the shared admin recovery key after verifying the
    /// requesting admin's own password. Returns the new plaintext key (shown
    /// once) or null if the password didn't match.</summary>
    Task<string?> RegenerateRecoveryKeyAsync(string requestingEmail, string password);
}
