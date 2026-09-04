using System.Security.Cryptography;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly ISessionValidationService _sessionValidation;

    public UserService(AppDbContext db, IStoreAccessService storeAccess, ISessionValidationService sessionValidation)
    {
        _db = db;
        _storeAccess = storeAccess;
        _sessionValidation = sessionValidation;
    }

    private static UserViewModel ToVm(User u) => new()
    {
        Id = u.Id, Email = u.Email, Phone = u.Phone, Role = u.Role, AssignedName = u.AssignedName ?? "",
        HasPassword = !string.IsNullOrEmpty(u.PasswordHash), CreatedAt = u.CreatedAt
    };

    public async Task<List<UserViewModel>> GetAllAsync()
    {
        var users = (await _db.Users.OrderBy(u => u.CreatedAt).ToListAsync()).Select(ToVm).ToList();

        // Store-restricted roles only — Admin/User aren't matched against
        // StoreReference, so leave MatchedStoreCount null for them (there's
        // nothing meaningful to count).
        foreach (var u in users.Where(u => _storeAccess.IsRestrictedRole(u.Role)))
        {
            var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(u.Role, u.Email);
            u.MatchedStoreCount = accessible?.Count ?? 0;
        }

        return users;
    }

    public async Task<UserViewModel?> GetByIdAsync(int id)
    {
        var u = await _db.Users.FindAsync(id);
        return u == null ? null : ToVm(u);
    }

    public async Task<(UserViewModel? user, string? error)> CreateAsync(CreateUserViewModel vm)
    {
        var email = vm.Email.ToLower();
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return (null, "duplicate-email");

        var user = new User
        {
            Email = email,
            Phone = vm.Phone,
            AssignedName = vm.AssignedName?.Trim() ?? "",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password),
            Role = vm.Role,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return (ToVm(user), null);
    }

    private async Task<bool> IsLastAdminAsync() => await _db.Users.CountAsync(u => u.Role == "Admin") <= 1;

    public async Task<(UserViewModel? user, string? error)> UpdateAsync(int id, EditUserViewModel vm)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return (null, null);

        if (user.Role == "Admin" && vm.Role != "Admin" && await IsLastAdminAsync())
            return (null, "last-admin");

        var email = vm.Email.ToLower();
        if (email != user.Email && await _db.Users.AnyAsync(u => u.Id != id && u.Email == email))
            return (null, "duplicate-email");

        user.Email = email;
        user.Phone = vm.Phone;
        user.AssignedName = vm.AssignedName?.Trim() ?? "";
        user.Role = vm.Role;
        if (!string.IsNullOrEmpty(vm.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password);
        await _db.SaveChangesAsync();
        // A role change (or the account being edited at all) must not keep
        // working off a stale cached session-validation result — see
        // SessionValidationService.
        _sessionValidation.Invalidate(id);
        return (ToVm(user), null);
    }

    public async Task<(bool success, string? error)> DeleteAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return (false, null);
        if (user.Role == "Admin" && await IsLastAdminAsync())
            return (false, "last-admin");

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        _sessionValidation.Invalidate(id);
        return (true, null);
    }

    public async Task<bool> VerifyRecoveryKeyAsync(string key)
    {
        var setting = await _db.AppSettings.FindAsync("admin_recovery_key_hash");
        if (setting == null || string.IsNullOrEmpty(setting.Value)) return false;
        return BCrypt.Net.BCrypt.Verify(key, setting.Value);
    }

    // The Master Recovery Key is an emergency mechanism for the single Super
    // Admin (admin@mcd.com) only — it must never be usable to take over any
    // other Admin account, even by someone who legitimately holds the key.
    // Other Admins get a password reset via a Super-Admin-issued OTP instead
    // (see OtpService.GenerateAdminResetOtpAsync/VerifyAndResetAdminPasswordAsync).
    public async Task<bool> ResetAdminPasswordAsync(string email, string newPassword)
    {
        if (!SuperAdminPolicy.IsSuperAdmin(email)) return false;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower() && u.Role == "Admin");
        if (user == null) return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string GenerateRecoveryKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // Only the Super Admin may generate/regenerate the Master Recovery Key —
    // enforced here server-side (not just hidden in the UI), independent of
    // the re-authentication (current password) check below.
    public async Task<string?> RegenerateRecoveryKeyAsync(string requestingEmail, string password)
    {
        if (!SuperAdminPolicy.IsSuperAdmin(requestingEmail)) return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == requestingEmail.ToLower() && u.Role == "Admin");
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return null;

        var key = GenerateRecoveryKey();
        var hash = BCrypt.Net.BCrypt.HashPassword(key);

        var setting = await _db.AppSettings.FindAsync("admin_recovery_key_hash");
        if (setting == null) _db.AppSettings.Add(new AppSetting { Key = "admin_recovery_key_hash", Value = hash });
        else setting.Value = hash;
        await _db.SaveChangesAsync();

        return key;
    }

    private static string Col(IXLRow row, IXLWorksheet ws, params string[] names)
    {
        foreach (var name in names)
        {
            var col = ws.Row(1).Cells().FirstOrDefault(c => c.GetString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            if (col != null) return row.Cell(col.Address.ColumnNumber).GetString().Trim();
        }
        return "";
    }

    private static readonly string[] ValidRolesArray =
    {
        "Admin", "User", "Operation_Manager", "Operation_Consultant",
        "Head_Manager", "Senior_Operation_Consultant", "Operation_Director",
    };

    public IReadOnlyList<string> ValidRoles => ValidRolesArray;

    public async Task<(int created, int skipped)> UploadBulkUsersAsync(IFormFile file)
    {
        const long maxBytes = 10 * 1024 * 1024;
        if (file.Length > maxBytes) throw new InvalidOperationException("File size exceeds the 10 MB limit.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls") throw new InvalidOperationException("Only Excel files (.xlsx / .xls) are allowed.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        var existingEmails = (await _db.Users.Select(u => u.Email).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = new List<User>();
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var email = Col(row, ws, "Email", "email").ToLower();
            var phone = Col(row, ws, "Phone", "Phone Number", "phone");
            var assignedName = Col(row, ws, "Assigned Name", "AssignedName", "Name", "Display Name");
            var roleRaw = Col(row, ws, "Role", "role");
            if (string.IsNullOrWhiteSpace(email)) continue;

            // Skip accounts that already exist (by email) so re-uploading a
            // file never overwrites an activated user, and skip in-file
            // duplicates too.
            if (existingEmails.Contains(email) || !seenInFile.Add(email)) { skipped++; continue; }

            // A blank Role cell defaults to "User" (unchanged). A non-blank
            // Role that doesn't match a known value is kept as-is (not
            // silently coerced to "User") so the account gets no data access
            // (see StoreAccessService) and the typo stays visible to Admin.
            var matched = ValidRolesArray.FirstOrDefault(r => string.Equals(r, roleRaw, StringComparison.OrdinalIgnoreCase));
            var role = matched ?? (string.IsNullOrWhiteSpace(roleRaw) ? "User" : roleRaw);
            toAdd.Add(new User { Email = email, Phone = phone, AssignedName = assignedName, Role = role, PasswordHash = null });
        }

        if (toAdd.Count > 0)
        {
            await _db.Users.AddRangeAsync(toAdd);
            await _db.SaveChangesAsync();
        }

        return (toAdd.Count, skipped);
    }
}
