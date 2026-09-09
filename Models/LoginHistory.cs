using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// One row per login attempt (Home and Admin alike) that reached a known
/// account with a password to check — written by AuthService.ValidateAsync,
/// the single choke point both AccountControllers' Login actions share. An
/// attempt against an unknown email is never logged (there's no account to
/// attach it to, and it would otherwise let this log be used to enumerate
/// registered emails); everything else — successful or not — is recorded.
/// </summary>
[Table("login_history")]
public class LoginHistory
{
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    // Denormalized so the log still reads correctly even if the account is
    // later renamed/deleted — the same reasoning UploadLog.UploadedBy uses.
    [Column("email")]
    public string Email { get; set; } = "";

    [Column("logged_in_at")]
    public DateTime LoggedInAt { get; set; } = DateTime.UtcNow;

    [Column("ip_address")]
    public string? IpAddress { get; set; }

    [Column("user_agent")]
    public string? UserAgent { get; set; }

    [Column("success")]
    public bool Success { get; set; } = true;

    /// <summary>"Home" or "Admin" — which login form the attempt came through.</summary>
    [Column("portal")]
    public string Portal { get; set; } = "";

    /// <summary>Short reason code (e.g. "wrong-password") for a failed attempt;
    /// null for a successful one. Never set from an unknown-email attempt —
    /// see the class summary.</summary>
    [Column("failure_reason")]
    public string? FailureReason { get; set; }
}
