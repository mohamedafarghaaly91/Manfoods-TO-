namespace MvcApp.Services;

/// <summary>
/// Single source of truth for the Super Admin identity. Only this account may
/// generate/regenerate/use the Master Admin Recovery Key, or issue
/// password-reset OTPs for other Admin accounts. Enforced server-side
/// wherever recovery-key or admin-OTP actions are gated — never trust a
/// client-supplied email/role/parameter for this check.
/// </summary>
public static class SuperAdminPolicy
{
    public const string SuperAdminEmail = "admin@mcd.com";

    public static bool IsSuperAdmin(string? email) =>
        string.Equals(email?.Trim(), SuperAdminEmail, StringComparison.OrdinalIgnoreCase);
}
